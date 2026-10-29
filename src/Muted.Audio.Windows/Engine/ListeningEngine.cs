using System.Diagnostics;
using System.Runtime.InteropServices;
using Muted.Audio.Windows.Dsp;
using Muted.Core.Dsp;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Muted.Audio.Windows.Engine;

/// <summary>What to clean and where to play it.</summary>
public sealed record ListeningOptions(
    string InputDeviceId,
    string OutputDeviceId,
    float Strength = 1f,
    int LatencyMilliseconds = 40)
{
    public ListeningOptions Normalize() => this with
    {
        Strength = float.IsFinite(Strength) ? Math.Clamp(Strength, 0f, 1f) : 1f,
        LatencyMilliseconds = Math.Clamp(LatencyMilliseconds, 20, 100)
    };
}

/// <summary>
/// Cleans what you hear. Your call app plays into a second virtual cable; this engine
/// records the cable's other side, runs it through RNNoise and plays it on your
/// headphones. It is independent of the microphone pipeline: either can run alone,
/// and a failure here never touches your microphone.
/// </summary>
public sealed class ListeningEngine : IAsyncDisposable
{
    private const int SampleRate = 48_000;
    private const int FrameSize = 480;
    private const int CapacitySamples = FrameSize * 16;
    private const int TargetSamples = FrameSize * 2;
    private const int HighWaterSamples = FrameSize * 6;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AutoResetEvent _inputReady = new(false);
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _inputDevice;
    private MMDevice? _outputDevice;
    private WasapiCapture? _capture;
    private WasapiOut? _render;
    private FloatRingBuffer? _inputBuffer;
    private RealtimeFloatWaveProvider? _outputProvider;
    private RnnoiseProcessor? _rnnoise;
    private CancellationTokenSource? _cancellation;
    private Thread? _thread;
    private ListeningOptions? _options;
    private float _inputPeak;
    private float _outputPeak;
    private float _noiseReductionDb;
    private int _faultRaised;
    private int _disposed;

    public bool IsRunning { get; private set; }

    public float InputPeak => Volatile.Read(ref _inputPeak);

    public float OutputPeak => Volatile.Read(ref _outputPeak);

    /// <summary>How much RNNoise takes out of what you hear, smoothed, in dB.</summary>
    public float NoiseReductionDb => Volatile.Read(ref _noiseReductionDb);

    /// <summary>Raised once, from a background thread, when a device fails. The engine stops itself.</summary>
    public event EventHandler<Exception>? Faulted;

    public async Task StartAsync(ListeningOptions options, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        options = options.Normalize();
        if (string.Equals(options.InputDeviceId, options.OutputDeviceId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Pick different devices to listen from and to play on.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(StopCore, CancellationToken.None).ConfigureAwait(false);
            Volatile.Write(ref _options, options);
            Interlocked.Exchange(ref _faultRaised, 0);
            try
            {
                await Task.Run(() => StartCore(options), cancellationToken).ConfigureAwait(false);
                IsRunning = true;
            }
            catch
            {
                await Task.Run(StopCore, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Changes how much of the cleaned signal you hear, without a restart.</summary>
    public void UpdateStrength(float strength)
    {
        var current = Volatile.Read(ref _options);
        if (current is not null)
        {
            Volatile.Write(ref _options, (current with { Strength = strength }).Normalize());
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(StopCore).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void StartCore(ListeningOptions options)
    {
        _enumerator = new MMDeviceEnumerator();
        _inputDevice = _enumerator.GetDevice(options.InputDeviceId);
        _outputDevice = _enumerator.GetDevice(options.OutputDeviceId);
        _inputBuffer = new FloatRingBuffer(CapacitySamples);
        _outputProvider = new RealtimeFloatWaveProvider(
            SampleRate,
            CapacitySamples,
            TargetSamples,
            HighWaterSamples,
            TargetSamples);
        _rnnoise = new RnnoiseProcessor();

        // Shared mode converts whatever the cable runs at to 48 kHz mono, like the
        // microphone path. Voices do not need stereo.
        _capture = new WasapiCapture(_inputDevice, useEventSync: true, options.LatencyMilliseconds)
        {
            ShareMode = AudioClientShareMode.Shared,
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1)
        };
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;

        _render = new WasapiOut(_outputDevice, AudioClientShareMode.Shared, useEventSync: true, options.LatencyMilliseconds);
        _render.PlaybackStopped += OnPlaybackStopped;
        _render.Init(_outputProvider);

        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        _thread = new Thread(() => Process(cancellation.Token))
        {
            IsBackground = true,
            Name = "Muted listening",
            Priority = ThreadPriority.AboveNormal
        };
        _thread.Start();
        _capture.StartRecording();
        _outputProvider.SetLive(true);
        _render.Play();
    }

    private void Process(CancellationToken cancellationToken)
    {
        var input = new float[FrameSize];
        var wet = new float[FrameSize];
        var delayedDry = new float[FrameSize];
        var mixed = new float[FrameSize];
        var driftOutput = new float[FrameSize + 1];
        var dryDelay = new SampleDelayLine(RnnoiseProcessor.AlgorithmicDelaySamples);
        var smoothedReduction = 0f;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var buffer = _inputBuffer;
                var output = _outputProvider;
                var rnnoise = _rnnoise;
                var options = Volatile.Read(ref _options);
                if (buffer is null || output is null || rnnoise is null || options is null)
                {
                    return;
                }

                if (buffer.Count < FrameSize)
                {
                    _inputReady.WaitOne(10);
                    continue;
                }

                if (buffer.Read(input) != FrameSize)
                {
                    continue;
                }

                dryDelay.Process(input, delayedDry);
                rnnoise.Process(input, wet);
                AudioMath.Mix(delayedDry, wet, mixed, options.Strength);

                var dryRms = AudioMath.Rms(delayedDry);
                if (dryRms > 0.0015f)
                {
                    var reduction = 20f * MathF.Log10(dryRms / MathF.Max(AudioMath.Rms(wet), 1e-6f));
                    smoothedReduction = (smoothedReduction * 0.9f) + (Math.Clamp(reduction, 0f, 60f) * 0.1f);
                }
                else
                {
                    smoothedReduction *= 0.9f;
                }

                // The cable and the headphones run on different clocks, so keep the
                // buffer level steady one sample at a time, like the microphone path.
                var buffered = output.BufferedSamples;
                var correction = buffered < TargetSamples - FrameSize ? 1 : buffered > TargetSamples + FrameSize ? -1 : 0;
                var length = DriftCorrector.Process(mixed, driftOutput, correction);
                output.Write(driftOutput.AsSpan(0, length));

                Volatile.Write(ref _inputPeak, AudioMath.Peak(input));
                Volatile.Write(ref _outputPeak, AudioMath.Peak(mixed));
                Volatile.Write(ref _noiseReductionDb, smoothedReduction);
            }
        }
        catch (Exception exception)
        {
            RaiseFaulted(exception);
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        var buffer = _inputBuffer;
        if (!ReferenceEquals(sender, _capture) || buffer is null || eventArgs.BytesRecorded <= 0)
        {
            return;
        }

        var usable = eventArgs.BytesRecorded - (eventArgs.BytesRecorded % sizeof(float));
        buffer.Write(MemoryMarshal.Cast<byte, float>(eventArgs.Buffer.AsSpan(0, usable)));
        _inputReady.Set();
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs eventArgs)
    {
        if (ReferenceEquals(sender, _capture) && IsRunning)
        {
            RaiseFaulted(eventArgs.Exception ?? new InvalidOperationException("The listening input stopped unexpectedly."));
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs eventArgs)
    {
        if (ReferenceEquals(sender, _render) && IsRunning)
        {
            RaiseFaulted(eventArgs.Exception ?? new InvalidOperationException("The listening output stopped unexpectedly."));
        }
    }

    private void RaiseFaulted(Exception exception)
    {
        if (Interlocked.Exchange(ref _faultRaised, 1) != 0 || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await StopAsync().ConfigureAwait(false);
            }
            catch
            {
                // Stopping after a fault is best effort.
            }

            Faulted?.Invoke(this, exception);
        });
    }

    private void StopCore()
    {
        IsRunning = false;
        _outputProvider?.SetLive(false);

        void Try(Action action)
        {
            try
            {
                action();
            }
            catch
            {
                // A device that will not close cleanly must not block the rest.
            }
        }

        var capture = _capture;
        _capture = null;
        if (capture is not null)
        {
            Try(() => capture.DataAvailable -= OnDataAvailable);
            Try(() => capture.RecordingStopped -= OnRecordingStopped);
            Try(capture.Dispose);
        }

        var render = _render;
        _render = null;
        if (render is not null)
        {
            Try(() => render.PlaybackStopped -= OnPlaybackStopped);
            Try(render.Dispose);
        }

        _cancellation?.Cancel();
        _inputReady.Set();
        var thread = _thread;
        if (thread is not null && thread != Thread.CurrentThread)
        {
            thread.Join(TimeSpan.FromSeconds(2));
        }

        _thread = null;
        _cancellation?.Dispose();
        _cancellation = null;
        Try(() => _rnnoise?.Dispose());
        _rnnoise = null;
        _inputBuffer = null;
        _outputProvider = null;
        Try(() => _inputDevice?.Dispose());
        Try(() => _outputDevice?.Dispose());
        Try(() => _enumerator?.Dispose());
        _inputDevice = null;
        _outputDevice = null;
        _enumerator = null;
        Volatile.Write(ref _inputPeak, 0f);
        Volatile.Write(ref _outputPeak, 0f);
        Volatile.Write(ref _noiseReductionDb, 0f);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
        _inputReady.Dispose();
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using Muted.Audio.Windows.Dsp;
using Muted.Core.Audio;
using Muted.Core.Dsp;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Muted.Audio.Windows.Engine;

public sealed class RealtimeAudioEngine : IAsyncDisposable
{
    private const int SampleRate = 48_000;
    private const int FrameSize = 480;
    private const int PrebufferFrames = 4;
    private const int OutputCapacitySamples = FrameSize * 16;
    private const int InputCapacitySamples = FrameSize * 16;
    private const int SteadyOutputTargetSamples = FrameSize * 2;
    private const int OutputHighWaterSamples = FrameSize * 6;
    private const int MonitorHighWaterSamples = FrameSize * 8;

    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly AutoResetEvent _inputReady = new(false);
    private readonly object _faultTaskSync = new();
    private volatile AudioEngineState _state;
    private AudioEngineOptions? _options;
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _inputDevice;
    private MMDevice? _outputDevice;
    private MMDevice? _monitorDevice;
    private WasapiCapture? _capture;
    private WasapiOut? _render;
    private WasapiOut? _monitorRender;
    private FloatRingBuffer? _inputBuffer;
    private RealtimeFloatWaveProvider? _outputProvider;
    private RealtimeFloatWaveProvider? _monitorProvider;
    private RnnoiseProcessor? _rnnoise;
    private CancellationTokenSource? _processorCancellation;
    private Thread? _processorThread;
    private Exception? _pendingFault;
    private Task? _faultTask;
    private float _inputPeak;
    private float _outputPeak;
    private float _inputRms;
    private float _outputRms;
    private float _voiceProbability;
    private float _processingLoad;
    private float _noiseReductionDb;
    private float _limiterReductionDb;
    private float _autoGainDb;
    private int _faultHandling;
    private long _generation;
    private int _disposed;

    public event EventHandler<AudioEngineState>? StateChanged;

    public event EventHandler<Exception>? Faulted;

    /// <summary>Raised when only the monitor path failed; the main pipeline keeps running.</summary>
    public event EventHandler<Exception>? MonitorFaulted;

    public AudioEngineState State => _state;

    /// <summary>Rolling frame peaks for the live waveform in the UI.</summary>
    public WaveformScope Scope { get; } = new(256);

    public AudioMetrics Metrics
    {
        get
        {
            var input = _inputBuffer;
            var output = _outputProvider;
            return new AudioMetrics(
                Volatile.Read(ref _inputPeak),
                Volatile.Read(ref _outputPeak),
                Volatile.Read(ref _voiceProbability),
                Volatile.Read(ref _processingLoad),
                output is null ? 0 : output.BufferedSamples * 1_000d / SampleRate,
                input?.DroppedSamples ?? 0,
                output?.DroppedSamples ?? 0,
                output?.UnderrunSamples ?? 0,
                Volatile.Read(ref _inputRms),
                Volatile.Read(ref _outputRms),
                Volatile.Read(ref _noiseReductionDb),
                Volatile.Read(ref _limiterReductionDb),
                Volatile.Read(ref _autoGainDb),
                Volatile.Read(ref _monitorProvider) is not null);
        }
    }

    public async Task StartAsync(AudioEngineOptions options, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state is AudioEngineState.Running or AudioEngineState.Starting)
            {
                return;
            }

            if (_state != AudioEngineState.Stopped)
            {
                var priorCleanupException = CleanupCore();
                if (priorCleanupException is not null)
                {
                    throw new InvalidOperationException(
                        "The previous audio pipeline could not be fully cleaned up.",
                        priorCleanupException);
                }
            }

            var generation = Interlocked.Increment(ref _generation);
            Interlocked.Exchange(ref _pendingFault, null);
            Interlocked.Exchange(ref _faultHandling, 0);
            Volatile.Write(ref _options, options.Normalize());
            Scope.Clear();
            SetState(AudioEngineState.Starting);

            await Task.Run(
                    () => StartCore(Volatile.Read(ref _options)!, generation, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
            ThrowIfFaultedDuringStart();
            SetState(AudioEngineState.Running);
        }
        catch (Exception startException)
        {
            var cleanupException = CleanupCore();
            SetState(AudioEngineState.Faulted);
            if (cleanupException is not null)
            {
                throw new AggregateException(
                    "The audio pipeline could not start and could not be fully cleaned up.",
                    startException,
                    cleanupException);
            }

            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == AudioEngineState.Stopped)
            {
                return;
            }

            Interlocked.Increment(ref _generation);
            SetState(AudioEngineState.Stopping);
            var cleanupException = await Task.Run(CleanupCore, CancellationToken.None).ConfigureAwait(false);
            SetState(cleanupException is null ? AudioEngineState.Stopped : AudioEngineState.Faulted);
            if (cleanupException is not null)
            {
                throw cleanupException;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task RestartAsync(AudioEngineOptions options, CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);
        await StartAsync(options, cancellationToken).ConfigureAwait(false);
    }

    public void UpdateSuppression(SuppressionOptions suppression)
    {
        var current = Volatile.Read(ref _options);
        if (current is not null)
        {
            Volatile.Write(ref _options, current with { Suppression = suppression.Normalize() });
        }
    }

    /// <summary>
    /// Starts, stops, or re-points the monitor output without interrupting the
    /// pipeline that feeds the virtual cable.
    /// </summary>
    public async Task UpdateMonitorAsync(MonitorOptions monitor, CancellationToken cancellationToken = default)
    {
        monitor = monitor.Normalize();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = Volatile.Read(ref _options);
            if (current is not null)
            {
                Volatile.Write(ref _options, current with { Monitor = monitor });
            }

            if (_state != AudioEngineState.Running)
            {
                return;
            }

            var deviceChanged = !string.Equals(
                _monitorDevice?.ID,
                monitor.DeviceId,
                StringComparison.Ordinal);
            var isRunning = Volatile.Read(ref _monitorProvider) is not null;
            if (monitor.IsActive && isRunning && !deviceChanged)
            {
                return;
            }

            await Task.Run(
                    () =>
                    {
                        StopMonitorCore();
                        if (monitor.IsActive)
                        {
                            StartMonitorCore(monitor, Volatile.Read(ref _options)!.LatencyMilliseconds);
                        }
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            StopMonitorCore();
            RaiseMonitorFaulted(exception);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private void StartCore(
        AudioEngineOptions options,
        long generation,
        CancellationToken cancellationToken)
    {
        _enumerator = new MMDeviceEnumerator();
        _inputDevice = ResolveDevice(_enumerator, options.InputDeviceId, DataFlow.Capture);
        _outputDevice = ResolveDevice(_enumerator, options.OutputDeviceId, DataFlow.Render);

        _inputBuffer = new FloatRingBuffer(InputCapacitySamples);
        var startupPrebufferFrames = Math.Max(
            PrebufferFrames,
            (int)Math.Ceiling(options.LatencyMilliseconds / 10d) + 2);
        var startupPrebufferSamples = FrameSize * startupPrebufferFrames;
        _outputProvider = new RealtimeFloatWaveProvider(
            SampleRate,
            OutputCapacitySamples,
            SteadyOutputTargetSamples,
            OutputHighWaterSamples,
            startupPrebufferSamples);
        _rnnoise = new RnnoiseProcessor();

        _capture = new WasapiCapture(_inputDevice, useEventSync: true, options.LatencyMilliseconds)
        {
            ShareMode = AudioClientShareMode.Shared,
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1)
        };
        _capture.DataAvailable += OnCaptureDataAvailable;
        _capture.RecordingStopped += OnCaptureStopped;

        _render = new WasapiOut(
            _outputDevice,
            AudioClientShareMode.Shared,
            useEventSync: true,
            options.LatencyMilliseconds);
        _render.PlaybackStopped += OnPlaybackStopped;
        _render.Init(_outputProvider);

        var processorCancellation = new CancellationTokenSource();
        _processorCancellation = processorCancellation;
        var processorToken = processorCancellation.Token;
        _processorThread = new Thread(() => ProcessingLoop(generation, processorToken))
        {
            IsBackground = true,
            Name = "Muted RNNoise",
            Priority = ThreadPriority.AboveNormal
        };
        _processorThread.Start();
        _capture.StartRecording();

        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
        while (_outputProvider.BufferedSamples < startupPrebufferSamples && Stopwatch.GetTimestamp() < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfFaultedDuringStart();
            Thread.Sleep(2);
        }

        ThrowIfFaultedDuringStart();
        if (_outputProvider.BufferedSamples < startupPrebufferSamples)
        {
            throw new TimeoutException("The microphone did not provide enough audio for a clean start.");
        }

        _outputProvider.SetLive(true);
        _render.Play();
        ThrowIfFaultedDuringStart();

        if (options.Monitor?.IsActive == true)
        {
            try
            {
                StartMonitorCore(options.Monitor, options.LatencyMilliseconds);
            }
            catch (Exception exception)
            {
                // Losing the monitor must never keep the microphone off the cable.
                StopMonitorCore();
                RaiseMonitorFaulted(exception);
            }
        }
    }

    private void StartMonitorCore(MonitorOptions monitor, int latencyMilliseconds)
    {
        var enumerator = _enumerator;
        if (enumerator is null || string.IsNullOrWhiteSpace(monitor.DeviceId))
        {
            return;
        }

        _monitorDevice = enumerator.GetDevice(monitor.DeviceId);
        var provider = new RealtimeFloatWaveProvider(
            SampleRate,
            OutputCapacitySamples,
            SteadyOutputTargetSamples,
            MonitorHighWaterSamples,
            SteadyOutputTargetSamples);
        _monitorRender = new WasapiOut(
            _monitorDevice,
            AudioClientShareMode.Shared,
            useEventSync: true,
            latencyMilliseconds);
        _monitorRender.PlaybackStopped += OnMonitorPlaybackStopped;
        _monitorRender.Init(provider);
        provider.SetLive(true);
        Volatile.Write(ref _monitorProvider, provider);
        _monitorRender.Play();
    }

    private void StopMonitorCore()
    {
        var provider = Volatile.Read(ref _monitorProvider);
        Volatile.Write(ref _monitorProvider, null);
        provider?.SetLive(false);

        var render = _monitorRender;
        _monitorRender = null;
        if (render is not null)
        {
            try
            {
                render.PlaybackStopped -= OnMonitorPlaybackStopped;
                render.Dispose();
            }
            catch
            {
                // A monitor that refuses to shut down cleanly must not block the pipeline.
            }
        }

        var device = _monitorDevice;
        _monitorDevice = null;
        try
        {
            device?.Dispose();
        }
        catch
        {
            // Same reasoning as above.
        }
    }

    private void ProcessingLoop(long generation, CancellationToken cancellationToken)
    {
        var input = new float[FrameSize];
        var delayedDry = new float[FrameSize];
        var wet = new float[FrameSize];
        var mixed = new float[FrameSize];
        var monitorFrame = new float[FrameSize];
        var driftOutput = new float[FrameSize + 1];
        var dryDelay = new SampleDelayLine(RnnoiseProcessor.AlgorithmicDelaySamples);
        var voiceGate = new VoiceGate();
        var highPass = new HighPassFilter(SampleRate);
        var limiter = new SoftLimiter(SampleRate);
        var autoGain = new AutoGainControl(SampleRate / (float)FrameSize);
        var smoothedLoad = 0f;
        var smoothedReduction = 0f;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var inputBuffer = _inputBuffer;
                var outputProvider = _outputProvider;
                var rnnoise = _rnnoise;
                var suppression = Volatile.Read(ref _options)?.Suppression;
                var monitor = Volatile.Read(ref _options)?.Monitor;
                if (inputBuffer is null || outputProvider is null || rnnoise is null || suppression is null)
                {
                    return;
                }

                if (inputBuffer.Count < FrameSize)
                {
                    _inputReady.WaitOne(10);
                    continue;
                }

                if (inputBuffer.Read(input) != FrameSize)
                {
                    continue;
                }

                var started = Stopwatch.GetTimestamp();
                AudioMath.ApplyGainAndClamp(input, suppression.InputGain);
                var inputPeak = AudioMath.Peak(input);
                var inputRms = AudioMath.Rms(input);

                if (suppression.HighPassEnabled)
                {
                    highPass.SetFrequency(suppression.HighPassFrequency);
                    highPass.Process(input);
                }
                else
                {
                    highPass.Reset();
                }

                dryDelay.Process(input, delayedDry);
                var voiceProbability = rnnoise.Process(input, wet);

                AudioMath.Mix(
                    delayedDry,
                    wet,
                    mixed,
                    suppression.Enabled ? suppression.WetMix : 0f);
                voiceGate.Process(
                    mixed,
                    voiceProbability,
                    suppression.VoiceThreshold,
                    suppression.VoiceHoldMilliseconds,
                    suppression.Enabled && suppression.VoiceGateEnabled);
                autoGain.Process(
                    mixed,
                    voiceProbability,
                    suppression.AutoGainTargetDb,
                    suppression.AutoGainEnabled);
                AudioMath.ApplyGainAndClamp(mixed, suppression.OutputGain);
                limiter.Process(mixed, suppression.LimiterEnabled);
                if (suppression.IsMuted)
                {
                    mixed.AsSpan().Clear();
                }

                // How much RNNoise actually removed, measured before and after the model.
                var dryRms = AudioMath.Rms(delayedDry);
                var wetRms = AudioMath.Rms(wet);
                if (dryRms > 0.0015f && suppression.Enabled)
                {
                    var reduction = 20f * MathF.Log10(dryRms / MathF.Max(wetRms, 1e-6f));
                    smoothedReduction = (smoothedReduction * 0.9f) + (Math.Clamp(reduction, 0f, 60f) * 0.1f);
                }
                else
                {
                    smoothedReduction *= 0.9f;
                }

                var monitorProvider = Volatile.Read(ref _monitorProvider);
                if (monitorProvider is not null)
                {
                    var volume = monitor?.Volume ?? 0f;
                    mixed.AsSpan().CopyTo(monitorFrame);
                    if (volume < 0.999f)
                    {
                        AudioMath.ApplyGainAndClamp(monitorFrame, volume);
                    }

                    monitorProvider.Write(monitorFrame);
                }

                var target = outputProvider.TargetBufferedSamples;
                var buffered = outputProvider.BufferedSamples;
                var correction = buffered < target - FrameSize
                    ? 1
                    : buffered > target + FrameSize
                        ? -1
                        : 0;
                var outputLength = DriftCorrector.Process(mixed, driftOutput, correction);
                outputProvider.Write(driftOutput.AsSpan(0, outputLength));

                var outputPeak = AudioMath.Peak(mixed);
                var elapsedSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
                var currentLoad = (float)(elapsedSeconds / (FrameSize / (double)SampleRate));
                smoothedLoad = (smoothedLoad * 0.95f) + (currentLoad * 0.05f);
                Volatile.Write(ref _inputPeak, inputPeak);
                Volatile.Write(ref _outputPeak, outputPeak);
                Volatile.Write(ref _inputRms, inputRms);
                Volatile.Write(ref _outputRms, AudioMath.Rms(mixed));
                Volatile.Write(ref _voiceProbability, voiceProbability);
                Volatile.Write(ref _processingLoad, smoothedLoad);
                Volatile.Write(ref _noiseReductionDb, smoothedReduction);
                Volatile.Write(ref _limiterReductionDb, limiter.GainReductionDb);
                Volatile.Write(ref _autoGainDb, autoGain.CurrentGainDb);
                Scope.Push(outputPeak);
            }
        }
        catch (Exception exception)
        {
            HandleFault(exception, generation);
        }
    }

    private void OnCaptureDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        var generation = Volatile.Read(ref _generation);
        if (!ReferenceEquals(sender, _capture) || generation != Volatile.Read(ref _generation))
        {
            return;
        }

        var buffer = _inputBuffer;
        if (buffer is null || eventArgs.BytesRecorded <= 0)
        {
            return;
        }

        var usableBytes = eventArgs.BytesRecorded - (eventArgs.BytesRecorded % sizeof(float));
        var samples = MemoryMarshal.Cast<byte, float>(eventArgs.Buffer.AsSpan(0, usableBytes));
        buffer.Write(samples);
        _inputReady.Set();
    }

    private void OnCaptureStopped(object? sender, StoppedEventArgs eventArgs)
    {
        var generation = Volatile.Read(ref _generation);
        if (ReferenceEquals(sender, _capture) &&
            _state is AudioEngineState.Running or AudioEngineState.Starting)
        {
            HandleFault(
                eventArgs.Exception ?? new InvalidOperationException("The microphone stopped unexpectedly."),
                generation);
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs eventArgs)
    {
        var generation = Volatile.Read(ref _generation);
        if (ReferenceEquals(sender, _render) &&
            _state is AudioEngineState.Running or AudioEngineState.Starting)
        {
            HandleFault(
                eventArgs.Exception ?? new InvalidOperationException("The audio output stopped unexpectedly."),
                generation);
        }
    }

    private void OnMonitorPlaybackStopped(object? sender, StoppedEventArgs eventArgs)
    {
        if (!ReferenceEquals(sender, _monitorRender))
        {
            return;
        }

        Volatile.Write(ref _monitorProvider, null);
        if (eventArgs.Exception is not null &&
            _state is AudioEngineState.Running or AudioEngineState.Starting)
        {
            RaiseMonitorFaulted(eventArgs.Exception);
        }
    }

    private void HandleFault(Exception exception, long generation)
    {
        if (generation != Volatile.Read(ref _generation) ||
            _state is not (AudioEngineState.Starting or AudioEngineState.Running) ||
            Volatile.Read(ref _disposed) != 0 ||
            Interlocked.CompareExchange(ref _pendingFault, exception, null) is not null ||
            Interlocked.CompareExchange(ref _faultHandling, 1, 0) != 0)
        {
            return;
        }

        lock (_faultTaskSync)
        {
            if (generation == Volatile.Read(ref _generation) &&
                _state is AudioEngineState.Starting or AudioEngineState.Running &&
                Volatile.Read(ref _disposed) == 0)
            {
                _faultTask = Task.Run(() => ProcessFaultAsync(generation));
            }
        }
    }

    private async Task ProcessFaultAsync(long generation)
    {
        try
        {
            await _lifecycleGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (generation != Volatile.Read(ref _generation) ||
                    _state is not (AudioEngineState.Starting or AudioEngineState.Running))
                {
                    return;
                }

                var original = Volatile.Read(ref _pendingFault)
                    ?? new InvalidOperationException("The realtime audio pipeline stopped unexpectedly.");
                var cleanupException = await Task.Run(CleanupCore).ConfigureAwait(false);
                SetState(AudioEngineState.Faulted);
                RaiseFaulted(cleanupException is null
                    ? original
                    : new AggregateException(
                        "The realtime audio pipeline failed and could not be fully cleaned up.",
                        original,
                        cleanupException));
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }
        catch (ObjectDisposedException)
        {
            // The application is already shutting down.
        }
    }

    private void ThrowIfFaultedDuringStart()
    {
        var fault = Volatile.Read(ref _pendingFault);
        if (fault is not null)
        {
            throw new InvalidOperationException("The audio pipeline failed while starting.", fault);
        }
    }

    private Exception? CleanupCore()
    {
        List<Exception>? errors = null;
        void TryCleanup(Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
        }

        var outputProvider = _outputProvider;
        if (outputProvider is not null)
        {
            TryCleanup(() => outputProvider.SetLive(false));
        }

        TryCleanup(StopMonitorCore);

        var capture = _capture;
        _capture = null;
        if (capture is not null)
        {
            TryCleanup(() => capture.DataAvailable -= OnCaptureDataAvailable);
            TryCleanup(() => capture.RecordingStopped -= OnCaptureStopped);
            TryCleanup(capture.Dispose);
        }

        var render = _render;
        _render = null;
        if (render is not null)
        {
            TryCleanup(() => render.PlaybackStopped -= OnPlaybackStopped);
            TryCleanup(render.Dispose);
        }

        var processorCancellation = _processorCancellation;
        if (processorCancellation is not null)
        {
            TryCleanup(processorCancellation.Cancel);
        }

        TryCleanup(() => _inputReady.Set());
        var processorThread = _processorThread;
        var processorStopped = processorThread is null || !processorThread.IsAlive;
        if (!processorStopped)
        {
            if (processorThread == Thread.CurrentThread)
            {
                (errors ??= []).Add(
                    new InvalidOperationException("The RNNoise thread cannot clean up itself."));
            }
            else
            {
                try
                {
                    processorStopped = processorThread!.Join(TimeSpan.FromSeconds(2));
                }
                catch (Exception exception)
                {
                    (errors ??= []).Add(exception);
                }

                if (!processorStopped)
                {
                    (errors ??= []).Add(
                        new TimeoutException("The RNNoise thread did not stop within two seconds."));
                }
            }
        }

        var inputDevice = _inputDevice;
        _inputDevice = null;
        if (inputDevice is not null)
        {
            TryCleanup(inputDevice.Dispose);
        }

        var outputDevice = _outputDevice;
        _outputDevice = null;
        if (outputDevice is not null)
        {
            TryCleanup(outputDevice.Dispose);
        }

        var enumerator = _enumerator;
        _enumerator = null;
        if (enumerator is not null)
        {
            TryCleanup(enumerator.Dispose);
        }

        if (processorStopped)
        {
            _processorThread = null;
            _processorCancellation = null;
            if (processorCancellation is not null)
            {
                TryCleanup(processorCancellation.Dispose);
            }

            var rnnoise = _rnnoise;
            _rnnoise = null;
            if (rnnoise is not null)
            {
                TryCleanup(rnnoise.Dispose);
            }

            _inputBuffer = null;
            _outputProvider = null;
            Volatile.Write(ref _options, null);
        }

        Volatile.Write(ref _inputPeak, 0f);
        Volatile.Write(ref _outputPeak, 0f);
        Volatile.Write(ref _inputRms, 0f);
        Volatile.Write(ref _outputRms, 0f);
        Volatile.Write(ref _voiceProbability, 0f);
        Volatile.Write(ref _processingLoad, 0f);
        Volatile.Write(ref _noiseReductionDb, 0f);
        Volatile.Write(ref _limiterReductionDb, 0f);
        Volatile.Write(ref _autoGainDb, 0f);

        return errors switch
        {
            null or { Count: 0 } => null,
            { Count: 1 } => errors[0],
            _ => new AggregateException("Not all audio resources could be cleaned up.", errors)
        };
    }

    private static MMDevice ResolveDevice(MMDeviceEnumerator enumerator, string? id, DataFlow flow)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            return enumerator.GetDevice(id);
        }

        if (enumerator.HasDefaultAudioEndpoint(flow, Role.Communications))
        {
            return enumerator.GetDefaultAudioEndpoint(flow, Role.Communications);
        }

        return enumerator.GetDefaultAudioEndpoint(flow, Role.Console);
    }

    private void SetState(AudioEngineState state)
    {
        _state = state;
        var handlers = StateChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<AudioEngineState> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, state);
            }
            catch
            {
                // A UI/event subscriber must never take down the audio lifecycle.
            }
        }
    }

    private void RaiseFaulted(Exception exception) => Raise(Faulted, exception);

    private void RaiseMonitorFaulted(Exception exception) => Raise(MonitorFaulted, exception);

    private void Raise(EventHandler<Exception>? handlers, Exception exception)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<Exception> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, exception);
            }
            catch
            {
                // Fault reporting must remain isolated from the realtime engine.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Exception? disposeException = null;
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            disposeException = exception;
        }

        Task? faultTask;
        lock (_faultTaskSync)
        {
            faultTask = _faultTask;
        }

        if (faultTask is not null)
        {
            try
            {
                await faultTask.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                disposeException = disposeException is null
                    ? exception
                    : new AggregateException(disposeException, exception);
            }
        }

        if (_processorThread is not { IsAlive: true })
        {
            var finalCleanupException = CleanupCore();
            if (finalCleanupException is not null)
            {
                disposeException = disposeException is null
                    ? finalCleanupException
                    : new AggregateException(disposeException, finalCleanupException);
            }

            _inputReady.Dispose();
            _lifecycleGate.Dispose();
        }
        else
        {
            var processorThread = _processorThread;
            _ = Task.Run(() => FinalizeAfterProcessorExit(processorThread));
        }

        if (disposeException is not null)
        {
            ExceptionDispatchInfo.Capture(disposeException).Throw();
        }
    }

    private void FinalizeAfterProcessorExit(Thread processorThread)
    {
        try
        {
            processorThread.Join();
            _ = CleanupCore();
        }
        catch
        {
            // The process can still terminate; all immediately releasable resources were already cleaned.
        }
        finally
        {
            try
            {
                _inputReady.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                _lifecycleGate.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}

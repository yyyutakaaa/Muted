using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Muted.App.Services;

internal static class LocalAudioPlayer
{
    public static byte[] WaveBytes(float[] samples)
    {
        using var stream = new MemoryStream();
        // Writer owns its stream; ToArray is also valid after MemoryStream disposal.
        using (var writer = new WaveFileWriter(stream, new WaveFormat(48_000, 16, 1)))
            writer.WriteSamples(samples, 0, samples.Length);
        return stream.ToArray();
    }

    public static async Task PlayAsync(float[] samples, string deviceId, CancellationToken token)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDevice(deviceId);
        using var stream = new MemoryStream(WaveBytes(samples));
        using var reader = new WaveFileReader(stream);
        using var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 60);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (_, args) =>
        {
            if (args.Exception is not null) stopped.TrySetException(args.Exception);
            else stopped.TrySetResult();
        };
        output.Init(reader);
        token.ThrowIfCancellationRequested();
        output.Play();
        try { await stopped.Task.WaitAsync(token); }
        finally { output.Stop(); }
    }

    public static float[] Cue(bool opened)
    {
        var samples = new float[4_800];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = .08f * MathF.Sin(2 * MathF.PI * (opened ? 880 : 440) * i / 48_000) *
                MathF.Sin(MathF.PI * i / samples.Length);
        return samples;
    }
}

internal sealed class RecordedClipPlayer : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly MMDevice _device;
    private readonly MemoryStream _stream;
    private readonly WaveFileReader _reader;
    private readonly WasapiOut _output;
    private int _disposed;

    public RecordedClipPlayer(float[] samples, string deviceId)
    {
        _device = _enumerator.GetDevice(deviceId);
        _stream = new MemoryStream(LocalAudioPlayer.WaveBytes(samples));
        _reader = new WaveFileReader(_stream);
        _output = new WasapiOut(_device, AudioClientShareMode.Shared, true, 60);
        _output.Init(_reader);
    }

    public bool IsPlaying => _output.PlaybackState == PlaybackState.Playing;
    public double DurationSeconds => _reader.TotalTime.TotalSeconds;
    public double PositionSeconds
    {
        get => _reader.CurrentTime.TotalSeconds;
        set => _reader.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(value, 0, DurationSeconds));
    }

    public void Play()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (PositionSeconds >= DurationSeconds - .02) PositionSeconds = 0;
        _output.Play();
    }

    public void Pause() => _output.Pause();
    public void Rewind() => PositionSeconds = 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _output.Stop();
        _output.Dispose();
        _reader.Dispose();
        _stream.Dispose();
        _device.Dispose();
        _enumerator.Dispose();
    }
}

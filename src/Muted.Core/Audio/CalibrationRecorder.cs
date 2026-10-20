namespace Muted.Core.Audio;

/// <summary>
/// Collects the raw microphone (before any gain) and RNNoise's speech probability per
/// frame while the voice calibration runs. Single producer, preallocated, never writes
/// to disk; read only after <see cref="Finish"/>.
/// </summary>
public sealed class CalibrationRecorder
{
    public const int SampleRate = 48_000;
    public const int FrameSize = 480;

    private readonly float[] _samples;
    private readonly float[] _probabilities;
    private int _frames;
    private int _sealed;
    private int _writing;

    public CalibrationRecorder(int seconds)
    {
        if (seconds is < 1 or > 60)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        var frames = seconds * SampleRate / FrameSize;
        _samples = new float[frames * FrameSize];
        _probabilities = new float[frames];
    }

    public int CapacityFrames => _probabilities.Length;

    public int RecordedFrames => Volatile.Read(ref _frames);

    public double RecordedSeconds => RecordedFrames * FrameSize / (double)SampleRate;

    public bool Complete => RecordedFrames == _probabilities.Length;

    /// <summary>Called from the audio thread with exactly one frame.</summary>
    public void Write(ReadOnlySpan<float> rawFrame, float voiceProbability)
    {
        Interlocked.Exchange(ref _writing, 1);
        try
        {
            var frames = _frames;
            if (Volatile.Read(ref _sealed) != 0 || frames >= _probabilities.Length || rawFrame.Length != FrameSize)
            {
                return;
            }

            rawFrame.CopyTo(_samples.AsSpan(frames * FrameSize, FrameSize));
            _probabilities[frames] = voiceProbability;
            Volatile.Write(ref _frames, frames + 1);
        }
        finally
        {
            Volatile.Write(ref _writing, 0);
        }
    }

    public CalibrationRecording Finish()
    {
        Volatile.Write(ref _sealed, 1);
        var spinner = new SpinWait();
        while (Volatile.Read(ref _writing) != 0)
        {
            spinner.SpinOnce();
        }

        var frames = Volatile.Read(ref _frames);
        return new CalibrationRecording(
            _samples.AsSpan(0, frames * FrameSize).ToArray(),
            _probabilities.AsSpan(0, frames).ToArray());
    }
}

/// <summary>What the calibration captured: raw samples, and one probability per 480-sample frame.</summary>
public sealed record CalibrationRecording(float[] Samples, float[] VoiceProbabilities)
{
    public int Frames => VoiceProbabilities.Length;
}

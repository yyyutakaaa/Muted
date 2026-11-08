namespace Muted.Core.Audio;

/// <summary>
/// Collects the raw microphone (before any gain), the cleaned voice (after the noise
/// removal, before any gate) and RNNoise's speech probability per frame while the voice
/// calibration runs. Single producer, preallocated, never writes to disk. Frames below
/// <see cref="RecordedFrames"/> never change again, so they can be read while it runs.
/// </summary>
public sealed class CalibrationRecorder
{
    public const int SampleRate = 48_000;
    public const int FrameSize = 480;

    private readonly float[] _samples;
    private readonly float[] _cleaned;
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
        _cleaned = new float[frames * FrameSize];
        _probabilities = new float[frames];
    }

    public int CapacityFrames => _probabilities.Length;

    public int RecordedFrames => Volatile.Read(ref _frames);

    public double RecordedSeconds => RecordedFrames * FrameSize / (double)SampleRate;

    public bool Complete => RecordedFrames == _probabilities.Length;

    /// <summary>Called from the audio thread with exactly one frame.</summary>
    public void Write(ReadOnlySpan<float> rawFrame, float voiceProbability) =>
        Write(rawFrame, rawFrame, voiceProbability);

    /// <summary>Called from the audio thread with exactly one raw and one cleaned frame.</summary>
    public void Write(ReadOnlySpan<float> rawFrame, ReadOnlySpan<float> cleanedFrame, float voiceProbability)
    {
        Interlocked.Exchange(ref _writing, 1);
        try
        {
            var frames = _frames;
            if (Volatile.Read(ref _sealed) != 0 || frames >= _probabilities.Length ||
                rawFrame.Length != FrameSize || cleanedFrame.Length != FrameSize)
            {
                return;
            }

            rawFrame.CopyTo(_samples.AsSpan(frames * FrameSize, FrameSize));
            cleanedFrame.CopyTo(_cleaned.AsSpan(frames * FrameSize, FrameSize));
            _probabilities[frames] = voiceProbability;
            Volatile.Write(ref _frames, frames + 1);
        }
        finally
        {
            Volatile.Write(ref _writing, 0);
        }
    }

    /// <summary>The speech probabilities recorded so far, from <paramref name="fromFrame"/> on.</summary>
    public float[] ProbabilitiesSince(int fromFrame)
    {
        var frames = RecordedFrames;
        fromFrame = Math.Clamp(fromFrame, 0, frames);
        return _probabilities.AsSpan(fromFrame, frames - fromFrame).ToArray();
    }

    /// <summary>
    /// The raw microphone recorded so far from <paramref name="fromFrame"/> on, but no
    /// more than the newest <paramref name="maxFrames"/> frames.
    /// </summary>
    public float[] RawSince(int fromFrame, int maxFrames)
    {
        var frames = RecordedFrames;
        fromFrame = Math.Clamp(Math.Max(fromFrame, frames - maxFrames), 0, frames);
        return _samples.AsSpan(fromFrame * FrameSize, (frames - fromFrame) * FrameSize).ToArray();
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
            _probabilities.AsSpan(0, frames).ToArray())
        {
            Cleaned = _cleaned.AsSpan(0, frames * FrameSize).ToArray()
        };
    }
}

/// <summary>What the calibration captured: raw samples, and one probability per 480-sample frame.</summary>
public sealed record CalibrationRecording(float[] Samples, float[] VoiceProbabilities)
{
    /// <summary>The same moments after the noise removal; the raw samples when those were not kept.</summary>
    public float[] Cleaned { get; init; } = Samples;

    public int Frames => VoiceProbabilities.Length;
}

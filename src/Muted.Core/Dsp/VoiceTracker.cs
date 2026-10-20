namespace Muted.Core.Dsp;

/// <summary>
/// Slow, allocation-free statistics of the raw microphone for the automatic tuning:
/// how loud your speech is, how loud the room is, and how speech-like the room
/// sounds to RNNoise while you are quiet. Runs on the audio thread, once per frame.
/// </summary>
public sealed class VoiceTracker
{
    private const int Bins = 20;
    private const float SpeechProbability = 0.7f;
    private const float QuietProbability = 0.15f;
    private const int QuietFramesPerDecay = 1_000;

    private readonly float _speechCoefficient;
    private readonly float _noiseCoefficient;
    private readonly float[] _histogram = new float[Bins];
    private float _speechPower;
    private float _noiseDb = -70f;
    private bool _hasSpeech;
    private bool _hasNoise;
    private int _quietFramesSinceDecay;
    private int _framesSincePercentile;
    private float _quietProbabilityP95;
    private long _speechFrames;

    /// <param name="frameRate">Frames per second; the engine runs 100.</param>
    public VoiceTracker(float frameRate = 100f)
    {
        if (frameRate <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(frameRate));
        }

        // About four seconds of speech for the level, two for the room.
        _speechCoefficient = 1f - MathF.Exp(-1f / (4f * frameRate));
        _noiseCoefficient = 1f - MathF.Exp(-1f / (2f * frameRate));
    }

    /// <summary>Average speech level of the raw microphone, dBFS RMS. -100 until speech was heard.</summary>
    public float SpeechLevelDb => _hasSpeech ? 10f * MathF.Log10(MathF.Max(_speechPower, 1e-10f)) : -100f;

    /// <summary>Room level while you are quiet, dBFS RMS.</summary>
    public float NoiseFloorDb => _noiseDb;

    /// <summary>95th percentile of RNNoise's speech probability in quiet moments.</summary>
    public float QuietProbabilityP95 => _quietProbabilityP95;

    /// <summary>Frames counted as speech since construction; lets a caller see how much is new.</summary>
    public long SpeechFrames => _speechFrames;

    /// <param name="rawFrame">The microphone before any gain.</param>
    /// <param name="excluded">True while something abnormal is going on, such as a shout being blocked.</param>
    public void Process(ReadOnlySpan<float> rawFrame, float voiceProbability, bool excluded)
    {
        if (rawFrame.Length == 0 || !float.IsFinite(voiceProbability))
        {
            return;
        }

        var rms = AudioMath.Rms(rawFrame);
        if (!float.IsFinite(rms))
        {
            return;
        }

        var levelDb = rms <= 1e-5f ? -100f : 20f * MathF.Log10(rms);

        if (!excluded && voiceProbability >= SpeechProbability && levelDb > _noiseDb + 6f)
        {
            var power = rms * rms;
            _speechPower = _hasSpeech ? _speechPower + ((power - _speechPower) * _speechCoefficient) : power;
            _hasSpeech = true;
            _speechFrames++;
        }
        else if (voiceProbability < QuietProbability)
        {
            _noiseDb = _hasNoise ? _noiseDb + ((levelDb - _noiseDb) * _noiseCoefficient) : levelDb;
            _hasNoise = true;
        }

        // Quiet by level, not by probability: the point is to see how often the room
        // itself looks like speech.
        if (_hasNoise && levelDb < _noiseDb + 4f)
        {
            var bin = Math.Clamp((int)(voiceProbability * Bins), 0, Bins - 1);
            _histogram[bin] += 1f;
            if (++_quietFramesSinceDecay >= QuietFramesPerDecay)
            {
                _quietFramesSinceDecay = 0;
                for (var index = 0; index < Bins; index++)
                {
                    _histogram[index] *= 0.5f;
                }
            }
        }

        if (++_framesSincePercentile >= 50)
        {
            _framesSincePercentile = 0;
            _quietProbabilityP95 = Percentile95();
        }
    }

    public void Reset()
    {
        Array.Clear(_histogram);
        _speechPower = 0f;
        _noiseDb = -70f;
        _hasSpeech = false;
        _hasNoise = false;
        _quietFramesSinceDecay = 0;
        _framesSincePercentile = 0;
        _quietProbabilityP95 = 0f;
        _speechFrames = 0;
    }

    private float Percentile95()
    {
        var total = 0f;
        for (var index = 0; index < Bins; index++)
        {
            total += _histogram[index];
        }

        if (total < 1f)
        {
            return 0f;
        }

        var target = total * 0.95f;
        var running = 0f;
        for (var index = 0; index < Bins; index++)
        {
            running += _histogram[index];
            if (running >= target)
            {
                return (index + 1) / (float)Bins;
            }
        }

        return 1f;
    }
}

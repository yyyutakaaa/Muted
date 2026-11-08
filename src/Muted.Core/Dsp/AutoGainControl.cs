namespace Muted.Core.Dsp;

/// <summary>
/// Slow automatic gain control. It measures your average speech level over a few
/// seconds and sets one steady gain from that. Only frames RNNoise is sure are speech
/// count, so a quiet room never gets amplified up into hiss between sentences, and
/// soft word endings cannot drag the level up the way per-frame correction did.
/// </summary>
public sealed class AutoGainControl
{
    public const float MinimumTargetDb = -30f;
    public const float MaximumTargetDb = -6f;
    private const float MinimumGain = 0.25f;

    /// <summary>+12 dB. Speech that needs more is better fixed at the microphone, where it adds no noise.</summary>
    private const float MaximumGain = 4f;

    private const float SpeechFloorRms = 0.0025f;
    private const float SpeechProbability = 0.7f;

    private readonly float _levelCoefficient;
    private readonly float _attack;
    private readonly float _release;
    private float _gain = 1f;
    private float _speechPower;
    private bool _hasSpeech;

    /// <param name="frameRate">How often <see cref="Process"/> runs per second.</param>
    public AutoGainControl(float frameRate = 100f)
    {
        if (frameRate <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(frameRate));
        }

        // About three seconds of speech for the level.
        _levelCoefficient = 1f - MathF.Exp(-1f / (3f * frameRate));

        // Roughly a second to turn down, three to turn up: fast enough to catch a
        // louder voice, slow enough that it never pumps between words.
        _attack = 1f - MathF.Exp(-1f / frameRate);
        _release = _attack / 3f;
    }

    public float CurrentGain => _gain;

    public float CurrentGainDb => 20f * MathF.Log10(MathF.Max(_gain, 1e-6f));

    /// <summary>Applies the running gain to <paramref name="samples"/> and adapts it.</summary>
    public void Process(Span<float> samples, float voiceProbability, float targetDb, bool enabled)
    {
        if (!enabled)
        {
            if (Math.Abs(_gain - 1f) > 0.001f)
            {
                ApplyRamp(samples, _gain, 1f);
            }

            _gain = 1f;
            _hasSpeech = false;
            return;
        }

        var startGain = _gain;
        var rms = AudioMath.Rms(samples);
        if (float.IsFinite(rms) && rms > SpeechFloorRms && voiceProbability > SpeechProbability)
        {
            // Average power, not average correction: loud syllables weigh in fully.
            var power = rms * rms;
            _speechPower = _hasSpeech ? _speechPower + ((power - _speechPower) * _levelCoefficient) : power;
            _hasSpeech = true;
        }

        if (_hasSpeech)
        {
            var target = MathF.Pow(10f, Math.Clamp(targetDb, MinimumTargetDb, MaximumTargetDb) / 20f);
            var level = MathF.Sqrt(MathF.Max(_speechPower, 1e-12f));
            var desired = Math.Clamp(target / level, MinimumGain, MaximumGain);
            var coefficient = desired < _gain ? _attack : _release;
            _gain = Math.Clamp(_gain + ((desired - _gain) * coefficient), MinimumGain, MaximumGain);
        }

        ApplyRamp(samples, startGain, _gain);
    }

    public void Reset()
    {
        _gain = 1f;
        _speechPower = 0f;
        _hasSpeech = false;
    }

    private static void ApplyRamp(Span<float> samples, float startGain, float endGain)
    {
        if (samples.Length == 0)
        {
            return;
        }

        var step = (endGain - startGain) / samples.Length;
        var gain = startGain;
        for (var index = 0; index < samples.Length; index++)
        {
            gain += step;
            samples[index] = Math.Clamp(samples[index] * gain, -1f, 1f);
        }
    }
}

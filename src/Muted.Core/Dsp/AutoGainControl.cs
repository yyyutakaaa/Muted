namespace Muted.Core.Dsp;

/// <summary>
/// Slow automatic gain control. It only tracks while RNNoise reports speech, so a
/// quiet room never gets amplified up into hiss between sentences.
/// </summary>
public sealed class AutoGainControl
{
    public const float MinimumTargetDb = -30f;
    public const float MaximumTargetDb = -6f;
    private const float MinimumGain = 0.25f;
    private const float MaximumGain = 6f;
    private const float SpeechFloorRms = 0.0025f;

    private readonly float _attack;
    private readonly float _release;
    private float _gain = 1f;

    /// <param name="frameRate">How often <see cref="Process"/> runs per second.</param>
    public AutoGainControl(float frameRate = 100f)
    {
        if (frameRate <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(frameRate));
        }

        // Roughly a second to turn down, three to turn up: fast enough to catch a
        // shout, slow enough that it never pumps between words.
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
            return;
        }

        var startGain = _gain;
        var rms = AudioMath.Rms(samples);
        if (rms > SpeechFloorRms && voiceProbability > 0.5f)
        {
            var target = MathF.Pow(10f, Math.Clamp(targetDb, MinimumTargetDb, MaximumTargetDb) / 20f);
            var desired = Math.Clamp(target / MathF.Max(rms, 1e-6f), MinimumGain, MaximumGain);
            var coefficient = desired < _gain ? _attack : _release;
            _gain = Math.Clamp(_gain + ((desired - _gain) * coefficient), MinimumGain, MaximumGain);
        }

        ApplyRamp(samples, startGain, _gain);
    }

    public void Reset() => _gain = 1f;

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

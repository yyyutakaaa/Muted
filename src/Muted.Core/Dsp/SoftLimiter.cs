namespace Muted.Core.Dsp;

/// <summary>
/// Frame-based peak limiter. Hard clipping turns loud syllables into buzz, which gets
/// audible as soon as any input gain is applied. The gain needed for the whole frame is
/// worked out first, so the ceiling is never crossed, and a per-sample safety covers the
/// short ramp into a new gain.
/// </summary>
public sealed class SoftLimiter
{
    private const float Ceiling = 0.97f;

    private readonly int _attackSamples;
    private readonly float _releaseSamples;
    private float _gain = 1f;
    private float _gainReduction;

    public SoftLimiter(int sampleRate, float attackMilliseconds = 1.5f, float releaseMilliseconds = 120f)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        _attackSamples = Math.Max(1, (int)(attackMilliseconds * sampleRate / 1000f));
        _releaseSamples = MathF.Max(1f, releaseMilliseconds * sampleRate / 1000f);
    }

    /// <summary>The strongest gain reduction of the last processed frame, in dB.</summary>
    public float GainReductionDb => _gainReduction;

    public void Process(Span<float> samples, bool enabled)
    {
        if (samples.Length == 0)
        {
            return;
        }

        if (!enabled)
        {
            // Ease back to unity instead of jumping, so toggling stays inaudible.
            if (_gain < 1f)
            {
                Ramp(samples, _gain, 1f, samples.Length);
                _gain = 1f;
            }

            _gainReduction = 0f;
            return;
        }

        var peak = TruePeak(samples);
        var required = peak > Ceiling ? Ceiling / peak : 1f;
        var startGain = _gain;
        var endGain = required < _gain
            ? required
            : _gain + ((required - _gain) * (1f - MathF.Exp(-samples.Length / _releaseSamples)));
        endGain = Math.Clamp(endGain, 0.001f, 1f);

        var rampLength = endGain < startGain
            ? Math.Min(samples.Length, _attackSamples)
            : samples.Length;
        var lowestGain = Ramp(samples, startGain, endGain, rampLength);

        _gain = endGain;
        _gainReduction = lowestGain >= 1f ? 0f : -20f * MathF.Log10(lowestGain);
    }

    public void Reset()
    {
        _gain = 1f;
        _gainReduction = 0f;
    }

    /// <summary>Applies a gain ramp and returns the lowest gain that actually landed.</summary>
    private static float Ramp(Span<float> samples, float startGain, float endGain, int rampLength)
    {
        var lowestGain = 1f;
        for (var index = 0; index < samples.Length; index++)
        {
            var progress = index + 1 >= rampLength ? 1f : (index + 1) / (float)rampLength;
            var gain = startGain + ((endGain - startGain) * progress);

            // While the ramp is still catching up, this keeps the ceiling intact.
            var magnitude = MathF.Abs(samples[index]);
            if (magnitude > 1e-9f)
            {
                gain = MathF.Min(gain, Ceiling / magnitude);
            }

            if (gain < lowestGain)
            {
                lowestGain = gain;
            }

            samples[index] = Math.Clamp(samples[index] * gain, -1f, 1f);
        }

        return lowestGain;
    }

    private static float TruePeak(ReadOnlySpan<float> samples)
    {
        var peak = 0f;
        foreach (var sample in samples)
        {
            var magnitude = MathF.Abs(sample);
            if (magnitude > peak)
            {
                peak = magnitude;
            }
        }

        return peak;
    }
}

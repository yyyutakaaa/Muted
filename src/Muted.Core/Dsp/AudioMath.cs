namespace Muted.Core.Dsp;

public static class AudioMath
{
    public static float Peak(ReadOnlySpan<float> samples)
    {
        var peak = 0f;
        foreach (var sample in samples)
        {
            var absolute = MathF.Abs(sample);
            if (absolute > peak)
            {
                peak = absolute;
            }
        }

        return Math.Clamp(peak, 0f, 1f);
    }

    public static void ApplyGainAndClamp(Span<float> samples, float gain)
    {
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = Math.Clamp(samples[index] * gain, -1f, 1f);
        }
    }

    public static void Mix(
        ReadOnlySpan<float> dry,
        ReadOnlySpan<float> wet,
        Span<float> destination,
        float wetMix)
    {
        if (dry.Length != wet.Length || wet.Length != destination.Length)
        {
            throw new ArgumentException("Dry, wet and destination buffers must have the same size.");
        }

        wetMix = Math.Clamp(wetMix, 0f, 1f);
        var dryMix = 1f - wetMix;
        for (var index = 0; index < destination.Length; index++)
        {
            destination[index] = Math.Clamp(
                (dry[index] * dryMix) + (wet[index] * wetMix),
                -1f,
                1f);
        }
    }
}

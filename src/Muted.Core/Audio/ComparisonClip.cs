namespace Muted.Core.Audio;

/// <summary>Joins the raw and the cleaned take into one file: before, a short pause, after.</summary>
public static class ComparisonClip
{
    public const int SampleRate = 48_000;

    public static float[] Build(ReadOnlySpan<float> before, ReadOnlySpan<float> after, double gapSeconds = 0.75)
    {
        var gap = (int)Math.Round(Math.Clamp(gapSeconds, 0, 5) * SampleRate);
        var result = new float[before.Length + gap + after.Length];
        before.CopyTo(result);
        after.CopyTo(result.AsSpan(before.Length + gap));
        return result;
    }
}

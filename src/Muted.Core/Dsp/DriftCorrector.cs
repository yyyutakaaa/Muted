namespace Muted.Core.Dsp;

public static class DriftCorrector
{
    /// <summary>
    /// Resamples one short frame to one sample fewer, the same length, or one sample more.
    /// </summary>
    public static int Process(ReadOnlySpan<float> input, Span<float> output, int correction)
    {
        if (correction is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(correction));
        }

        var outputLength = input.Length + correction;
        if (input.Length < 2 || output.Length < outputLength)
        {
            throw new ArgumentException("Buffers are too small for drift correction.");
        }

        if (correction == 0)
        {
            input.CopyTo(output);
            return input.Length;
        }

        var scale = (input.Length - 1f) / (outputLength - 1f);
        for (var index = 0; index < outputLength; index++)
        {
            var sourcePosition = index * scale;
            var left = (int)sourcePosition;
            var right = Math.Min(left + 1, input.Length - 1);
            var fraction = sourcePosition - left;
            output[index] = input[left] + ((input[right] - input[left]) * fraction);
        }

        return outputLength;
    }
}

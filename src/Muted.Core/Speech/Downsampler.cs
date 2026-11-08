namespace Muted.Core.Speech;

/// <summary>
/// Turns 48 kHz audio into the 16 kHz the speech models expect: a low-pass filter
/// below the new Nyquist frequency, then every third sample. Keeps its history between
/// calls, so a stream can be fed one frame at a time. Allocates nothing per call.
/// </summary>
public sealed class Downsampler
{
    public const int Factor = 3;
    private const int Taps = 63;

    private static readonly float[] Kernel = BuildKernel();

    private readonly float[] _history = new float[Taps];
    private int _phase;

    /// <summary>Output samples for <paramref name="inputLength"/> input samples, at most.</summary>
    public static int MaxOutput(int inputLength) => (inputLength / Factor) + 1;

    public void Reset()
    {
        Array.Clear(_history);
        _phase = 0;
    }

    /// <summary>Filters and decimates <paramref name="input"/>; returns how many samples it wrote.</summary>
    public int Process(ReadOnlySpan<float> input, Span<float> output)
    {
        var written = 0;
        for (var index = 0; index < input.Length; index++)
        {
            // Shift the newest sample into the history; the kernel is short, so a plain
            // copy is cheaper than the bookkeeping of a circular buffer.
            Array.Copy(_history, 1, _history, 0, _history.Length - 1);
            _history[^1] = input[index];

            if (++_phase < Factor)
            {
                continue;
            }

            _phase = 0;
            var sum = 0f;
            for (var tap = 0; tap < Taps; tap++)
            {
                sum += Kernel[tap] * _history[Taps - 1 - tap];
            }

            output[written++] = sum;
        }

        return written;
    }

    /// <summary>Downsamples a whole recording in one go.</summary>
    public static float[] Downsample(ReadOnlySpan<float> input)
    {
        var downsampler = new Downsampler();
        var output = new float[MaxOutput(input.Length)];
        var written = downsampler.Process(input, output);
        return output.AsSpan(0, written).ToArray();
    }

    private static float[] BuildKernel()
    {
        // Windowed sinc with its cut-off at 7.2 kHz of 48 kHz, so speech up to 7 kHz
        // survives and nothing above 8 kHz folds back.
        const double cutoff = 7_200.0 / 48_000.0;
        var kernel = new float[Taps];
        var middle = (Taps - 1) / 2.0;
        var sum = 0.0;
        for (var index = 0; index < Taps; index++)
        {
            var x = index - middle;
            var sinc = x == 0 ? 2 * cutoff : Math.Sin(2 * Math.PI * cutoff * x) / (Math.PI * x);
            var window = 0.42 - (0.5 * Math.Cos(2 * Math.PI * index / (Taps - 1))) +
                         (0.08 * Math.Cos(4 * Math.PI * index / (Taps - 1)));
            kernel[index] = (float)(sinc * window);
            sum += kernel[index];
        }

        for (var index = 0; index < Taps; index++)
        {
            kernel[index] = (float)(kernel[index] / sum);
        }

        return kernel;
    }
}

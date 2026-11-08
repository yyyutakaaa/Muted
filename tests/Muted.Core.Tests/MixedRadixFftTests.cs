using Muted.Core.Dsp;

namespace Muted.Core.Tests;

public sealed class MixedRadixFftTests
{
    [Theory]
    [InlineData(960)]
    [InlineData(64)]
    [InlineData(512)]
    [InlineData(480)]
    [InlineData(120)]
    public void MatchesTheDirectDft(int size)
    {
        var fft = new MixedRadixFft(size);
        var random = new Random(size);
        var samples = new float[size];
        for (var index = 0; index < size; index++)
        {
            samples[index] = (float)((random.NextDouble() * 2) - 1);
        }

        var real = new float[fft.HalfSize];
        var imaginary = new float[fft.HalfSize];
        fft.ForwardReal(samples, real, imaginary);

        var tolerance = size > 512 ? 2e-3 : 1e-4;
        for (var bin = 0; bin < fft.HalfSize; bin++)
        {
            double sumRe = 0, sumIm = 0;
            for (var sample = 0; sample < size; sample++)
            {
                var angle = 2 * Math.PI * bin * sample / size;
                sumRe += samples[sample] * Math.Cos(angle);
                sumIm -= samples[sample] * Math.Sin(angle);
            }

            Assert.InRange(real[bin], (float)sumRe - tolerance, (float)sumRe + tolerance);
            Assert.InRange(imaginary[bin], (float)sumIm - tolerance, (float)sumIm + tolerance);
        }
    }

    [Fact]
    public void RoundTripRestoresTheSignal()
    {
        var fft = new MixedRadixFft(960);
        var random = new Random(7);
        var samples = new float[960];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = (float)((random.NextDouble() * 2) - 1);
        }

        var real = new float[fft.HalfSize];
        var imaginary = new float[fft.HalfSize];
        fft.ForwardReal(samples, real, imaginary);

        var restored = new float[960];
        fft.InverseReal(real, imaginary, restored);

        for (var index = 0; index < samples.Length; index++)
        {
            Assert.InRange(restored[index], samples[index] - 1e-4f, samples[index] + 1e-4f);
        }
    }

    [Fact]
    public void AToneLandsOnItsBin()
    {
        var fft = new MixedRadixFft(960);
        var samples = new float[960];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = (float)Math.Sin(2 * Math.PI * 100 * index / 960);
        }

        var real = new float[fft.HalfSize];
        var imaginary = new float[fft.HalfSize];
        fft.ForwardReal(samples, real, imaginary);

        var magnitudes = new float[fft.HalfSize];
        var peak = 0;
        for (var bin = 0; bin < fft.HalfSize; bin++)
        {
            magnitudes[bin] = MathF.Sqrt((real[bin] * real[bin]) + (imaginary[bin] * imaginary[bin]));
            if (magnitudes[bin] > magnitudes[peak])
            {
                peak = bin;
            }
        }

        Assert.Equal(100, peak);
        // Every other bin is far below the peak.
        for (var bin = 0; bin < fft.HalfSize; bin++)
        {
            if (Math.Abs(bin - 100) <= 1)
            {
                continue;
            }

            Assert.True(magnitudes[bin] < magnitudes[100] * 0.02f, $"bin {bin} leaked {magnitudes[bin]}");
        }
    }
}

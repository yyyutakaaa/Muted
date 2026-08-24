using Muted.Core.Dsp;

namespace Muted.Core.Tests;

public sealed class FftTests
{
    private static (float[] Real, float[] Imaginary) NaiveDft(float[] real, float[] imaginary)
    {
        var size = real.Length;
        var outputReal = new float[size];
        var outputImaginary = new float[size];
        for (var bin = 0; bin < size; bin++)
        {
            double sumReal = 0;
            double sumImaginary = 0;
            for (var index = 0; index < size; index++)
            {
                var angle = -2.0 * Math.PI * bin * index / size;
                var cos = Math.Cos(angle);
                var sin = Math.Sin(angle);
                sumReal += (real[index] * cos) - (imaginary[index] * sin);
                sumImaginary += (real[index] * sin) + (imaginary[index] * cos);
            }

            outputReal[bin] = (float)sumReal;
            outputImaginary[bin] = (float)sumImaginary;
        }

        return (outputReal, outputImaginary);
    }

    [Fact]
    public void Forward_MatchesADirectTransform()
    {
        const int size = 64;
        var random = new Random(7);
        var real = new float[size];
        var imaginary = new float[size];
        for (var index = 0; index < size; index++)
        {
            real[index] = (float)(random.NextDouble() - 0.5);
            imaginary[index] = (float)(random.NextDouble() - 0.5);
        }

        var (expectedReal, expectedImaginary) = NaiveDft(real, imaginary);

        new Fft(size).Forward(real, imaginary);

        for (var bin = 0; bin < size; bin++)
        {
            Assert.Equal(expectedReal[bin], real[bin], precision: 4);
            Assert.Equal(expectedImaginary[bin], imaginary[bin], precision: 4);
        }
    }

    [Fact]
    public void RoundTrip_ReturnsTheOriginalSignal()
    {
        const int size = 1_024;
        var fft = new Fft(size);
        var random = new Random(11);
        var real = new float[size];
        var imaginary = new float[size];
        for (var index = 0; index < size; index++)
        {
            real[index] = (float)((random.NextDouble() * 2) - 1);
        }

        var expected = (float[])real.Clone();

        fft.Forward(real, imaginary);
        fft.Inverse(real, imaginary);

        for (var index = 0; index < size; index++)
        {
            Assert.Equal(expected[index], real[index], precision: 4);
            Assert.Equal(0f, imaginary[index], precision: 4);
        }
    }

    [Fact]
    public void Forward_PutsASineInItsOwnBin()
    {
        const int size = 256;
        const int bin = 9;
        var real = new float[size];
        var imaginary = new float[size];
        for (var index = 0; index < size; index++)
        {
            real[index] = (float)Math.Sin(2 * Math.PI * bin * index / size);
        }

        new Fft(size).Forward(real, imaginary);

        for (var index = 0; index < size; index++)
        {
            var magnitude = Math.Sqrt((real[index] * real[index]) + (imaginary[index] * imaginary[index]));
            var expected = index == bin || index == size - bin ? size / 2d : 0d;
            Assert.Equal(expected, magnitude, tolerance: 0.01);
        }
    }

    [Fact]
    public void Convolution_ThroughTheFrequencyDomainMatchesTheDirectResult()
    {
        // The echo canceller leans on this identity for its filtering.
        const int size = 32;
        var fft = new Fft(size);
        var random = new Random(3);
        var signal = new float[size];
        var kernel = new float[size];
        for (var index = 0; index < 16; index++)
        {
            signal[index] = (float)(random.NextDouble() - 0.5);
            kernel[index] = (float)(random.NextDouble() - 0.5);
        }

        var expected = new float[size];
        for (var n = 0; n < size; n++)
        {
            for (var k = 0; k <= n; k++)
            {
                expected[n] += signal[k] * kernel[n - k];
            }
        }

        var signalReal = (float[])signal.Clone();
        var signalImaginary = new float[size];
        var kernelReal = (float[])kernel.Clone();
        var kernelImaginary = new float[size];
        fft.Forward(signalReal, signalImaginary);
        fft.Forward(kernelReal, kernelImaginary);

        var productReal = new float[size];
        var productImaginary = new float[size];
        for (var index = 0; index < size; index++)
        {
            productReal[index] = (signalReal[index] * kernelReal[index]) -
                (signalImaginary[index] * kernelImaginary[index]);
            productImaginary[index] = (signalReal[index] * kernelImaginary[index]) +
                (signalImaginary[index] * kernelReal[index]);
        }

        fft.Inverse(productReal, productImaginary);

        for (var index = 0; index < size; index++)
        {
            Assert.Equal(expected[index], productReal[index], precision: 4);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(100)]
    public void Constructor_RejectsSizesThatAreNotPowersOfTwo(int size) =>
        Assert.Throws<ArgumentException>(() => new Fft(size));
}

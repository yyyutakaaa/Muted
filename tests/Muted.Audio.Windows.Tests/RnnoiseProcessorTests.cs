using Muted.Audio.Windows.Devices;
using Muted.Audio.Windows.Dsp;

namespace Muted.Audio.Windows.Tests;

public sealed class RnnoiseProcessorTests
{
    [Fact]
    public void NativeProcessor_ProcessesSilentFrame()
    {
        using var processor = new RnnoiseProcessor();
        var input = new float[RnnoiseProcessor.ExpectedFrameSize];
        var output = new float[RnnoiseProcessor.ExpectedFrameSize];

        var voiceProbability = processor.Process(input, output);

        Assert.Equal(RnnoiseProcessor.ExpectedFrameSize, processor.FrameSize);
        Assert.InRange(voiceProbability, 0f, 1f);
        Assert.All(output, sample => Assert.True(float.IsFinite(sample)));
        Assert.All(output, sample => Assert.InRange(sample, -1f, 1f));
    }

    [Fact]
    public void NativeProcessor_ReducesStationaryNoiseEnergy()
    {
        using var processor = new RnnoiseProcessor();
        var random = new Random(42);
        var input = new float[RnnoiseProcessor.ExpectedFrameSize];
        var output = new float[RnnoiseProcessor.ExpectedFrameSize];
        double inputEnergy = 0;
        double outputEnergy = 0;

        for (var frame = 0; frame < 120; frame++)
        {
            for (var index = 0; index < input.Length; index++)
            {
                input[index] = (float)((random.NextDouble() * 2d - 1d) * 0.08d);
            }

            processor.Process(input, output);
            if (frame < 20)
            {
                continue;
            }

            inputEnergy += input.Sum(sample => sample * sample);
            outputEnergy += output.Sum(sample => sample * sample);
        }

        Assert.True(
            outputEnergy < inputEnergy * 0.8d,
            $"Expected RNNoise to reduce stationary noise; input={inputEnergy}, output={outputEnergy}.");
    }

    [Fact]
    public void NativeProcessor_HasPinnedTwentyMillisecondStreamingDelay()
    {
        using var processor = new RnnoiseProcessor();
        const int frameCount = 6;
        const int impulseIndex = 240;
        var input = new float[RnnoiseProcessor.ExpectedFrameSize];
        var frameOutput = new float[RnnoiseProcessor.ExpectedFrameSize];
        var streamOutput = new float[frameCount * RnnoiseProcessor.ExpectedFrameSize];
        input[impulseIndex] = 10_000f / 32_768f;

        for (var frame = 0; frame < frameCount; frame++)
        {
            processor.Process(input, frameOutput);
            frameOutput.CopyTo(streamOutput, frame * frameOutput.Length);
            Array.Clear(input);
        }

        var peakIndex = Enumerable.Range(0, streamOutput.Length)
            .MaxBy(index => MathF.Abs(streamOutput[index]));
        var measuredDelay = peakIndex - impulseIndex;

        Assert.Equal(RnnoiseProcessor.AlgorithmicDelaySamples, measuredDelay);
    }

    [Theory]
    [InlineData("CABLE Input (VB-Audio Virtual Cable)", true)]
    [InlineData("Voicemeeter Input", true)]
    [InlineData("Speakers (Realtek Audio)", false)]
    public void VirtualCableDetection_IsConservative(string name, bool expected)
    {
        Assert.Equal(expected, WasapiDeviceCatalog.IsLikelyVirtualCable(name));
    }
}

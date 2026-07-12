using Muted.Core.Dsp;

namespace Muted.Core.Tests;

public sealed class DspTests
{
    [Fact]
    public async Task RingBuffer_PreservesOrderBetweenProducerAndConsumerThreads()
    {
        const int sampleCount = 100_000;
        var buffer = new FloatRingBuffer(1_024);
        var received = new float[sampleCount];

        var producer = Task.Run(() =>
        {
            var spin = new SpinWait();
            for (var index = 0; index < sampleCount; index++)
            {
                while (buffer.Count == buffer.Capacity)
                {
                    spin.SpinOnce();
                }

                Assert.Equal(1, buffer.Write([(float)index]));
            }
        });

        var consumer = Task.Run(() =>
        {
            var oneSample = new float[1];
            var spin = new SpinWait();
            for (var index = 0; index < sampleCount; index++)
            {
                while (buffer.Read(oneSample) == 0)
                {
                    spin.SpinOnce();
                }

                received[index] = oneSample[0];
            }
        });

        await Task.WhenAll(producer, consumer);

        Assert.Equal(0, buffer.DroppedSamples);
        for (var index = 0; index < sampleCount; index++)
        {
            Assert.Equal((float)index, received[index]);
        }
    }

    [Fact]
    public void RingBuffer_PreservesSamplesAcrossWraparound()
    {
        var buffer = new FloatRingBuffer(8);
        Assert.Equal(6, buffer.Write([0, 1, 2, 3, 4, 5]));

        var first = new float[4];
        Assert.Equal(4, buffer.Read(first));
        Assert.Equal([0, 1, 2, 3], first);

        Assert.Equal(6, buffer.Write([6, 7, 8, 9, 10, 11]));
        var second = new float[8];
        Assert.Equal(8, buffer.Read(second));
        Assert.Equal([4, 5, 6, 7, 8, 9, 10, 11], second);
    }

    [Fact]
    public void RingBuffer_DropsNewestSamplesOnOverflow()
    {
        var buffer = new FloatRingBuffer(4);

        Assert.Equal(4, buffer.Write([1, 2, 3, 4, 5, 6]));
        Assert.Equal(2, buffer.DroppedSamples);

        var output = new float[4];
        Assert.Equal(4, buffer.Read(output));
        Assert.Equal([1, 2, 3, 4], output);
    }

    [Fact]
    public void RingBuffer_ConsumerCanDiscardStaleSamples()
    {
        var buffer = new FloatRingBuffer(8);
        buffer.Write([0, 1, 2, 3, 4, 5]);

        Assert.Equal(4, buffer.Discard(4));
        var output = new float[2];
        Assert.Equal(2, buffer.Read(output));
        Assert.Equal([4, 5], output);
    }

    [Fact]
    public void DelayLine_DelaysByConfiguredSampleCount()
    {
        var delay = new SampleDelayLine(3);
        var output = new float[5];

        delay.Process([1, 2, 3, 4, 5], output);

        Assert.Equal([0, 0, 0, 1, 2], output);
    }

    [Theory]
    [InlineData(-1, 479)]
    [InlineData(0, 480)]
    [InlineData(1, 481)]
    public void DriftCorrector_ProducesRequestedLength(int correction, int expectedLength)
    {
        var input = Enumerable.Range(0, 480).Select(index => index / 480f).ToArray();
        var output = new float[481];

        var length = DriftCorrector.Process(input, output, correction);

        Assert.Equal(expectedLength, length);
        Assert.Equal(input[0], output[0]);
        Assert.Equal(input[^1], output[length - 1], precision: 5);
        Assert.All(output[..length], sample => Assert.True(float.IsFinite(sample)));
    }

    [Fact]
    public void VoiceGate_ClosesOnNoiseAndOpensOnVoice()
    {
        var gate = new VoiceGate();
        var noise = Enumerable.Repeat(1f, 480).ToArray();
        gate.Process(noise, voiceProbability: 0.1f, threshold: 0.55f, holdMilliseconds: 0, enabled: true);
        Assert.Equal(0f, gate.Gain);
        Assert.True(noise[^1] < 0.01f);

        var voice = Enumerable.Repeat(1f, 480).ToArray();
        gate.Process(voice, voiceProbability: 0.9f, threshold: 0.55f, holdMilliseconds: 100, enabled: true);
        Assert.Equal(1f, gate.Gain);
        Assert.True(voice[^1] > 0.99f);

        var held = Enumerable.Repeat(1f, 480).ToArray();
        gate.Process(held, voiceProbability: 0.1f, threshold: 0.55f, holdMilliseconds: 100, enabled: true);
        Assert.Equal(1f, gate.Gain);
    }
}

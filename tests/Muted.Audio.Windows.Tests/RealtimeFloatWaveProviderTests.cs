using System.Runtime.InteropServices;
using Muted.Audio.Windows.Engine;

namespace Muted.Audio.Windows.Tests;

public sealed class RealtimeFloatWaveProviderTests
{
    [Fact]
    public void Provider_DrainsStartupBufferBeforeTrimmingStaleAudio()
    {
        const int frameSize = 480;
        var provider = new RealtimeFloatWaveProvider(
            sampleRate: 48_000,
            capacitySamples: 4_096,
            targetBufferedSamples: frameSize,
            highWaterSamples: frameSize * 3,
            startupBufferedSamples: frameSize * 4);
        var startup = Enumerable.Range(0, frameSize * 4).Select(value => (float)value).ToArray();
        Assert.Equal(startup.Length, provider.Write(startup));

        var bytes = new byte[frameSize * sizeof(float)];
        for (var frame = 0; frame < 3; frame++)
        {
            Assert.Equal(bytes.Length, provider.Read(bytes, 0, bytes.Length));
            var samples = MemoryMarshal.Cast<byte, float>(bytes);
            Assert.Equal(frame * frameSize, samples[0]);
            Assert.Equal(((frame + 1) * frameSize) - 1, samples[^1]);
        }

        Assert.Equal(0, provider.DroppedSamples);
        var backlog = Enumerable.Range(2_000, (frameSize * 3) + 1).Select(value => (float)value).ToArray();
        Assert.Equal(backlog.Length, provider.Write(backlog));

        Assert.Equal(bytes.Length, provider.Read(bytes, 0, bytes.Length));
        var freshSamples = MemoryMarshal.Cast<byte, float>(bytes);
        Assert.Equal(2_961f, freshSamples[0]);
        Assert.Equal(3_440f, freshSamples[^1]);
        Assert.Equal(1_441, provider.DroppedSamples);
    }
}

using Muted.Core.Dsp;

namespace Muted.Core.Tests;

public sealed class EchoCancellerTests
{
    private const int FrameSize = EchoCanceller.FrameSize;
    private const int SampleRate = 48_000;

    /// <summary>Noise with a syllabic envelope, which excites the filter like speech does.</summary>
    private static float[] Speechlike(int frames, int seed, float level = 0.3f)
    {
        var random = new Random(seed);
        var samples = new float[frames * FrameSize];
        var previous = 0f;
        for (var index = 0; index < samples.Length; index++)
        {
            var white = (float)((random.NextDouble() * 2) - 1);
            previous = (previous * 0.85f) + (white * 0.15f);
            var envelope = 0.35f + (0.65f * (float)Math.Abs(Math.Sin(2 * Math.PI * 3 * index / SampleRate)));
            samples[index] = previous * envelope;
        }

        // Scale to a realistic peak: audio above full scale would only be clipped.
        var peak = samples.Max(Math.Abs);
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = samples[index] / peak * level;
        }

        return samples;
    }

    /// <summary>A speaker-to-microphone path: a delay, then a short decaying tail.</summary>
    private static float[] EchoPath(int delayMilliseconds, float attenuation, int seed)
    {
        var random = new Random(seed);
        var delay = delayMilliseconds * SampleRate / 1000;
        var tail = 40 * SampleRate / 1000;
        var impulse = new float[delay + tail];
        for (var index = 0; index < tail; index++)
        {
            var decay = (float)Math.Exp(-index / (0.008 * SampleRate));
            impulse[delay + index] = (float)((random.NextDouble() * 2) - 1) * decay;
        }

        // Normalise the energy gain of the whole path, not its loudest single tap:
        // a speaker heard through a microphone always comes back quieter overall.
        var gain = (float)Math.Sqrt(impulse.Sum(tap => (double)tap * tap));
        for (var index = 0; index < impulse.Length; index++)
        {
            impulse[index] = impulse[index] / gain * attenuation;
        }

        return impulse;
    }

    private static float[] Convolve(float[] signal, float[] impulse)
    {
        var output = new float[signal.Length];
        for (var n = 0; n < signal.Length; n++)
        {
            var sum = 0f;
            var limit = Math.Min(impulse.Length, n + 1);
            for (var k = 0; k < limit; k++)
            {
                sum += impulse[k] * signal[n - k];
            }

            output[n] = sum;
        }

        return output;
    }

    private static float Energy(ReadOnlySpan<float> samples)
    {
        var sum = 0d;
        foreach (var sample in samples)
        {
            sum += sample * (double)sample;
        }

        return (float)sum;
    }

    /// <summary>Runs a whole signal through the canceller and returns its output.</summary>
    private static float[] Run(
        EchoCanceller canceller,
        float[] microphone,
        float[] reference,
        float strength = 0f)
    {
        var frames = microphone.Length / FrameSize;
        var output = new float[frames * FrameSize];
        var frame = new float[FrameSize];
        for (var index = 0; index < frames; index++)
        {
            var offset = index * FrameSize;
            microphone.AsSpan(offset, FrameSize).CopyTo(frame);
            canceller.Process(frame, reference.AsSpan(offset, FrameSize), enabled: true, strength);
            frame.CopyTo(output.AsSpan(offset));
        }

        return output;
    }

    private static float ReductionDb(ReadOnlySpan<float> before, ReadOnlySpan<float> after) =>
        10f * MathF.Log10((Energy(before) + 1e-12f) / (Energy(after) + 1e-12f));

    [Fact]
    public void Disabled_PassesTheMicrophoneThroughUntouched()
    {
        var canceller = new EchoCanceller();
        var microphone = Speechlike(1, seed: 1);
        var expected = (float[])microphone.Clone();

        canceller.Process(microphone, Speechlike(1, seed: 2), enabled: false, strength: 1f);

        Assert.Equal(expected, microphone);
        Assert.Equal(0f, canceller.ErleDb);
    }

    [Fact]
    public void SilentReference_LeavesTheMicrophoneAlone()
    {
        var canceller = new EchoCanceller();
        var frames = 50;
        var microphone = Speechlike(frames, seed: 3);
        var expected = (float[])microphone.Clone();

        var output = Run(canceller, microphone, new float[frames * FrameSize]);

        Assert.False(canceller.FarEndActive);
        for (var index = 0; index < output.Length; index++)
        {
            Assert.Equal(expected[index], output[index], tolerance: 1e-6);
        }
    }

    [Fact]
    public void Converges_OnASpeakerPath()
    {
        const int frames = 400; // four seconds
        var canceller = new EchoCanceller();
        var reference = Speechlike(frames, seed: 4, level: 0.5f);
        var microphone = Convolve(reference, EchoPath(delayMilliseconds: 30, attenuation: 0.35f, seed: 5));

        var output = Run(canceller, microphone, reference);

        // Judge the last second, once the filter has had time to learn the path.
        var tail = (frames - 100) * FrameSize;
        var reduction = ReductionDb(microphone.AsSpan(tail), output.AsSpan(tail));
        Assert.True(reduction > 20f, $"Only removed {reduction:0.0} dB of echo.");
        Assert.True(canceller.ErleDb > 15f, $"Reported ERLE was {canceller.ErleDb:0.0} dB.");
        Assert.All(output, sample => Assert.True(float.IsFinite(sample)));
    }

    [Fact]
    public void Converges_WhenTheEchoArrivesLate()
    {
        // 120 ms still fits inside the 200 ms tail.
        const int frames = 500;
        var canceller = new EchoCanceller();
        var reference = Speechlike(frames, seed: 6, level: 0.5f);
        var microphone = Convolve(reference, EchoPath(delayMilliseconds: 120, attenuation: 0.3f, seed: 7));

        var output = Run(canceller, microphone, reference);

        var tail = (frames - 100) * FrameSize;
        var reduction = ReductionDb(microphone.AsSpan(tail), output.AsSpan(tail));
        Assert.True(reduction > 15f, $"Only removed {reduction:0.0} dB of echo.");
    }

    [Fact]
    public void Survives_DoubleTalk()
    {
        const int frames = 600;
        var canceller = new EchoCanceller();
        var reference = Speechlike(frames, seed: 8, level: 0.5f);
        var echoPath = EchoPath(delayMilliseconds: 25, attenuation: 0.3f, seed: 9);
        var echo = Convolve(reference, echoPath);

        // Let it converge on echo alone first.
        var converge = 300 * FrameSize;
        Run(canceller, echo[..converge], reference[..converge]);
        var beforeErle = canceller.ErleDb;
        Assert.True(beforeErle > 15f, $"Did not converge before double talk: {beforeErle:0.0} dB.");

        // Now talk over it for a second.
        var nearEnd = Speechlike(100, seed: 10, level: 0.4f);
        var doubleTalkMicrophone = new float[nearEnd.Length];
        for (var index = 0; index < nearEnd.Length; index++)
        {
            doubleTalkMicrophone[index] = echo[converge + index] + nearEnd[index];
        }

        var doubleTalkOutput = Run(
            canceller,
            doubleTalkMicrophone,
            reference.AsSpan(converge, nearEnd.Length).ToArray());

        // Your own voice has to survive: it may not be ducked into the ground.
        var kept = ReductionDb(nearEnd, doubleTalkOutput);
        Assert.True(kept < 6f, $"Near-end speech lost {kept:0.0} dB during double talk.");

        // And the filter may not have thrown away what it learned.
        var after = 400 * FrameSize;
        var recovery = Run(
            canceller,
            echo.AsSpan(after, 100 * FrameSize).ToArray(),
            reference.AsSpan(after, 100 * FrameSize).ToArray());
        var reduction = ReductionDb(echo.AsSpan(after + (50 * FrameSize), 50 * FrameSize),
            recovery.AsSpan(50 * FrameSize));
        Assert.True(reduction > 15f, $"Only {reduction:0.0} dB left after double talk.");
    }

    [Fact]
    public void Strength_DucksTheResidualFurther()
    {
        const int frames = 400;
        var reference = Speechlike(frames, seed: 11, level: 0.5f);
        var microphone = Convolve(reference, EchoPath(delayMilliseconds: 30, attenuation: 0.35f, seed: 12));

        var soft = Run(new EchoCanceller(), microphone, reference, strength: 0f);
        var hard = Run(new EchoCanceller(), microphone, reference, strength: 1f);

        var tail = (frames - 100) * FrameSize;
        Assert.True(Energy(hard.AsSpan(tail)) < Energy(soft.AsSpan(tail)));
    }

    [Fact]
    public void EchoBeyondTheTail_IsLeftAloneWithoutBlowingUp()
    {
        // 400 ms is twice the tail, so most of it cannot be modelled.
        const int frames = 300;
        var canceller = new EchoCanceller();
        var reference = Speechlike(frames, seed: 13, level: 0.5f);
        var microphone = Convolve(reference, EchoPath(delayMilliseconds: 400, attenuation: 0.3f, seed: 14));

        var output = Run(canceller, microphone, reference);

        Assert.All(output, sample => Assert.True(float.IsFinite(sample) && Math.Abs(sample) <= 1f));
        var tail = (frames - 50) * FrameSize;
        Assert.True(Energy(output.AsSpan(tail)) < Energy(microphone.AsSpan(tail)) * 4f);
    }

    [Fact]
    public void Reset_ForgetsTheRoom()
    {
        const int frames = 300;
        var canceller = new EchoCanceller();
        var reference = Speechlike(frames, seed: 15, level: 0.5f);
        var microphone = Convolve(reference, EchoPath(delayMilliseconds: 30, attenuation: 0.35f, seed: 16));

        Run(canceller, microphone, reference);
        Assert.True(canceller.ErleDb > 10f);

        canceller.Reset();

        Assert.Equal(0f, canceller.ErleDb);
        Assert.False(canceller.FarEndActive);
        Assert.False(canceller.DoubleTalk);
    }
}

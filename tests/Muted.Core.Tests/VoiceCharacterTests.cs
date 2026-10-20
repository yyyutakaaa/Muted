using Muted.Core.Dsp;

namespace Muted.Core.Tests;

public sealed class VoiceCharacterTests
{
    private const int SampleRate = 48_000;
    private const int FrameSize = 480;

    // The oversampled saturation stage delays the enabled chain by this many samples.
    private const int Latency = 15;

    [Fact]
    public void Constructor_RejectsBadSampleRate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VoiceCharacter(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VoiceCharacter(-48_000));
    }

    [Fact]
    public void Disabled_IsExactPassthrough()
    {
        var character = new VoiceCharacter(SampleRate);
        var input = Noise(FrameSize * 20, 0.3f, seed: 1);
        var output = (float[])input.Clone();

        RunFrames(character, output, 1, 1, 1, 1, 1, enabled: false);

        Assert.Equal(input, output);
        Assert.Equal(0f, character.CompressorReductionDb);
        Assert.Equal(0f, character.DeEsserReductionDb);
    }

    [Fact]
    public void Disabling_FadesOutThenBecomesExactPassthrough()
    {
        var character = new VoiceCharacter(SampleRate);
        var warmup = Sine(FrameSize * 50, 200f, 0.3f);
        RunFrames(character, warmup, 1, 1, 1, 1, 1, enabled: true);

        // Transition frame: continuous, no jump at the edges.
        var phaseStart = FrameSize * 50;
        var transition = Sine(FrameSize, 200f, 0.3f, phaseStart);
        var lastWet = warmup[^1];
        character.Process(transition, 1, 1, 1, 1, 1, enabled: false);
        Assert.True(MathF.Abs(transition[0] - lastWet) < 0.1f, $"jump {transition[0] - lastWet}");
        var dryLast = Sine(FrameSize, 200f, 0.3f, phaseStart)[^1];
        Assert.Equal(dryLast, transition[^1], 4);

        var after = Sine(FrameSize * 5, 200f, 0.3f, phaseStart + FrameSize);
        var expected = (float[])after.Clone();
        RunFrames(character, after, 1, 1, 1, 1, 1, enabled: false);
        Assert.Equal(expected, after);
    }

    [Fact]
    public void AllAmountsZero_IsNearPassthroughWithFixedLatency()
    {
        var character = new VoiceCharacter(SampleRate);
        var input = Noise(FrameSize * 40, 0.25f, seed: 2);
        var output = (float[])input.Clone();

        RunFrames(character, output, 0, 0, 0, 0, 0, enabled: true);

        // Skip the fade-in frame; compare against the input delayed by the chain latency.
        var maxError = 0f;
        for (var i = FrameSize * 2; i < input.Length; i++)
        {
            maxError = MathF.Max(maxError, MathF.Abs(output[i] - input[i - Latency]));
        }

        // The halfband filters cost a little top-octave ripple; everything else is unity.
        Assert.True(maxError < 0.03f, $"max error {maxError}");

        var lowBand = Sine(FrameSize * 40, 1000f, 0.3f);
        var lowOut = (float[])lowBand.Clone();
        RunFrames(new VoiceCharacter(SampleRate), lowOut, 0, 0, 0, 0, 0, enabled: true);
        var sineError = 0f;
        for (var i = FrameSize * 2; i < lowBand.Length; i++)
        {
            sineError = MathF.Max(sineError, MathF.Abs(lowOut[i] - lowBand[i - Latency]));
        }

        Assert.True(sineError < 1e-3f, $"1 kHz error {sineError}");
    }

    [Fact]
    public void Warmth_LiftsLowsAndLeavesMidsAlone()
    {
        var gain120 = GainDb(120f, 0.1f, warmth: 1f);
        var gain1k = GainDb(1000f, 0.1f, warmth: 1f);

        Assert.InRange(gain120, 3f, 6.5f);
        Assert.InRange(gain1k, -0.5f, 0.5f);
    }

    [Fact]
    public void Proximity_AddsLowMidBump()
    {
        Assert.InRange(GainDb(220f, 0.1f, proximity: 1f), 3f, 4.5f);
        Assert.InRange(GainDb(3500f, 0.1f, proximity: 1f), -2.5f, -1f);
    }

    [Fact]
    public void Smoothness_LowersSibilantToneAndDeEssesNoiseBursts()
    {
        var plain = GainDb(7500f, 0.2f);
        var smooth = GainDb(7500f, 0.2f, smoothness: 1f);
        Assert.True(smooth < plain - 3f, $"plain {plain:F2} dB, smooth {smooth:F2} dB");

        var character = new VoiceCharacter(SampleRate);
        var burst = BandPassedNoise(FrameSize * 10, 6500f, 0.3f, seed: 3);
        var maxReduction = 0f;
        for (var offset = 0; offset < burst.Length; offset += FrameSize)
        {
            character.Process(burst.AsSpan(offset, FrameSize), 0, 0, 1, 0, 0, enabled: true);
            maxReduction = MathF.Max(maxReduction, character.DeEsserReductionDb);
        }

        Assert.True(maxReduction > 3f, $"de-ess {maxReduction:F2} dB");
        Assert.True(maxReduction <= 10.01f);

        // A plain low voiced tone must not trigger the de-esser.
        var voiced = new VoiceCharacter(SampleRate);
        var vowel = Sine(FrameSize * 10, 300f, 0.3f);
        RunFrames(voiced, vowel, 0, 0, 1, 0, 0, enabled: true);
        Assert.Equal(0f, voiced.DeEsserReductionDb);
    }

    [Fact]
    public void Compression_NarrowsLevelDifference()
    {
        var loudIn = DbToAmp(-10f);
        var quietIn = DbToAmp(-35f);

        var loud = new VoiceCharacter(SampleRate);
        var loudSignal = Sine(FrameSize * 100, 500f, loudIn);
        RunFrames(loud, loudSignal, 0, 0, 0, 1, 0, enabled: true);
        Assert.True(loud.CompressorReductionDb > 3f, $"reduction {loud.CompressorReductionDb:F2} dB");

        var quiet = new VoiceCharacter(SampleRate);
        var quietSignal = Sine(FrameSize * 100, 500f, quietIn);
        RunFrames(quiet, quietSignal, 0, 0, 0, 1, 0, enabled: true);

        var loudOut = RmsDb(loudSignal.AsSpan(loudSignal.Length - FrameSize * 10));
        var quietOut = RmsDb(quietSignal.AsSpan(quietSignal.Length - FrameSize * 10));
        var outDifference = loudOut - quietOut;

        Assert.True(outDifference < 25f - 8f, $"difference {outDifference:F2} dB");
        Assert.True(outDifference > 3f, $"difference {outDifference:F2} dB");
        // Quiet speech should come up a little thanks to make-up gain.
        Assert.True(quietOut > RmsDb(Sine(FrameSize, 500f, quietIn)) + 1f);
    }

    [Fact]
    public void Saturation_AddsHarmonicsButStaysBounded()
    {
        var clean = Sine(FrameSize * 40, 200f, 0.7f);
        var cleanOut = (float[])clean.Clone();
        RunFrames(new VoiceCharacter(SampleRate), cleanOut, 0, 0, 0, 0, 0, enabled: true);

        var hot = (float[])clean.Clone();
        RunFrames(new VoiceCharacter(SampleRate), hot, 0, 0, 0, 0, 1, enabled: true);

        var tail = FrameSize * 20;
        var cleanHarmonics = Goertzel(cleanOut.AsSpan(tail), 400f) + Goertzel(cleanOut.AsSpan(tail), 600f);
        var hotHarmonics = Goertzel(hot.AsSpan(tail), 400f) + Goertzel(hot.AsSpan(tail), 600f);
        Assert.True(hotHarmonics > cleanHarmonics * 20f + 1e-4f, $"clean {cleanHarmonics}, hot {hotHarmonics}");

        // Loudness stays roughly the same.
        var levelChange = RmsDb(hot.AsSpan(tail)) - RmsDb(cleanOut.AsSpan(tail));
        Assert.InRange(levelChange, -2f, 1f);

        // Full scale, everything maxed: finite and inside [-1, 1].
        var extreme = Sine(FrameSize * 20, 200f, 1f);
        RunFrames(new VoiceCharacter(SampleRate), extreme, 1, 1, 1, 1, 1, enabled: true);
        Assert.All(extreme, value =>
        {
            Assert.True(float.IsFinite(value));
            Assert.InRange(value, -1f, 1f);
        });
    }

    [Fact]
    public void Silence_StaysSilent()
    {
        var character = new VoiceCharacter(SampleRate);
        var silence = new float[FrameSize * 100];
        RunFrames(character, silence, 1, 1, 1, 1, 1, enabled: true);

        Assert.All(silence, value => Assert.Equal(0f, value));
        Assert.Equal(0f, character.CompressorReductionDb);
    }

    [Fact]
    public void NonFiniteInput_NeverProducesNaN()
    {
        var character = new VoiceCharacter(SampleRate);
        var frame = Sine(FrameSize, 200f, 0.5f);
        frame[10] = float.NaN;
        frame[20] = float.PositiveInfinity;
        character.Process(frame, 1, 1, 1, 1, 1, enabled: true);
        var next = Sine(FrameSize * 10, 200f, 0.5f);
        RunFrames(character, next, 1, 1, 1, 1, 1, enabled: true);

        Assert.All(frame, value => Assert.True(float.IsFinite(value)));
        Assert.All(next, value => Assert.True(float.IsFinite(value)));
        Assert.True(RmsDb(next.AsSpan(next.Length - FrameSize)) > -20f);
    }

    [Fact]
    public void ParameterJump_DoesNotClick()
    {
        var character = new VoiceCharacter(SampleRate);
        var signal = Sine(FrameSize * 40, 150f, 0.2f);
        for (var frame = 0; frame < 40; frame++)
        {
            var amount = frame < 20 ? 0f : 1f;
            character.Process(signal.AsSpan(frame * FrameSize, FrameSize), amount, amount, amount, amount, amount, enabled: true);
        }

        // A 150 Hz sine at 0.2 changes by at most ~0.004 per sample; allow growth from the EQ.
        var maxStep = 0f;
        for (var i = FrameSize * 2; i < signal.Length; i++)
        {
            maxStep = MathF.Max(maxStep, MathF.Abs(signal[i] - signal[i - 1]));
        }

        Assert.True(maxStep < 0.02f, $"max step {maxStep}");
    }

    [Fact]
    public void Process_HandlesOddAndLargeFrameLengths()
    {
        var character = new VoiceCharacter(SampleRate);
        foreach (var length in new[] { 1, 7, 480, 4096, 5000 })
        {
            var frame = Noise(length, 0.3f, seed: length);
            character.Process(frame, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, enabled: true);
            Assert.All(frame, value => Assert.True(float.IsFinite(value) && MathF.Abs(value) <= 1f));
        }
    }

    [Fact]
    public void Process_DoesNotAllocate()
    {
        var character = new VoiceCharacter(SampleRate);
        var frame = Noise(FrameSize, 0.3f, seed: 4);
        for (var i = 0; i < 20; i++)
        {
            character.Process(frame, 0.7f, 0.6f, 0.5f, 0.4f, 0.3f, enabled: true);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 500; i++)
        {
            var amount = (i % 10) / 10f;
            character.Process(frame, amount, 1 - amount, amount, 1 - amount, amount, enabled: i % 50 != 49);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    private static float GainDb(
        float frequency,
        float amplitude,
        float warmth = 0f,
        float proximity = 0f,
        float smoothness = 0f)
    {
        var character = new VoiceCharacter(SampleRate);
        var signal = Sine(FrameSize * 40, frequency, amplitude);
        RunFrames(character, signal, warmth, proximity, smoothness, 0f, 0f, enabled: true);
        var tail = signal.AsSpan(signal.Length - (FrameSize * 10));
        return RmsDb(tail) - RmsDb(Sine(FrameSize * 10, frequency, amplitude));
    }

    private static void RunFrames(
        VoiceCharacter character,
        float[] samples,
        float warmth,
        float proximity,
        float smoothness,
        float compression,
        float saturation,
        bool enabled)
    {
        for (var offset = 0; offset < samples.Length; offset += FrameSize)
        {
            var length = Math.Min(FrameSize, samples.Length - offset);
            character.Process(samples.AsSpan(offset, length), warmth, proximity, smoothness, compression, saturation, enabled);
        }
    }

    private static float[] Sine(int length, float frequency, float amplitude, int phaseOffset = 0)
    {
        var samples = new float[length];
        for (var i = 0; i < length; i++)
        {
            samples[i] = amplitude * MathF.Sin(2f * MathF.PI * frequency * (i + phaseOffset) / SampleRate);
        }

        return samples;
    }

    private static float[] Noise(int length, float amplitude, int seed)
    {
        var random = new Random(seed);
        var samples = new float[length];
        for (var i = 0; i < length; i++)
        {
            samples[i] = amplitude * ((float)random.NextDouble() * 2f - 1f);
        }

        return samples;
    }

    private static float[] BandPassedNoise(int length, float center, float amplitude, int seed)
    {
        // Two RBJ band-pass passes (Q 2) shape white noise into an "sss"-like hiss.
        var samples = Noise(length, 1f, seed);
        var omega = 2.0 * Math.PI * center / SampleRate;
        var alpha = Math.Sin(omega) / (2.0 * 2.0);
        var a0 = 1 + alpha;
        double b0 = alpha / a0, b2 = -alpha / a0, a1 = -2 * Math.Cos(omega) / a0, a2 = (1 - alpha) / a0;
        for (var pass = 0; pass < 2; pass++)
        {
            double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
            for (var i = 0; i < length; i++)
            {
                double x0 = samples[i];
                var y0 = (b0 * x0) + (b2 * x2) - (a1 * y1) - (a2 * y2);
                x2 = x1;
                x1 = x0;
                y2 = y1;
                y1 = y0;
                samples[i] = (float)y0;
            }
        }

        var peak = samples.Max(MathF.Abs);
        for (var i = 0; i < length; i++)
        {
            samples[i] *= amplitude / peak;
        }

        return samples;
    }

    private static float RmsDb(ReadOnlySpan<float> samples)
    {
        double sum = 0;
        foreach (var sample in samples)
        {
            sum += sample * sample;
        }

        return (float)(10.0 * Math.Log10((sum / samples.Length) + 1e-20));
    }

    private static float Goertzel(ReadOnlySpan<float> samples, float frequency)
    {
        var coefficient = 2.0 * Math.Cos(2.0 * Math.PI * frequency / SampleRate);
        double s1 = 0, s2 = 0;
        foreach (var sample in samples)
        {
            var s0 = sample + (coefficient * s1) - s2;
            s2 = s1;
            s1 = s0;
        }

        var power = (s1 * s1) + (s2 * s2) - (coefficient * s1 * s2);
        return (float)(Math.Sqrt(Math.Max(0, power)) * 2.0 / samples.Length);
    }

    private static float DbToAmp(float db) => MathF.Pow(10f, db / 20f);
}

using Muted.Core.Dsp;

namespace Muted.Core.Tests;

public sealed class ShoutGuardTests
{
    private const int SampleRate = 48_000;
    private const int LookAhead = 960;
    private const int Frame = 480;
    private const float ThresholdDb = -6f;
    private static readonly float ThresholdLinear = MathF.Pow(10f, ThresholdDb / 20f);
    private const int HoldSamples = 400 * SampleRate / 1000;
    private const int ReleaseSamples = 150 * SampleRate / 1000;

    [Fact]
    public void QuietSignal_IsExactPassthrough()
    {
        var guard = new ShoutGuard(SampleRate, LookAhead);
        var signal = Sine(SampleRate, amplitude: 0.3f);

        var output = RunEngine(guard, signal, signal, ShoutGuardMode.Silence);

        for (var n = LookAhead; n < signal.Length; n++)
        {
            Assert.Equal(signal[n - LookAhead], output[n]);
        }

        Assert.False(guard.IsEngaged);
        Assert.Equal(0, guard.TriggerCount);
    }

    [Fact]
    public void LoudBurst_IsSilencedAtAlignedPosition_AndNothingLeaks()
    {
        var guard = new ShoutGuard(SampleRate, LookAhead);
        var signal = Sine(SampleRate, amplitude: 0.05f);
        AddBurst(signal, start: 24_000, length: 4_800, amplitude: 0.9f);

        var output = RunEngine(guard, signal, signal, ShoutGuardMode.Silence);

        for (var n = LookAhead; n < signal.Length; n++)
        {
            Assert.True(MathF.Abs(output[n]) <= ThresholdLinear, $"Leak at {n}: {output[n]}");
            if (MathF.Abs(signal[n - LookAhead]) > ThresholdLinear)
            {
                Assert.Equal(0f, output[n]);
            }
        }

        Assert.Equal(1, guard.TriggerCount);
    }

    [Fact]
    public void BurstOnset_IsFullyClosedBeforeFirstLoudSample()
    {
        var guard = new ShoutGuard(SampleRate, LookAhead);
        var early = Constant(SampleRate, 0.1f);
        const int onset = 12_345;
        for (var n = onset; n < onset + 2_000; n++)
        {
            early[n] = 0.95f;
        }

        var applied = Constant(SampleRate, 0.1f);
        var output = RunEngine(guard, early, applied, ShoutGuardMode.Silence);

        // Applied sample at n corresponds to early position n - LookAhead.
        Assert.Equal(0f, output[onset + LookAhead]);
        Assert.Equal(0.1f, output[onset + LookAhead - 200]);
    }

    [Fact]
    public void DuckMode_AttenuatesByTwentyDecibels()
    {
        var guard = new ShoutGuard(SampleRate, LookAhead);
        var signal = Sine(SampleRate, amplitude: 0.05f);
        AddBurst(signal, start: 24_000, length: 4_800, amplitude: 0.9f);

        var output = RunEngine(guard, signal, signal, ShoutGuardMode.Duck);

        var checkedAny = false;
        for (var n = LookAhead; n < signal.Length; n++)
        {
            var input = signal[n - LookAhead];
            if (MathF.Abs(input) > ThresholdLinear)
            {
                Assert.Equal(0.1f, output[n] / input, 3);
                checkedAny = true;
            }
        }

        Assert.True(checkedAny);
    }

    [Fact]
    public void Hold_KeepsClosed_ThenReleasesToUnity()
    {
        var guard = new ShoutGuard(SampleRate, LookAhead);
        var early = Constant(SampleRate * 2, 0.1f);
        const int burstStart = 10_000;
        const int burstEnd = 11_000; // exclusive
        for (var n = burstStart; n < burstEnd; n++)
        {
            early[n] = 0.9f;
        }

        var applied = Constant(early.Length, 0.1f);
        var output = RunEngine(guard, early, applied, ShoutGuardMode.Silence);

        var lastLoud = burstEnd - 1 + LookAhead;
        for (var n = lastLoud; n <= lastLoud + HoldSamples; n++)
        {
            Assert.Equal(0f, output[n]);
        }

        // Releasing: strictly between closed and open, and rising.
        var midRelease = lastLoud + HoldSamples + ReleaseSamples / 2;
        Assert.InRange(output[midRelease], 0.01f, 0.09f);

        for (var n = lastLoud + HoldSamples + ReleaseSamples + 1; n < output.Length; n++)
        {
            Assert.Equal(0.1f, output[n]);
        }

        Assert.False(guard.IsEngaged);
    }

    [Fact]
    public void GainRamps_AreSmooth()
    {
        var guard = new ShoutGuard(SampleRate, LookAhead);
        var early = Constant(SampleRate * 2, 0.1f);
        for (var n = 20_000; n < 25_000; n++)
        {
            early[n] = 0.9f;
        }

        var applied = Constant(early.Length, 1f);
        var output = RunEngine(guard, early, applied, ShoutGuardMode.Silence);

        var attackSamples = 1.5f * SampleRate / 1000f;
        var maxStep = MathF.PI / (2f * attackSamples) * 1.01f;
        for (var n = LookAhead + 1; n < output.Length; n++)
        {
            Assert.True(MathF.Abs(output[n] - output[n - 1]) <= maxStep, $"Step at {n}: {output[n - 1]} -> {output[n]}");
        }
    }

    [Fact]
    public void TriggerCount_CountsSeparateShoutsButNotContinuousOnes()
    {
        var separated = new ShoutGuard(SampleRate, LookAhead);
        var signal = Sine(SampleRate * 3, amplitude: 0.05f);
        AddBurst(signal, start: 10_000, length: 4_800, amplitude: 0.9f);
        AddBurst(signal, start: 10_000 + 4_800 + HoldSamples + ReleaseSamples + 5_000, length: 4_800, amplitude: 0.9f);
        RunEngine(separated, signal, signal, ShoutGuardMode.Silence);
        Assert.Equal(2, separated.TriggerCount);

        var continuous = new ShoutGuard(SampleRate, LookAhead);
        var longShout = Sine(SampleRate * 3, amplitude: 0.05f);
        AddBurst(longShout, start: 10_000, length: 96_000, amplitude: 0.9f);
        RunEngine(continuous, longShout, longShout, ShoutGuardMode.Silence);
        Assert.Equal(1, continuous.TriggerCount);

        // A second shout inside the hold window re-closes without a new trigger.
        var withinHold = new ShoutGuard(SampleRate, LookAhead);
        var close = Sine(SampleRate * 3, amplitude: 0.05f);
        AddBurst(close, start: 10_000, length: 4_800, amplitude: 0.9f);
        AddBurst(close, start: 10_000 + 4_800 + HoldSamples / 2, length: 4_800, amplitude: 0.9f);
        RunEngine(withinHold, close, close, ShoutGuardMode.Silence);
        Assert.Equal(1, withinHold.TriggerCount);
    }

    [Fact]
    public void IsEngaged_TogglesWithShout()
    {
        var guard = new ShoutGuard(SampleRate, LookAhead);
        var quiet = new float[Frame];
        var loud = new float[Frame];
        Array.Fill(loud, 0.9f);
        var buffer = new float[Frame];

        guard.Analyze(quiet, ThresholdDb, true);
        guard.Apply(buffer, ShoutGuardMode.Silence, true);
        Assert.False(guard.IsEngaged);

        guard.Analyze(loud, ThresholdDb, true);
        guard.Apply(buffer, ShoutGuardMode.Silence, true);
        // The loud frame is still 960 samples away, but its attack ramp is not.
        guard.Analyze(quiet, ThresholdDb, true);
        guard.Apply(buffer, ShoutGuardMode.Silence, true);
        guard.Analyze(quiet, ThresholdDb, true);
        guard.Apply(buffer, ShoutGuardMode.Silence, true);
        Assert.True(guard.IsEngaged);

        var framesUntilOpen = (HoldSamples + ReleaseSamples) / Frame + 4;
        for (var i = 0; i < framesUntilOpen; i++)
        {
            guard.Analyze(quiet, ThresholdDb, true);
            guard.Apply(buffer, ShoutGuardMode.Silence, true);
        }

        Assert.False(guard.IsEngaged);
        Assert.Equal(1, guard.TriggerCount);
    }

    [Fact]
    public void DisabledApply_EasesBackToExactPassthrough()
    {
        var guard = new ShoutGuard(SampleRate, LookAhead);
        var loud = new float[Frame];
        Array.Fill(loud, 0.9f);
        var buffer = new float[Frame];

        for (var i = 0; i < 6; i++)
        {
            guard.Analyze(loud, ThresholdDb, true);
            Array.Fill(buffer, 0.5f);
            guard.Apply(buffer, ShoutGuardMode.Silence, true);
        }

        Assert.True(guard.IsEngaged);
        Assert.Equal(0f, buffer[^1]);

        // 5 ms is 240 samples, so the first disabled frame ends fully open.
        guard.Analyze(loud, ThresholdDb, true);
        Array.Fill(buffer, 0.5f);
        guard.Apply(buffer, ShoutGuardMode.Silence, enabled: false);
        Assert.True(buffer[0] < 0.01f);
        Assert.Equal(0.5f, buffer[^1]);
        for (var n = 1; n < Frame; n++)
        {
            Assert.True(buffer[n] >= buffer[n - 1]);
        }

        for (var i = 0; i < 3; i++)
        {
            guard.Analyze(loud, ThresholdDb, true);
            Array.Fill(buffer, 0.5f);
            guard.Apply(buffer, ShoutGuardMode.Silence, enabled: false);
            Assert.All(buffer, sample => Assert.Equal(0.5f, sample));
        }

        Assert.False(guard.IsEngaged);
    }

    [Fact]
    public void DisabledAnalyze_DoesNotTrigger()
    {
        var guard = new ShoutGuard(SampleRate, LookAhead);
        var signal = Sine(SampleRate, amplitude: 0.05f);
        AddBurst(signal, start: 12_000, length: 4_800, amplitude: 0.9f);

        var output = RunEngine(guard, signal, signal, ShoutGuardMode.Silence, analyzeEnabled: false);

        for (var n = LookAhead; n < signal.Length; n++)
        {
            Assert.Equal(signal[n - LookAhead], output[n]);
        }

        Assert.Equal(0, guard.TriggerCount);
    }

    [Fact]
    public void ZeroLookAhead_StillSilencesAfterOnset()
    {
        var guard = new ShoutGuard(SampleRate, lookAheadSamples: 0);
        var signal = Constant(SampleRate, 0.1f);
        // Onset on a frame boundary: no lookahead at all, so the onset is caught late.
        const int onset = 20 * Frame;
        for (var n = onset; n < onset + 10_000; n++)
        {
            signal[n] = 0.9f;
        }

        var output = RunEngine(guard, signal, signal, ShoutGuardMode.Silence, lookAhead: 0);

        Assert.Equal(0.1f, output[onset - 1]);
        Assert.True(output[onset] < 0.9f);
        for (var n = onset + 200; n < onset + 10_000; n++)
        {
            Assert.Equal(0f, output[n]);
        }
    }

    [Fact]
    public void AnalyzeAndApply_DoNotAllocate()
    {
        var guard = new ShoutGuard(SampleRate, LookAhead);
        var frame = new float[Frame];
        var buffer = new float[Frame];

        void Run(int frames)
        {
            for (var i = 0; i < frames; i++)
            {
                var loud = i % 200 < 20;
                for (var n = 0; n < Frame; n++)
                {
                    frame[n] = loud ? 0.9f : 0.05f;
                    buffer[n] = frame[n];
                }

                guard.Analyze(frame, ThresholdDb, true);
                guard.Apply(buffer, (i / 100) % 2 == 0 ? ShoutGuardMode.Silence : ShoutGuardMode.Duck, i % 700 < 650);
            }
        }

        Run(500);
        var before = GC.GetAllocatedBytesForCurrentThread();
        Run(2_000);
        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    [Fact]
    public void SupportsLargeFramesAndLookAhead()
    {
        var guard = new ShoutGuard(SampleRate, ShoutGuard.MaximumLookAheadSamples);
        var signal = Constant(SampleRate * 2, 0.1f);
        for (var n = 30_000; n < 31_000; n++)
        {
            signal[n] = 0.9f;
        }

        var output = RunEngine(guard, signal, signal, ShoutGuardMode.Silence,
            lookAhead: ShoutGuard.MaximumLookAheadSamples, frame: ShoutGuard.MaximumFrameLength);

        for (var n = 30_000; n < 31_000; n++)
        {
            Assert.Equal(0f, output[n + ShoutGuard.MaximumLookAheadSamples]);
        }

        Assert.Throws<ArgumentException>(() => guard.Analyze(new float[ShoutGuard.MaximumFrameLength + 1], ThresholdDb, true));
    }

    [Theory]
    [InlineData(0, 960, 400, 1.5f, 150f)]
    [InlineData(48_000, -1, 400, 1.5f, 150f)]
    [InlineData(48_000, 4_097, 400, 1.5f, 150f)]
    [InlineData(48_000, 960, -1, 1.5f, 150f)]
    [InlineData(48_000, 960, 400, 0f, 150f)]
    [InlineData(48_000, 960, 400, float.NaN, 150f)]
    [InlineData(48_000, 960, 400, 1.5f, 0f)]
    [InlineData(48_000, 960, 400, 1.5f, float.PositiveInfinity)]
    public void Constructor_RejectsBadArguments(int sampleRate, int lookAhead, int hold, float attack, float release)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShoutGuard(sampleRate, lookAhead, hold, attack, release));
    }

    /// <summary>
    /// Mimics the engine: each frame, Analyze sees the early signal and Apply sees the
    /// applied signal delayed by lookAhead samples. Returns the applied output stream.
    /// </summary>
    private static float[] RunEngine(
        ShoutGuard guard,
        float[] early,
        float[] applied,
        ShoutGuardMode mode,
        bool analyzeEnabled = true,
        int lookAhead = LookAhead,
        int frame = Frame)
    {
        var output = new float[early.Length];
        var buffer = new float[frame];
        for (var start = 0; start + frame <= early.Length; start += frame)
        {
            guard.Analyze(early.AsSpan(start, frame), ThresholdDb, analyzeEnabled);
            for (var n = 0; n < frame; n++)
            {
                var source = start + n - lookAhead;
                buffer[n] = source >= 0 ? applied[source] : 0f;
            }

            guard.Apply(buffer, mode, true);
            buffer.CopyTo(output, start);
        }

        return output;
    }

    private static float[] Sine(int length, float amplitude)
    {
        var signal = new float[length];
        for (var n = 0; n < length; n++)
        {
            signal[n] = amplitude * MathF.Sin(2f * MathF.PI * 220f * n / SampleRate);
        }

        return signal;
    }

    private static float[] Constant(int length, float value)
    {
        var signal = new float[length];
        Array.Fill(signal, value);
        return signal;
    }

    private static void AddBurst(float[] signal, int start, int length, float amplitude)
    {
        for (var n = start; n < start + length; n++)
        {
            signal[n] = amplitude * MathF.Sin(2f * MathF.PI * 220f * n / SampleRate);
        }
    }
}

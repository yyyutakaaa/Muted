using Muted.Core.Dsp;

namespace Muted.Core.Tests;

public sealed class ProcessingTests
{
    private const int SampleRate = 48_000;

    private static float[] Tone(double frequency, int length, float amplitude = 0.5f)
    {
        var samples = new float[length];
        for (var index = 0; index < length; index++)
        {
            samples[index] = (float)(amplitude * Math.Sin(2 * Math.PI * frequency * index / SampleRate));
        }

        return samples;
    }

    [Fact]
    public void HighPass_KeepsSpeechAndRemovesRumble()
    {
        var filter = new HighPassFilter(SampleRate, 80f);
        var rumble = Tone(30, 4_800);
        var speech = Tone(500, 4_800);
        var rumbleInput = AudioMath.Rms(rumble);
        var speechInput = AudioMath.Rms(speech);

        filter.Process(rumble);
        filter.Reset();
        filter.Process(speech);

        // The tail of each buffer is past the filter's settling time.
        Assert.True(AudioMath.Rms(rumble.AsSpan(2_400)) < rumbleInput * 0.35f);
        Assert.True(AudioMath.Rms(speech.AsSpan(2_400)) > speechInput * 0.9f);
    }

    [Fact]
    public void HighPass_ClampsFrequencyToItsRange()
    {
        var filter = new HighPassFilter(SampleRate, 10f);
        Assert.Equal(HighPassFilter.MinimumFrequency, filter.Frequency);

        filter.SetFrequency(5_000f);
        Assert.Equal(HighPassFilter.MaximumFrequency, filter.Frequency);
    }

    [Fact]
    public void Limiter_PullsPeaksUnderTheCeiling()
    {
        var limiter = new SoftLimiter(SampleRate);
        var loud = Tone(220, 9_600, amplitude: 4f);

        // Frame by frame, the way the engine calls it.
        for (var offset = 0; offset < loud.Length; offset += 480)
        {
            limiter.Process(loud.AsSpan(offset, 480), enabled: true);
        }

        Assert.True(AudioMath.Peak(loud) <= 0.971f);
        Assert.True(limiter.GainReductionDb > 6f);
        Assert.All(loud, sample => Assert.True(float.IsFinite(sample)));
    }

    [Fact]
    public void Limiter_LeavesQuietSignalAlone()
    {
        var limiter = new SoftLimiter(SampleRate);
        var quiet = Tone(220, 4_800, amplitude: 0.3f);
        var expected = (float[])quiet.Clone();

        limiter.Process(quiet, enabled: true);

        Assert.Equal(0f, limiter.GainReductionDb);
        for (var index = 0; index < quiet.Length; index++)
        {
            Assert.Equal(expected[index], quiet[index], precision: 5);
        }
    }

    [Fact]
    public void Limiter_IsTransparentWhenDisabled()
    {
        var limiter = new SoftLimiter(SampleRate);
        var samples = Tone(220, 480, amplitude: 2f);
        var expected = (float[])samples.Clone();

        limiter.Process(samples, enabled: false);

        Assert.Equal(expected, samples);
    }

    [Fact]
    public void AutoGain_LiftsQuietSpeechTowardsTheTarget()
    {
        var control = new AutoGainControl(frameRate: 100f);
        var startGain = control.CurrentGain;

        for (var frame = 0; frame < 600; frame++)
        {
            control.Process(Tone(200, 480, amplitude: 0.02f), voiceProbability: 0.9f, targetDb: -18f, enabled: true);
        }

        Assert.True(control.CurrentGain > startGain * 2f);
        Assert.True(control.CurrentGain <= 6f);
    }

    [Fact]
    public void AutoGain_IgnoresSilenceBetweenSentences()
    {
        var control = new AutoGainControl(frameRate: 100f);

        for (var frame = 0; frame < 300; frame++)
        {
            control.Process(new float[480], voiceProbability: 0.1f, targetDb: -18f, enabled: true);
        }

        Assert.Equal(1f, control.CurrentGain, precision: 3);
    }

    [Fact]
    public void AutoGain_ReturnsToUnityWhenDisabled()
    {
        var control = new AutoGainControl(frameRate: 100f);
        for (var frame = 0; frame < 200; frame++)
        {
            control.Process(Tone(200, 480, amplitude: 0.02f), voiceProbability: 0.9f, targetDb: -18f, enabled: true);
        }

        control.Process(new float[480], voiceProbability: 0f, targetDb: -18f, enabled: false);

        Assert.Equal(1f, control.CurrentGain);
    }

    [Fact]
    public void Scope_ReturnsNewestValuesLast()
    {
        var scope = new WaveformScope(8);
        for (var index = 1; index <= 10; index++)
        {
            scope.Push(index / 10f);
        }

        var snapshot = new float[8];
        scope.CopyTo(snapshot);

        Assert.Equal(1f, snapshot[^1], precision: 5);
        Assert.Equal(0.9f, snapshot[^2], precision: 5);
        Assert.Equal(0.3f, snapshot[0], precision: 5);
    }

    [Fact]
    public void Scope_PadsWithSilenceBeforeAnythingIsPushed()
    {
        var scope = new WaveformScope(16);
        scope.Push(0.5f);

        var snapshot = new float[16];
        scope.CopyTo(snapshot);

        Assert.Equal(0.5f, snapshot[^1], precision: 5);
        Assert.All(snapshot[..^1], value => Assert.Equal(0f, value));
    }

    [Fact]
    public void Rms_MatchesTheKnownValueForASineWave()
    {
        var tone = Tone(1_000, 4_800, amplitude: 1f);

        Assert.Equal(0.7071f, AudioMath.Rms(tone), precision: 3);
        Assert.Equal(-100f, AudioMath.ToDecibels(0f));
        Assert.Equal(0f, AudioMath.ToDecibels(1f), precision: 4);
    }
}

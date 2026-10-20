using Muted.Core.Audio;
using Muted.Core.Dsp;

namespace Muted.Core.Tests;

public sealed class VoiceTuningTests
{
    private const int Rate = CalibrationRecorder.SampleRate;
    private const int Frame = CalibrationRecorder.FrameSize;

    // ---------------------------------------------------------------- recorder

    [Fact]
    public void Recorder_KeepsFramesAndProbabilitiesUntilSealed()
    {
        var recorder = new CalibrationRecorder(1);
        var frame = Enumerable.Repeat(0.25f, Frame).ToArray();

        recorder.Write(frame, 0.8f);
        recorder.Write(new float[Frame - 1], 0.5f); // wrong size is ignored
        recorder.Write(frame, 0.4f);
        var recording = recorder.Finish();
        recorder.Write(frame, 0.9f); // sealed

        Assert.Equal(2, recording.Frames);
        Assert.Equal(2 * Frame, recording.Samples.Length);
        Assert.Equal([0.8f, 0.4f], recording.VoiceProbabilities);
    }

    [Fact]
    public void Recorder_StopsAtCapacity()
    {
        var recorder = new CalibrationRecorder(1);
        var frame = new float[Frame];
        for (var index = 0; index < recorder.CapacityFrames + 10; index++)
        {
            recorder.Write(frame, 0f);
        }

        Assert.True(recorder.Complete);
        Assert.Equal(recorder.CapacityFrames, recorder.Finish().Frames);
    }

    [Fact]
    public void Recorder_RejectsBadLength() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CalibrationRecorder(0));

    // ---------------------------------------------------------------- analyzer

    [Fact]
    public void Analyzer_MeasuresAndRecommendsFromAGuidedTake()
    {
        var recording = Take(pitch: 140f, speechDb: -20f, loudDb: -8f);

        var measurement = VoiceAnalyzer.Measure(recording, 4, 19);
        var recommendation = VoiceAnalyzer.Recommend(measurement);

        Assert.InRange(measurement.PitchHz, 130f, 150f);
        Assert.InRange(measurement.NoiseFloorDb, -66f, -54f);
        Assert.True(measurement.SpeechSeconds > 10);
        Assert.False(measurement.Clipped);
        Assert.True(recommendation.Reliable);

        // Speech lands on the target once the boost is applied.
        var boostedSpeech = measurement.SpeechLevelDb + (20f * MathF.Log10(recommendation.InputGain));
        Assert.InRange(boostedSpeech, VoiceAnalyzer.TargetSpeechDb - 1.5f, VoiceAnalyzer.TargetSpeechDb + 1.5f);

        // The shout limit sits between normal peaks and the shout.
        var gainDb = 20f * MathF.Log10(recommendation.InputGain);
        Assert.True(recommendation.ShoutThresholdDb > measurement.SpeechPeakDb + gainDb + 2.9f);
        Assert.True(recommendation.ShoutThresholdDb < MathF.Min(0f, measurement.LoudPeakDb + gainDb));

        Assert.InRange(recommendation.VoiceThreshold, 0.3f, 0.85f);
        Assert.Equal(70f, recommendation.HighPassFrequency);
    }

    [Fact]
    public void Analyzer_GivesLowVoicesLessLowEndThanHighVoices()
    {
        var low = VoiceAnalyzer.Recommend(VoiceAnalyzer.Measure(Take(pitch: 95f, speechDb: -26f, loudDb: -6f), 4, 19));
        var high = VoiceAnalyzer.Recommend(VoiceAnalyzer.Measure(Take(pitch: 210f, speechDb: -26f, loudDb: -6f), 4, 19));

        Assert.True(low.Warmth < high.Warmth);
        Assert.True(low.Proximity < high.Proximity);
    }

    [Fact]
    public void Analyzer_NeverPushesNormalPeaksIntoClipping()
    {
        var recommendation = VoiceAnalyzer.Recommend(new VoiceMeasurement(
            NoiseFloorDb: -70f, SpeechLevelDb: -30f, SpeechPeakDb: -6f, LoudPeakDb: -1f,
            PitchHz: 150f, SibilanceDb: -20f, DynamicRangeDb: 12f, RumbleShareDb: -20f,
            QuietVoiceProbability: 0.1f, SpeechVoiceProbability: 0.9f, SpeechSeconds: 12, Clipped: false));

        var gainDb = 20f * MathF.Log10(recommendation.InputGain);
        Assert.True(-6f + gainDb <= VoiceAnalyzer.PeakHeadroomDb + 0.01f);
    }

    [Fact]
    public void Analyzer_IsUnreliableWithoutSpeech()
    {
        var recording = Take(pitch: 140f, speechDb: -34f, loudDb: -8f, speechProbability: 0.05f);

        var recommendation = VoiceAnalyzer.Recommend(VoiceAnalyzer.Measure(recording, 4, 19));

        Assert.False(recommendation.Reliable);
        Assert.Equal(1f, recommendation.InputGain, 3);
        Assert.NotEmpty(recommendation.Notes);
    }

    [Fact]
    public void Analyzer_WarnsAboutClippingAndMissingShout()
    {
        var recommendation = VoiceAnalyzer.Recommend(new VoiceMeasurement(
            NoiseFloorDb: -70f, SpeechLevelDb: -12f, SpeechPeakDb: 0f, LoudPeakDb: 0f,
            PitchHz: float.NaN, SibilanceDb: float.NaN, DynamicRangeDb: 10f, RumbleShareDb: -20f,
            QuietVoiceProbability: 0.1f, SpeechVoiceProbability: 0.9f, SpeechSeconds: 12, Clipped: true));

        Assert.Contains(recommendation.Notes, note => note.Contains("clips"));
        Assert.Contains(recommendation.Notes, note => note.Contains("No clearly louder phrase"));
        Assert.InRange(recommendation.ShoutThresholdDb, ShoutGuard.MinimumThresholdDb, ShoutGuard.MaximumThresholdDb);
    }

    [Fact]
    public void Analyzer_RaisesHighPassForRumble()
    {
        var recommendation = VoiceAnalyzer.Recommend(new VoiceMeasurement(
            NoiseFloorDb: -55f, SpeechLevelDb: -25f, SpeechPeakDb: -10f, LoudPeakDb: -2f,
            PitchHz: 150f, SibilanceDb: -20f, DynamicRangeDb: 12f, RumbleShareDb: -1f,
            QuietVoiceProbability: 0.1f, SpeechVoiceProbability: 0.9f, SpeechSeconds: 12, Clipped: false));

        Assert.Equal(100f, recommendation.HighPassFrequency);
    }

    // ---------------------------------------------------------------- tracker

    [Fact]
    public void Tracker_FollowsSpeechAndRoomLevels()
    {
        var tracker = new VoiceTracker();
        var quiet = Noise(Frame, -60f, seed: 1);
        var speech = Tone(Frame, 150f, -30f);

        for (var index = 0; index < 400; index++)
        {
            tracker.Process(quiet, 0.03f, excluded: false);
        }

        for (var index = 0; index < 1_500; index++)
        {
            tracker.Process(speech, 0.95f, excluded: false);
        }

        Assert.InRange(tracker.NoiseFloorDb, -63f, -57f);
        Assert.InRange(tracker.SpeechLevelDb, -31f, -29f);
        Assert.Equal(1_500, tracker.SpeechFrames);
        Assert.InRange(tracker.QuietProbabilityP95, 0.04f, 0.11f);
    }

    [Fact]
    public void Tracker_IgnoresExcludedFrames()
    {
        var tracker = new VoiceTracker();
        var shout = Tone(Frame, 150f, -3f);

        for (var index = 0; index < 200; index++)
        {
            tracker.Process(shout, 0.95f, excluded: true);
        }

        Assert.Equal(0, tracker.SpeechFrames);
        Assert.Equal(-100f, tracker.SpeechLevelDb);
    }

    [Fact]
    public void Tracker_DoesNotAllocate()
    {
        var tracker = new VoiceTracker();
        var frame = Tone(Frame, 150f, -30f);
        tracker.Process(frame, 0.9f, false);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1_000; index++)
        {
            tracker.Process(frame, index % 2 == 0 ? 0.9f : 0.05f, false);
        }

        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    // ---------------------------------------------------------------- auto tuner

    [Fact]
    public void Tuner_WaitsForTimeAndNewSpeech()
    {
        var tuner = new AutoTuner();
        tuner.Reset(1f, 0.55f, speechFrames: 0, nowSeconds: 0);

        Assert.Null(tuner.Update(5, -40f, 0.05f, 1_000, 1f, 0.55f));   // too soon
        Assert.Null(tuner.Update(10, -40f, 0.05f, 100, 1f, 0.55f));    // too little speech
        Assert.NotNull(tuner.Update(10, -40f, 0.05f, 1_000, 1f, 0.55f));
    }

    [Fact]
    public void Tuner_RaisesBoostInSmallBoundedSteps()
    {
        var tuner = new AutoTuner();
        tuner.Reset(1f, 0.55f, 0, 0);
        var gain = 1f;
        var threshold = 0.55f;
        long speech = 0;

        var first = tuner.Update(10, -40f, 0.43f, speech += 400, gain, threshold);
        Assert.NotNull(first);
        Assert.InRange(20f * MathF.Log10(first!.Value.InputGain), 1.4f, 1.6f);

        for (var step = 2; step < 40; step++)
        {
            if (tuner.Update(step * 10, -40f, 0.43f, speech += 400, gain, threshold) is { } next)
            {
                gain = next.InputGain;
                threshold = next.VoiceThreshold;
            }
        }

        // 18 dB short, but the boost never wanders more than 6 dB from where it started.
        Assert.InRange(20f * MathF.Log10(gain), AutoTuner.MaximumGainDriftDb - 0.01f, AutoTuner.MaximumGainDriftDb + 0.01f);
        Assert.InRange(threshold, 0.54f, 0.56f);
    }

    [Fact]
    public void Tuner_MovesTheGateTowardsTheRoom()
    {
        var tuner = new AutoTuner();
        tuner.Reset(1f, 0.4f, 0, 0);

        var step = tuner.Update(10, VoiceAnalyzer.TargetSpeechDb, 0.6f, 400, 1f, 0.4f);

        Assert.NotNull(step);
        Assert.Equal(1f, step!.Value.InputGain, 3);
        Assert.Equal(0.45f, step.Value.VoiceThreshold, 3);
        Assert.Contains("stricter", step.Value.Reason);
    }

    [Fact]
    public void Tuner_LeavesGoodSettingsAloneAndStops()
    {
        var tuner = new AutoTuner();
        tuner.Reset(1f, 0.42f, 0, 0);

        Assert.Null(tuner.Update(10, VoiceAnalyzer.TargetSpeechDb + 1f, 0.3f, 400, 1f, 0.42f));

        tuner.Stop();
        Assert.Null(tuner.Update(30, -45f, 0.3f, 2_000, 1f, 0.42f));
    }

    [Fact]
    public void Tuner_SurvivesAnEngineRestart()
    {
        var tuner = new AutoTuner();
        tuner.Reset(1f, 0.55f, speechFrames: 5_000, nowSeconds: 0);

        Assert.Null(tuner.Update(10, -40f, 0.43f, 100, 1f, 0.55f));     // counter restarted from zero
        Assert.NotNull(tuner.Update(20, -40f, 0.43f, 500, 1f, 0.55f));
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>4 s room noise, 15 s voiced "speech" with sibilant bursts, 5 s shout.</summary>
    private static CalibrationRecording Take(float pitch, float speechDb, float loudDb, float speechProbability = 0.92f)
    {
        const int seconds = 24;
        var frames = seconds * Rate / Frame;
        var samples = new float[frames * Frame];
        var probabilities = new float[frames];
        var random = new Random(7);
        var noise = MathF.Pow(10f, -60f / 20f) * MathF.Sqrt(3f);
        var speech = MathF.Pow(10f, speechDb / 20f);
        var loud = MathF.Pow(10f, loudDb / 20f);

        for (var index = 0; index < samples.Length; index++)
        {
            var time = index / (float)Rate;
            var value = ((float)random.NextDouble() * 2f - 1f) * noise;
            if (time >= 4f && time < 19f)
            {
                // A voiced tone with a few harmonics, and a syllable-rate swell.
                var swell = 0.55f + (0.45f * MathF.Sin(2f * MathF.PI * 3f * time));
                var voiced = MathF.Sin(2f * MathF.PI * pitch * time) + (0.5f * MathF.Sin(4f * MathF.PI * pitch * time)) +
                             (0.25f * MathF.Sin(6f * MathF.PI * pitch * time));
                value += voiced * speech * 0.75f * swell;
                if ((int)(time * 4f) % 5 == 0)
                {
                    value += MathF.Sin(2f * MathF.PI * 6_500f * time) * speech * 0.3f;
                }
            }
            else if (time >= 19f)
            {
                value += MathF.Sin(2f * MathF.PI * pitch * 1.3f * time) * loud;
            }

            samples[index] = value;
        }

        for (var frame = 0; frame < frames; frame++)
        {
            var time = frame * Frame / (float)Rate;
            probabilities[frame] = time < 4f ? 0.04f : speechProbability;
        }

        return new CalibrationRecording(samples, probabilities);
    }

    private static float[] Tone(int length, float frequency, float db)
    {
        var amplitude = MathF.Pow(10f, db / 20f) * MathF.Sqrt(2f);
        return Enumerable.Range(0, length)
            .Select(index => amplitude * MathF.Sin(2f * MathF.PI * frequency * index / Rate))
            .ToArray();
    }

    private static float[] Noise(int length, float db, int seed)
    {
        var random = new Random(seed);
        var amplitude = MathF.Pow(10f, db / 20f) * MathF.Sqrt(3f);
        return Enumerable.Range(0, length).Select(_ => ((float)random.NextDouble() * 2f - 1f) * amplitude).ToArray();
    }
}

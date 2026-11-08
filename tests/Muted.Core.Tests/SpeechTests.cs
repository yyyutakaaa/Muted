using Muted.Core.Audio;
using Muted.Core.Speech;

namespace Muted.Core.Tests;

public sealed class SpeechTests
{
    /// <summary>A deterministic 1.5 s test signal; the expected values below come from the Python reference.</summary>
    private static float[] Signal16k()
    {
        var signal = new float[24_000];
        for (var index = 0; index < signal.Length; index++)
        {
            var t = index / 16_000.0;
            signal[index] = (float)((0.3 * Math.Sin(2 * Math.PI * 440 * t)) +
                                    (0.1 * Math.Sin(2 * Math.PI * 3000 * t)) +
                                    (0.02 * Math.Sin(2 * Math.PI * 97 * t) * Math.Cos(2 * Math.PI * 1.3 * t)));
        }

        return signal;
    }

    [Fact]
    public void WhisperFeatures_MatchTheReferenceImplementation()
    {
        var features = new WhisperFeatures().Compute(Signal16k());
        Assert.Equal(WhisperFeatures.MelBands * WhisperFeatures.Frames, features.Length);

        float At(int band, int frame) => features[(band * WhisperFeatures.Frames) + frame];
        Assert.Equal(0.9603f, At(0, 0), 0.003f);
        Assert.Equal(1.2378f, At(10, 5), 0.003f);
        Assert.Equal(-0.6727f, At(20, 50), 0.003f);
        Assert.Equal(-0.5774f, At(79, 149), 0.003f);
        Assert.Equal(-0.6727f, At(30, 2999), 0.003f);
        Assert.Equal(-0.66127f, features.Average(), 0.002f);
    }

    [Fact]
    public void SpeakerFeatures_MatchTheReferenceImplementation()
    {
        var features = new SpeakerFeatures().Compute(Signal16k());
        Assert.Equal(148 * SpeakerFeatures.Bands, features.Length);

        float At(int frame, int band) => features[(frame * SpeakerFeatures.Bands) + band];
        Assert.Equal(-0.08f, At(0, 0), 0.01f);
        Assert.Equal(-1.056f, At(5, 10), 0.01f);
        Assert.Equal(-0.0564f, At(50, 20), 0.01f);
        Assert.Equal(-0.354f, At(100, 40), 0.01f);
        Assert.Equal(0.3678f, At(147, 79), 0.01f);
    }

    [Fact]
    public void Downsampler_KeepsSpeechAndRemovesWhatWouldFoldBack()
    {
        static double OutputRms(double hz)
        {
            var input = new float[48_000];
            for (var index = 0; index < input.Length; index++)
            {
                input[index] = (float)Math.Sin(2 * Math.PI * hz * index / 48_000);
            }

            var output = Downsampler.Downsample(input);
            Assert.Equal(16_000, output.Length);
            return Math.Sqrt(output.Skip(100).Average(sample => sample * (double)sample));
        }

        Assert.InRange(OutputRms(1_000), 0.68, 0.73);
        Assert.InRange(OutputRms(6_000), 0.6, 0.73);
        Assert.True(OutputRms(12_000) < 0.01, "A 12 kHz tone would fold back to 4 kHz.");
    }

    [Fact]
    public void Downsampler_GivesTheSameResultFrameByFrame()
    {
        var input = new float[4_800];
        var random = new Random(3);
        for (var index = 0; index < input.Length; index++)
        {
            input[index] = (float)((random.NextDouble() * 2) - 1);
        }

        var whole = Downsampler.Downsample(input);
        var streaming = new Downsampler();
        var output = new float[160 + 1];
        var pieces = new List<float>();
        for (var frame = 0; frame < 10; frame++)
        {
            var written = streaming.Process(input.AsSpan(frame * 480, 480), output);
            pieces.AddRange(output.Take(written));
        }

        Assert.Equal(whole, pieces);
    }

    [Fact]
    public void ReadingMatcher_FollowsAForgivingTranscript()
    {
        var matcher = new ReadingMatcher(
            "The quick brown fox jumps over the lazy dog. She sells sea shells by the sea shore, " +
            "and on Sundays we play games together until late at night.");

        Assert.False(matcher.Match("").Finished);

        var halfway = matcher.Match("The quick brown fox jumps over the lazy dog. She sells");
        Assert.False(halfway.Finished);
        Assert.InRange(halfway.Fraction, 0.35, 0.5);

        // Whisper writes compounds as one word and mishears the odd one.
        var done = matcher.Match(
            "The quick brown fox jumps over the lazy dog. She sells seashells by the seashore, " +
            "and on Sunday we play game together until late at night.");
        Assert.True(done.Finished);
        Assert.Equal(1.0, done.Fraction);

        // The last word lost to the end of the recording still counts.
        Assert.True(matcher.Match("brown fox jumps over the lazy dog she sells sea shells by the sea shore and on sundays we play games together until late at").Finished);

        // Saying only the ending is not reading the text.
        Assert.False(matcher.Match("until late at night").Finished);
    }

    [Fact]
    public void ReadingMatcher_RecognisesTheShortLoudPhrase()
    {
        var matcher = new ReadingMatcher("No way, behind you!");
        Assert.True(matcher.Match("No way! Behind you!").Finished);
        Assert.True(matcher.Match("no way behind").Finished);
        Assert.False(matcher.Match("no way").Finished);
    }

    [Fact]
    public void VoiceProfile_RoundTripsAndComparesByDirection()
    {
        var profile = VoiceProfile.Average([[1f, 0f, 0f], [0.9f, 0.1f, 0f]]);
        var restored = VoiceProfile.FromBase64(profile.ToBase64());
        Assert.NotNull(restored);
        Assert.Equal(profile.Embedding.ToArray(), restored!.Embedding.ToArray());
        Assert.Equal(1f, profile.Similarity([2f, 0.1f, 0f]), 0.01f);
        Assert.InRange(profile.Similarity([0f, 0f, 1f]), -0.01f, 0.01f);
        Assert.True(float.IsNaN(profile.Similarity([1f, 0f])));

        Assert.Null(VoiceProfile.FromBase64(null));
        Assert.Null(VoiceProfile.FromBase64("not base64!"));
        Assert.Null(VoiceProfile.FromBase64(Convert.ToBase64String(new byte[3])));
    }

    [Fact]
    public void SpeakerGate_HoldsOtherVoicesBackAndFailsOpen()
    {
        var gate = new SpeakerGate(48_000);
        var frame = new float[480];

        float Run(float similarity, int frames = 20, bool enabled = true)
        {
            for (var index = 0; index < frames; index++)
            {
                Array.Fill(frame, 0.5f);
                gate.Process(frame, similarity, 0.5f, enabled);
            }

            return frame[^1] / 0.5f;
        }

        Assert.Equal(1f, Run(0.8f), 0.001f);
        Assert.Equal(SpeakerGate.BlockedGain, Run(0.1f), 0.001f);
        Assert.True(gate.IsBlocking);

        // Just above the threshold is not enough to open again: hysteresis.
        Assert.Equal(SpeakerGate.BlockedGain, Run(SpeakerGate.BlockBelow(0.5f) + 0.05f), 0.001f);
        Assert.Equal(1f, Run(0.8f), 0.001f);

        // No score means no decision: let the sound through.
        Run(0.1f);
        Assert.Equal(1f, Run(float.NaN), 0.001f);
        Run(0.1f);
        Assert.Equal(1f, Run(0.1f, enabled: false), 0.001f);
    }

    [Fact]
    public void SpeakerGate_ClosesSmoothlyWithoutAClick()
    {
        var gate = new SpeakerGate(48_000);
        var frame = new float[480];
        Array.Fill(frame, 1f);
        gate.Process(frame, 0.1f, 0.5f, true);
        for (var index = 1; index < frame.Length; index++)
        {
            Assert.True(frame[index - 1] - frame[index] < 0.001f);
        }
    }

    [Fact]
    public void CalibrationRecorder_KeepsTheCleanedVoiceAndCanBeReadWhileRecording()
    {
        var recorder = new CalibrationRecorder(1);
        var raw = Enumerable.Repeat(0.5f, 480).ToArray();
        var cleaned = Enumerable.Repeat(0.25f, 480).ToArray();
        recorder.Write(raw, cleaned, 0.9f);
        recorder.Write(raw, cleaned, 0.1f);

        Assert.Equal([0.9f, 0.1f], recorder.ProbabilitiesSince(0));
        Assert.Equal([0.1f], recorder.ProbabilitiesSince(1));
        Assert.Equal(480, recorder.RawSince(0, maxFrames: 1).Length);

        var recording = recorder.Finish();
        Assert.All(recording.Samples, sample => Assert.Equal(0.5f, sample));
        Assert.All(recording.Cleaned, sample => Assert.Equal(0.25f, sample));
    }
}

using System.IO;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using Muted.Audio.Windows.Speech;
using Muted.Core.Speech;

namespace Muted.Audio.Windows.Tests;

/// <summary>
/// The speech models on real speech. Windows' own text-to-speech voices read the
/// sentences, so the tests know exactly what was said and by which voice. A PC without
/// any installed voice skips the checks that need one.
/// </summary>
public sealed class SpeechModelTests
{
    private const string Reading =
        "The quick brown fox jumps over the lazy dog. She sells sea shells by the sea shore, " +
        "and on Sundays we play games together until late at night.";

    private const string News =
        "Tonight on the news, heavy rain is expected across the country, and the football match has been moved to next week.";

    public static TheoryData<string> Models => new()
    {
        SpeechModels.WhisperEncoderFileName,
        SpeechModels.WhisperDecoderFileName,
        SpeechModels.WhisperTokensFileName,
        SpeechModels.SpeakerFileName
    };

    [Theory]
    [MemberData(nameof(Models))]
    public void ModelShipsWithTheBuildAndMatchesItsChecksum(string fileName)
    {
        var path = SpeechModels.PathOf(fileName);
        Assert.True(File.Exists(path), $"Missing {path}");
        var expected = File.ReadAllText(path + ".sha256").Trim().Split(' ', 2)[0];
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Whisper_HearsTheSentenceThatWasRead()
    {
        var voices = Voices();
        if (voices.Count == 0)
        {
            return;
        }

        using var recognizer = new WhisperRecognizer();
        var heard = recognizer.Transcribe(Speak(voices[0], Reading, 16_000));
        Assert.True(new ReadingMatcher(Reading).Match(heard).Finished, $"Heard: {heard}");

        // Half the sentence is not the end of it.
        var half = Speak(voices[0], "The quick brown fox jumps over the lazy dog.", 16_000);
        Assert.False(new ReadingMatcher(Reading).Match(recognizer.Transcribe(half)).Finished);
    }

    [Fact]
    public void Whisper_HearsNothingInSilence()
    {
        using var recognizer = new WhisperRecognizer();
        var heard = recognizer.Transcribe(new float[16_000 * 3]);
        Assert.False(new ReadingMatcher(Reading).Match(heard).Finished);
    }

    [Fact]
    public void SpeakerEmbedder_TellsTwoVoicesApart()
    {
        var voices = Voices();
        if (voices.Count < 2)
        {
            return;
        }

        using var embedder = new SpeakerEmbedder();
        var profile = new VoiceProfile(embedder.Embed(Speak(voices[0], Reading, 16_000)));
        var same = profile.Similarity(embedder.Embed(Speak(voices[0], News, 16_000)));
        var other = profile.Similarity(embedder.Embed(Speak(voices[1], News, 16_000)));
        Assert.True(same > 0.6f, $"Same voice: {same:0.00}");
        Assert.True(other < same - 0.2f && other < 0.5f, $"Other voice: {other:0.00}, same voice: {same:0.00}");
    }

    [Fact]
    public void SpeakerVerifier_ScoresTheVoiceItHearsAgainstTheProfile()
    {
        var voices = Voices();
        if (voices.Count < 2)
        {
            return;
        }

        VoiceProfile profile;
        using (var embedder = new SpeakerEmbedder())
        {
            profile = new VoiceProfile(embedder.Embed(Speak(voices[0], Reading, 16_000)));
        }

        using var verifier = new SpeakerVerifier();
        verifier.Configure(true, profile);
        var same = Feed(verifier, Speak(voices[0], News, 48_000));
        var other = Feed(verifier, Speak(voices[1], News, 48_000));
        Assert.True(same > 0.55f, $"Same voice: {same:0.00}, status {verifier.Status}, error {verifier.Error}, checks {verifier.Checks}");
        Assert.True(other < same - 0.15f, $"Other voice: {other:0.00}, same voice: {same:0.00}");

        verifier.Configure(false, profile);
        Assert.True(float.IsNaN(verifier.Similarity));
    }

    /// <summary>Plays speech into the verifier in real-time-sized frames and returns its last score.</summary>
    private static float Feed(SpeakerVerifier verifier, float[] speech48k)
    {
        var frame = new float[480];
        var checks = verifier.Checks;
        for (var start = 0; start + frame.Length <= speech48k.Length; start += frame.Length)
        {
            speech48k.AsSpan(start, frame.Length).CopyTo(frame);
            var rms = Math.Sqrt(frame.Average(sample => sample * (double)sample));
            verifier.Push(frame, rms > 0.01 ? 0.9f : 0.05f);
            Thread.Sleep(2);
        }

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (verifier.Checks == checks && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(20);
        }

        Thread.Sleep(300);
        return verifier.Similarity;
    }

    private static List<string> Voices()
    {
        try
        {
            using var synthesizer = new SpeechSynthesizer();
            return synthesizer.GetInstalledVoices()
                .Where(voice => voice.Enabled && voice.VoiceInfo.Culture.TwoLetterISOLanguageName == "en")
                .Select(voice => voice.VoiceInfo.Name)
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static float[] Speak(string voice, string text, int sampleRate)
    {
        using var synthesizer = new SpeechSynthesizer();
        synthesizer.SelectVoice(voice);
        using var stream = new MemoryStream();
        synthesizer.SetOutputToAudioStream(
            stream,
            new SpeechAudioFormatInfo(sampleRate, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
        synthesizer.Speak(text);
        synthesizer.SetOutputToNull();
        var bytes = stream.ToArray();
        var samples = new float[bytes.Length / 2];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = BitConverter.ToInt16(bytes, index * 2) / 32768f;
        }

        return samples;
    }
}

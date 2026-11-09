using System.Globalization;
using System.IO;
using Muted.Core.Speech;

namespace Muted.Audio.Windows.Tests;

/// <summary>
/// How every setup of the chain sounds to the other side, scored with DNSMOS after the
/// trip through Opus, on a noisy recording: fan, key clicks, speech with pauses. Writes a
/// CSV when MUTED_QUALITY_REPORT names a file, and fails when a change makes the speech
/// itself sound worse.
/// </summary>
public sealed class QualityHarnessTests
{
    private const int SampleRate = 48_000;
    private const int OpusBitrate = 24_000;

    private static readonly ChainSetup[] Setups =
    [
        new("rnnoise-old-gate", null, NewGate: false, RoomTone: false),
        new("rnnoise-new-gate", null, NewGate: true, RoomTone: false),
        new("classic-old", "deepfilternet3", NewGate: false, RoomTone: false),
        new("classic-new", "deepfilternet3", NewGate: true, RoomTone: true),
        new("balanced-old", "dpdfnet2_48khz_hr", NewGate: false, RoomTone: false),
        new("balanced-new", "dpdfnet2_48khz_hr", NewGate: true, RoomTone: true),
        new("maximum-old", "dpdfnet8_48khz_hr", NewGate: false, RoomTone: false),
        new("maximum-new", "dpdfnet8_48khz_hr", NewGate: true, RoomTone: true)
    ];

    /// <summary>Eleven seconds: the speech sample three times, with fan noise and key clicks all through.</summary>
    internal static float[] NoisyRecording()
    {
        var speech = SpeechFixture.Load();
        var audio = new float[SampleRate * 11];
        var random = new Random(23);
        var smooth = 0f;
        for (var index = 0; index < audio.Length; index++)
        {
            smooth = (smooth * 0.97f) + ((((float)random.NextDouble() * 2f) - 1f) * 0.03f);
            audio[index] = smooth * 0.25f;
        }

        // Key clicks: short decaying bursts, about five a second.
        for (var click = 0; click < 55; click++)
        {
            var at = random.Next(audio.Length - 400);
            var level = 0.05f + ((float)random.NextDouble() * 0.1f);
            for (var index = 0; index < 400; index++)
            {
                audio[at + index] += level * MathF.Exp(-index / 60f) * (((float)random.NextDouble() * 2f) - 1f);
            }
        }

        foreach (var second in new[] { 1.0, 4.5, 8.0 })
        {
            var at = (int)(second * SampleRate);
            for (var index = 0; index < speech.Length && at + index < audio.Length; index++)
            {
                audio[at + index] += speech[index];
            }
        }

        return audio;
    }

    [Fact]
    public void TheNewChainKeepsTheVoiceAtLeastAsGoodAsTheOld()
    {
        var input = NoisyRecording();
        using var dnsmos = new Dnsmos();
        var scores = new Dictionary<string, (double Sig, double Bak, double Ovrl)>
        {
            ["noisy-input"] = dnsmos.Score(Downsampler.Downsample(OfflineChain.ThroughOpus(input, OpusBitrate)))
        };

        foreach (var setup in Setups)
        {
            var output = OfflineChain.Run(input, setup);
            scores[setup.Name] = dnsmos.Score(Downsampler.Downsample(OfflineChain.ThroughOpus(output, OpusBitrate)));
        }

        var report = Environment.GetEnvironmentVariable("MUTED_QUALITY_REPORT");
        if (!string.IsNullOrWhiteSpace(report))
        {
            var lines = new List<string> { "setup,sig,bak,ovrl" };
            lines.AddRange(scores.Select(pair => string.Create(
                CultureInfo.InvariantCulture,
                $"{pair.Key},{pair.Value.Sig:0.000},{pair.Value.Bak:0.000},{pair.Value.Ovrl:0.000}")));
            File.WriteAllLines(report, lines);
        }

        // Every setup must beat leaving the noise in.
        foreach (var setup in Setups)
        {
            Assert.True(scores[setup.Name].Ovrl > scores["noisy-input"].Ovrl, $"{setup.Name} scored below the noisy input.");
        }

        // The voice itself (SIG) must not get worse with the new gate and room tone.
        foreach (var model in new[] { "rnnoise", "classic", "balanced", "maximum" })
        {
            var oldName = model == "rnnoise" ? "rnnoise-old-gate" : model + "-old";
            var newName = model == "rnnoise" ? "rnnoise-new-gate" : model + "-new";
            Assert.True(
                scores[newName].Sig >= scores[oldName].Sig - 0.05,
                $"{newName} SIG {scores[newName].Sig:0.00} fell below {oldName} {scores[oldName].Sig:0.00}.");
        }
    }
}

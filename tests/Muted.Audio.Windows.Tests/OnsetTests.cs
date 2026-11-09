using System.IO;
using Muted.Audio.Windows.Dsp;
using Muted.Core.Dsp;

namespace Muted.Audio.Windows.Tests;

/// <summary>
/// The first word after a silence: how much of it reaches the other side. Plays five
/// seconds of fan-like noise, then speech, through the real RNNoise and Studio Voice
/// models and the voice gate, the way the engine wires them, and measures the start of
/// the word that the gate lets through.
/// </summary>
public sealed class OnsetTests
{
    private const int Frame = 480;
    private const int SampleRate = 48_000;
    private const int SilenceSeconds = 5;

    /// <summary>What the gate did to the start of the first word.</summary>
    internal sealed record OnsetResult(double KeptFirst60Ms, double KeptFirst150Ms, double MillisecondsUntilOpen);

    /// <summary>The engine's gate decision before this change: the stricter of the two detectors.</summary>
    internal static OnsetResult Measure(string studioModel, Func<float, float, VoiceGate, float[], float> gate)
    {
        var speech = SpeechFixture.Load();
        var random = new Random(11);
        var input = new float[(SilenceSeconds * SampleRate) + speech.Length + (SampleRate / 2)];

        // Low, steady, pink-ish fan noise all the way through, at about -45 dBFS.
        var smooth = 0f;
        for (var index = 0; index < input.Length; index++)
        {
            smooth = (smooth * 0.97f) + (((float)random.NextDouble() * 2f) - 1f) * 0.03f;
            input[index] = smooth * 0.1f;
        }

        var speechStart = SilenceSeconds * SampleRate;
        for (var index = 0; index < speech.Length; index++)
        {
            input[speechStart + index] += speech[index];
        }

        // Where the voice really starts in the fixture: the first frame clearly above silence.
        var onset = speechStart;
        for (var frame = 0; frame * Frame < speech.Length; frame++)
        {
            if (AudioMath.Rms(speech.AsSpan(frame * Frame, Math.Min(Frame, speech.Length - (frame * Frame)))) > 0.01f)
            {
                onset = speechStart + (frame * Frame);
                break;
            }
        }

        using var rnnoise = new RnnoiseProcessor();
        using var studio = studioModel == "deepfilternet3"
            ? (IDenoiser)new DeepFilterProcessor(Path.Combine(AppContext.BaseDirectory, "models", "deepfilternet3.onnx"))
            : new DpdfNetProcessor(Path.Combine(AppContext.BaseDirectory, "models", studioModel + ".onnx"), studioModel);
        var voiceGate = new VoiceGate();
        var wet = new float[Frame];
        var studioWet = new float[Frame];
        var gated = new float[input.Length];
        var ungated = new float[input.Length];
        var gains = new List<float>();
        for (var start = 0; start + Frame <= input.Length; start += Frame)
        {
            var frame = input.AsSpan(start, Frame);
            var probability = rnnoise.Process(frame, wet);
            studio.Process(frame, studioWet);
            studioWet.CopyTo(ungated.AsSpan(start, Frame));
            var output = studioWet.ToArray();
            gains.Add(gate(probability, studio.SpeechConfidence, voiceGate, output));
            output.CopyTo(gated.AsSpan(start, Frame));
        }

        // Studio Voice delays the audio; the word reaches the gate this much later.
        var outputOnset = onset + IDenoiser.LatencySamples;
        double Kept(int samples)
        {
            var window = Math.Min(samples, gated.Length - outputOnset);
            var before = 0.0;
            var after = 0.0;
            for (var index = outputOnset; index < outputOnset + window; index++)
            {
                before += ungated[index] * (double)ungated[index];
                after += gated[index] * (double)gated[index];
            }

            return before <= 0 ? 0 : after / before;
        }

        var onsetFrame = outputOnset / Frame;
        var openFrame = onsetFrame;
        while (openFrame < gains.Count && gains[openFrame] < 0.9f)
        {
            openFrame++;
        }

        return new OnsetResult(Kept(SampleRate * 60 / 1000), Kept(SampleRate * 150 / 1000), (openFrame - onsetFrame) * 10.0);
    }

    /// <summary>
    /// The engine's gate before this change, kept here as it was: open only when both
    /// detectors are sure, snap to the new gain within one frame, close to silence.
    /// </summary>
    internal static float OldGate(float rnnoise, float studio, VoiceGate unused, float[] frame)
    {
        var probability = MathF.Min(rnnoise, studio);
        float target;
        if (probability >= 0.55f)
        {
            _oldHold = 25;
            target = 1f;
        }
        else if (_oldHold > 0)
        {
            _oldHold--;
            target = 1f;
        }
        else
        {
            target = 0f;
        }

        var step = (target - _oldGain) / frame.Length;
        var gain = _oldGain;
        for (var index = 0; index < frame.Length; index++)
        {
            gain += step;
            frame[index] *= gain;
        }

        _oldGain = target;
        return target;
    }

    [ThreadStatic] private static int _oldHold;
    [ThreadStatic] private static float _oldGain;

    /// <summary>The engine's gate now: RNNoise's early signal opens it, Studio Voice keeps it open.</summary>
    internal static float NewGate(float rnnoise, float studio, VoiceGate gate, float[] frame)
    {
        gate.Process(frame, rnnoise, studio, 0.55f, 250, true, VoiceGate.DefaultFloorGain);
        return gate.Gain;
    }

    [Theory]
    [InlineData("dpdfnet2_48khz_hr")]
    [InlineData("dpdfnet8_48khz_hr")]
    [InlineData("deepfilternet3")]
    public void TheGateLetsTheWholeFirstWordThrough(string model)
    {
        _oldHold = 0;
        _oldGain = 0f;
        var old = Measure(model, OldGate);
        var now = Measure(model, NewGate);
        var report = Environment.GetEnvironmentVariable("MUTED_ONSET_REPORT");
        if (!string.IsNullOrWhiteSpace(report))
        {
            File.AppendAllLines(report,
            [
                $"old {model}: kept60={old.KeptFirst60Ms:0.00} kept150={old.KeptFirst150Ms:0.00} open={old.MillisecondsUntilOpen:0} ms",
                $"new {model}: kept60={now.KeptFirst60Ms:0.00} kept150={now.KeptFirst150Ms:0.00} open={now.MillisecondsUntilOpen:0} ms"
            ]);
        }

        Assert.True(now.KeptFirst60Ms >= 0.9, $"New gate kept {now.KeptFirst60Ms:0.00} of the first 60 ms; the old one {old.KeptFirst60Ms:0.00}.");
        Assert.True(now.MillisecondsUntilOpen <= 0, $"The new gate opened {now.MillisecondsUntilOpen} ms late.");
    }
}

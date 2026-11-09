using System.IO;
using Concentus;
using Concentus.Enums;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Muted.Audio.Windows.Dsp;
using Muted.Core.Dsp;
using Muted.Core.Speech;

namespace Muted.Audio.Windows.Tests;

/// <summary>One way to set up the chain for the quality harness.</summary>
/// <param name="Name">How the CSV names it.</param>
/// <param name="StudioModel">The Studio Voice model file name, or null for RNNoise alone.</param>
/// <param name="NewGate">True for the early-opening gate with a floor; false for the old min() gate that closed to silence.</param>
/// <param name="RoomTone">True to keep the untouched signal under Studio Voice, at <paramref name="RoomToneGain"/>.</param>
/// <param name="RoomToneGain">How loud the untouched signal comes back with room tone on.</param>
internal sealed record ChainSetup(string Name, string? StudioModel, bool NewGate, bool RoomTone, float RoomToneGain = 0.1f, float? GateFloor = null);

/// <summary>
/// The parts of the engine that decide how a voice sounds, run offline on a recording:
/// RNNoise or a Studio Voice model, the room tone mix and the voice gate, wired the way
/// the engine wires them.
/// </summary>
internal static class OfflineChain
{
    private const int Frame = 480;
    private const float Threshold = 0.55f;
    private const int HoldMilliseconds = 250;
    private const float RnnoiseWetMix = 0.8f;

    public static float[] Run(float[] input, ChainSetup setup)
    {
        using var rnnoise = new RnnoiseProcessor();
        using var studio = setup.StudioModel switch
        {
            null => null,
            "deepfilternet3" => (IDenoiser)new DeepFilterProcessor(ModelPath("deepfilternet3")),
            var name => new DpdfNetProcessor(ModelPath(name), name)
        };
        var dryDelay = new SampleDelayLine(RnnoiseProcessor.AlgorithmicDelaySamples);
        var studioDryDelay = new SampleDelayLine(IDenoiser.LatencySamples);
        var gate = new VoiceGate();
        var wet = new float[Frame];
        var dry = new float[Frame];
        var studioWet = new float[Frame];
        var studioDry = new float[Frame];
        var mixed = new float[Frame];
        var output = new float[input.Length];
        var oldGain = 0f;
        var oldHold = 0;
        for (var start = 0; start + Frame <= input.Length; start += Frame)
        {
            var frame = input.AsSpan(start, Frame);
            dryDelay.Process(frame, dry);
            studioDryDelay.Process(frame, studioDry);
            var probability = rnnoise.Process(frame, wet);
            float early;
            float confirm;
            if (studio is null)
            {
                AudioMath.Mix(dry, wet, mixed, RnnoiseWetMix);
                early = 0f;
                confirm = probability;
            }
            else
            {
                studio.Process(frame, studioWet);
                AudioMath.Mix(studioDry, studioWet, mixed, setup.RoomTone ? 1f - setup.RoomToneGain : 1f);
                early = probability;
                confirm = setup.NewGate ? studio.SpeechConfidence : MathF.Min(probability, studio.SpeechConfidence);
            }

            if (setup.NewGate)
            {
                var floor = setup.GateFloor ?? (studio is not null && setup.RoomTone ? VoiceGate.RoomToneFloorGain : VoiceGate.DefaultFloorGain);
                gate.Process(mixed, early, confirm, Threshold, HoldMilliseconds, true, floor);
            }
            else
            {
                // The gate as it was: one probability, snap within a frame, close to silence.
                float target;
                if (confirm >= Threshold)
                {
                    oldHold = HoldMilliseconds / 10;
                    target = 1f;
                }
                else if (oldHold > 0)
                {
                    oldHold--;
                    target = 1f;
                }
                else
                {
                    target = 0f;
                }

                var step = (target - oldGain) / Frame;
                for (var index = 0; index < Frame; index++)
                {
                    oldGain += step;
                    mixed[index] *= oldGain;
                }

                oldGain = target;
            }

            mixed.CopyTo(output.AsSpan(start, Frame));
        }

        return output;
    }

    /// <summary>What Discord does to it on the way: Opus at the given bit rate, 20 ms frames.</summary>
    public static float[] ThroughOpus(float[] audio48k, int bitrate)
    {
        const int opusFrame = 960;
        var encoder = OpusCodecFactory.CreateEncoder(48_000, 1, OpusApplication.OPUS_APPLICATION_VOIP);
        encoder.Bitrate = bitrate;
        var decoder = OpusCodecFactory.CreateDecoder(48_000, 1);
        var pcm = new short[opusFrame];
        var decoded = new short[opusFrame];
        var packet = new byte[4000];
        var output = new float[audio48k.Length];
        for (var start = 0; start + opusFrame <= audio48k.Length; start += opusFrame)
        {
            for (var index = 0; index < opusFrame; index++)
            {
                pcm[index] = (short)Math.Clamp(audio48k[start + index] * 32767f, short.MinValue, short.MaxValue);
            }

            var length = encoder.Encode(pcm, opusFrame, packet, packet.Length);
            var samples = decoder.Decode(packet.AsSpan(0, length), decoded, opusFrame);
            for (var index = 0; index < samples; index++)
            {
                output[start + index] = decoded[index] / 32768f;
            }
        }

        return output;
    }

    private static string ModelPath(string name) => Path.Combine(AppContext.BaseDirectory, "models", name + ".onnx");
}

/// <summary>
/// Microsoft's DNSMOS P.835: predicts how people would rate speech quality (SIG),
/// background noise (BAK) and overall quality (OVRL), 1 to 5, without a clean reference.
/// Follows dnsmos_local.py: 16 kHz, 9.01 s windows a second apart, polynomial correction.
/// It hears only up to 8 kHz and rewards removing a lot of noise, so it guides; it does
/// not decide on its own.
/// </summary>
internal sealed class Dnsmos : IDisposable
{
    public const int SampleRate = 16_000;
    private const int WindowSamples = 144_160;

    private readonly InferenceSession _session = new(Path.Combine(AppContext.BaseDirectory, "fixtures", "dnsmos-sig_bak_ovr.onnx"));

    public (double Sig, double Bak, double Ovrl) Score(float[] audio16k)
    {
        var audio = audio16k;
        while (audio.Length < WindowSamples)
        {
            audio = [.. audio, .. audio];
        }

        var hops = (int)Math.Floor((audio.Length / (double)SampleRate) - 9.01) + 1;
        double sig = 0, bak = 0, ovrl = 0;
        for (var hop = 0; hop < hops; hop++)
        {
            var segment = audio.AsSpan(hop * SampleRate, WindowSamples).ToArray();
            var tensor = new DenseTensor<float>(segment, [1, WindowSamples]);
            using var results = _session.Run([NamedOnnxValue.CreateFromTensor("input_1", tensor)]);
            var raw = results.First().AsEnumerable<float>().ToArray();
            sig += Poly(raw[0], -0.08397278, 1.22083953, 0.0052439);
            bak += Poly(raw[1], -0.13166888, 1.60915514, -0.39604546);
            ovrl += Poly(raw[2], -0.06766283, 1.11546468, 0.04602535);
        }

        return (sig / hops, bak / hops, ovrl / hops);
    }

    private static double Poly(double x, double a, double b, double c) => (a * x * x) + (b * x) + c;

    public void Dispose() => _session.Dispose();
}

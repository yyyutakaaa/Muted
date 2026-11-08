using System.IO;
using Muted.Audio.Windows.Dsp;
using Muted.Core.Audio;
using Muted.Core.Dsp;

namespace Muted.Audio.Windows.Tests;

/// <summary>
/// Research, not a regression test: where in the chain should the boost that "Tune to my
/// voice" recommends be applied? The same total gain is applied before the noise model
/// (the microphone boost) or after it (the voice level), with real microphone noise
/// mixed under clean speech. Runs only when MUTED_EXPERIMENT names an output file;
/// MUTED_NOISE_WAV names a recording of the room.
/// </summary>
public sealed class GainStageExperiment
{
    private const int Frame = 480;

    private static float[] ReadWav(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        reader.BaseStream.Seek(12, SeekOrigin.Begin);
        while (reader.BaseStream.Position < reader.BaseStream.Length - 8)
        {
            var id = new string(reader.ReadChars(4));
            var size = reader.ReadInt32();
            if (id != "data")
            {
                reader.BaseStream.Seek(size, SeekOrigin.Current);
                continue;
            }

            var samples = new float[size / 2];
            for (var index = 0; index < samples.Length; index++)
            {
                samples[index] = reader.ReadInt16() / 32768f;
            }

            return samples;
        }

        throw new InvalidDataException(path);
    }

    private static float Db(double rms) => (float)(20 * Math.Log10(Math.Max(rms, 1e-9)));

    private static double Rms(IEnumerable<float> values)
    {
        double sum = 0;
        var count = 0;
        foreach (var value in values)
        {
            sum += value * value;
            count++;
        }

        return count == 0 ? 0 : Math.Sqrt(sum / count);
    }

    private delegate void Run(float[] input, float[] output);

    private static float[] Process(string model, float[] input)
    {
        var output = new float[input.Length];
        var frameIn = new float[Frame];
        var frameOut = new float[Frame];
        if (model == "rnnoise")
        {
            using var rnnoise = new RnnoiseProcessor();
            for (var start = 0; start + Frame <= input.Length; start += Frame)
            {
                rnnoise.Process(input.AsSpan(start, Frame), frameOut);
                frameOut.CopyTo(output, start);
            }

            return output;
        }

        var studio = model switch
        {
            "classic" => DenoiserFactory.Create(StudioModel.DeepFilterNet3),
            "balanced" => DenoiserFactory.Create(StudioModel.DpdfNet2),
            _ => DenoiserFactory.Create(StudioModel.DpdfNet8)
        };
        using (studio)
        {
            for (var start = 0; start + Frame <= input.Length; start += Frame)
            {
                input.AsSpan(start, Frame).CopyTo(frameIn);
                studio.Process(frameIn, frameOut);
                frameOut.CopyTo(output, start);
            }
        }

        return output;
    }

    private static int NominalLag(string model) => model == "rnnoise" ? 960 : IDenoiser.LatencySamples;

    [Fact]
    public void Run_()
    {
        var reportPath = Environment.GetEnvironmentVariable("MUTED_EXPERIMENT");
        var noisePath = Environment.GetEnvironmentVariable("MUTED_NOISE_WAV");
        if (string.IsNullOrWhiteSpace(reportPath) || string.IsNullOrWhiteSpace(noisePath))
        {
            return;
        }

        var room = ReadWav(noisePath);
        // Three sentences with a second of silence after each, like talking with pauses.
        var sentence = SpeechFixture.Load();
        var pause = new float[Frame * 120];
        var speechClean = sentence.Concat(pause).Concat(sentence).Concat(pause).Concat(sentence).Concat(pause).ToArray();
        var length = speechClean.Length / Frame * Frame;
        speechClean = speechClean[..length];

        // Where the speaker is actually talking, from the clean take.
        var active = new bool[length / Frame];
        for (var frame = 0; frame < active.Length; frame++)
        {
            active[frame] = Db(Rms(speechClean.Skip(frame * Frame).Take(Frame))) > -45f;
        }

        var activeRms = Rms(Enumerable.Range(0, active.Length).Where(f => active[f])
            .SelectMany(f => speechClean.Skip(f * Frame).Take(Frame)));

        var pauseFrames = Enumerable.Range(12, Math.Max(0, active.Length - 20))
            .Count(f => Enumerable.Range(f - 2, 6).All(around => around >= 0 && around < active.Length && !active[around]));
        var lines = new List<string>
        {
            $"# {active.Count(x => x)} talking frames, {pauseFrames} pause frames of {active.Length}",
            "speech dBFS\tmodel\tpre-gain\tpost-gain\tnoise in pauses dBFS (final)\tspeech SI-SDR dB"
        };

        foreach (var speechDb in new[] { -32f })
        {
            var speechScale = (float)(Math.Pow(10, speechDb / 20.0) / activeRms);
            foreach (var model in new[] { "rnnoise", "classic", "balanced", "maximum" })
            {
                foreach (var pre in new[] { 0f, 5f, 10f })
                {
                    const float total = 10f;
                    double noiseSum = 0, sdrSum = 0;
                    var runs = 0;
                    foreach (var offset in new[] { 0, 400_000 })
                    {
                        var input = new float[length];
                        for (var index = 0; index < length; index++)
                        {
                            var noise = room[(offset + index) % room.Length];
                            input[index] = Math.Clamp(((speechClean[index] * speechScale) + noise) * MathF.Pow(10f, pre / 20f), -1f, 1f);
                        }

                        var output = Process(model, input);
                        var post = MathF.Pow(10f, (total - pre) / 20f);
                        for (var index = 0; index < length; index++)
                        {
                            output[index] *= post;
                        }

                        // Fine alignment around the model's own delay.
                        var nominal = NominalLag(model);
                        var bestLag = nominal;
                        var bestScore = double.MinValue;
                        for (var lag = nominal - 24; lag <= nominal + 24; lag++)
                        {
                            double score = 0;
                            for (var index = Frame * 10; index + lag < length; index++)
                            {
                                score += speechClean[index] * output[index + lag];
                            }

                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestLag = lag;
                            }
                        }

                        // Speech quality: scale-invariant SDR over the talking frames.
                        double cross = 0, energyRef = 0;
                        var samplesIn = new List<int>();
                        for (var frame = 12; frame < active.Length; frame++)
                        {
                            if (!active[frame])
                            {
                                continue;
                            }

                            for (var index = frame * Frame; index < (frame + 1) * Frame && index + bestLag < length; index++)
                            {
                                samplesIn.Add(index);
                                cross += output[index + bestLag] * speechClean[index];
                                energyRef += speechClean[index] * speechClean[index];
                            }
                        }

                        var alpha = cross / Math.Max(energyRef, 1e-12);
                        double targetEnergy = 0, errorEnergy = 0;
                        foreach (var index in samplesIn)
                        {
                            var target = alpha * speechClean[index];
                            var error = output[index + bestLag] - target;
                            targetEnergy += target * target;
                            errorEnergy += error * error;
                        }

                        sdrSum += 10 * Math.Log10(Math.Max(targetEnergy, 1e-12) / Math.Max(errorEnergy, 1e-12));

                        // Room noise left in the pauses of the speech, at the final level.
                        var pauseSamples = new List<float>();
                        for (var frame = 12; frame < active.Length - 8; frame++)
                        {
                            var quiet = true;
                            for (var around = frame - 2; around <= frame + 3 && quiet; around++)
                            {
                                quiet = !active[around];
                            }

                            if (quiet)
                            {
                                var start = (frame * Frame) + bestLag;
                                if (start + Frame <= length)
                                {
                                    pauseSamples.AddRange(output.Skip(start).Take(Frame));
                                }
                            }
                        }

                        noiseSum += pauseSamples.Count == 0 ? double.NaN : Math.Pow(Rms(pauseSamples), 2);
                        runs++;
                    }

                    lines.Add($"{speechDb:0}\t{model}\t+{pre:0}\t+{total - pre:0}\t{Db(Math.Sqrt(noiseSum / runs)):0.0}\t{sdrSum / runs:0.0}");
                }
            }
        }

        File.WriteAllLines(reportPath, lines);
    }
}

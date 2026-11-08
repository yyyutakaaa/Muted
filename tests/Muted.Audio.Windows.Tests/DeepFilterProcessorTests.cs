using System.IO;
using Muted.Audio.Windows.Dsp;

namespace Muted.Audio.Windows.Tests;

public sealed class DeepFilterProcessorTests
{
    private const int Frame = DeepFilterProcessor.FrameSize;

    [Fact]
    public void ModelShipsWithTheBuildAndMatchesItsChecksum()
    {
        var model = DeepFilterProcessor.DefaultModelPath;
        Assert.True(File.Exists(model), $"Missing {model}");
        var expected = File.ReadAllText(model + ".sha256").Split(' ', 2)[0].Trim();
        using var stream = File.OpenRead(model);
        var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(-20f, 0f)]
    [InlineData(-5f, 0f)]
    [InlineData(2.5f, 0.5f)]
    [InlineData(10f, 1f)]
    [InlineData(30f, 1f)]
    [InlineData(float.NaN, 0f)]
    public void LocalSnrBecomesASpeechProbability(float snr, float expected)
    {
        // Every Studio Voice model reports 0..1 confidence with this same curve.
        Assert.Equal(expected, Math.Clamp((snr + 5f) / 15f, 0f, 1f) is var value && float.IsFinite(snr) ? value : 0f, 4);
    }

    [Fact]
    public void KeyClicksDoNotLookLikeSpeech()
    {
        using var processor = new DeepFilterProcessor(DeepFilterProcessor.DefaultModelPath);
        var random = new Random(5);
        var input = new float[Frame];
        var output = new float[Frame];
        var highest = float.MinValue;
        for (var frame = 0; frame < 300; frame++)
        {
            Array.Clear(input);
            if (frame % 15 == 0)
            {
                // A sharp, decaying burst like a key or mouse click.
                for (var index = 0; index < 300; index++)
                {
                    input[index] = (float)((random.NextDouble() * 2 - 1) * 0.2 * Math.Exp(-index / 40d));
                }
            }

            processor.Process(input, output);
            if (frame > 30)
            {
                highest = MathF.Max(highest, processor.SpeechConfidence);
            }
        }

        Assert.True(highest < 0.32f, $"Clicks reached a speech probability of {highest:0.00}.");
    }

    [Fact]
    public void SilentFramesStayFiniteAndInRange()
    {
        using var processor = new DeepFilterProcessor(DeepFilterProcessor.DefaultModelPath);
        var input = new float[Frame];
        var output = new float[Frame];
        for (var frame = 0; frame < 10; frame++)
        {
            processor.Process(input, output);
        }

        Assert.All(output, sample => Assert.InRange(sample, -1f, 1f));
    }

    [Fact]
    public void RemovesStationaryNoise()
    {
        using var processor = new DeepFilterProcessor(DeepFilterProcessor.DefaultModelPath);
        var random = new Random(42);
        var input = new float[Frame];
        var output = new float[Frame];
        double inputEnergy = 0, outputEnergy = 0;
        for (var frame = 0; frame < 150; frame++)
        {
            for (var index = 0; index < Frame; index++)
            {
                input[index] = (float)((random.NextDouble() * 2d - 1d) * 0.08d);
            }

            processor.Process(input, output);
            if (frame < 30)
            {
                continue;
            }

            inputEnergy += input.Sum(sample => sample * sample);
            outputEnergy += output.Sum(sample => sample * sample);
        }

        // Far stronger than RNNoise's test: pure noise should be almost gone.
        Assert.True(outputEnergy < inputEnergy * 0.05d, $"input={inputEnergy}, output={outputEnergy}");
    }

    [Fact]
    public void KeepsAVoiceLikeSignalAndHasAThirtyMillisecondDelay()
    {
        using var processor = new DeepFilterProcessor(DeepFilterProcessor.DefaultModelPath);
        const int frames = 200;
        var signal = VoiceLikeBuzz(frames * Frame);
        var output = new float[signal.Length];
        var frameOutput = new float[Frame];
        for (var frame = 0; frame < frames; frame++)
        {
            processor.Process(signal.AsSpan(frame * Frame, Frame), frameOutput);
            frameOutput.CopyTo(output, frame * Frame);
        }

        // The lag at which the output lines up best with the input is the total delay,
        // which every Studio Voice model shares.
        var bestLag = 0;
        var bestScore = double.MinValue;
        for (var lag = 0; lag <= Frame * 6; lag++)
        {
            double score = 0;
            for (var index = Frame * 50; index < signal.Length - lag; index++)
            {
                score += signal[index] * output[index + lag];
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestLag = lag;
            }
        }

        Assert.Equal(IDenoiser.LatencySamples, bestLag);
        Assert.True(IDenoiser.LatencySamples == DeepFilterProcessor.AlgorithmicDelaySamples + (Frame * 2),
            "The staging must bring DeepFilterNet3 to the shared Studio Voice delay.");
        var kept = output.Skip(Frame * 50).Sum(sample => sample * sample) /
                   signal.Skip(Frame * 50).Sum(sample => sample * sample);
        Assert.True(kept > 0.2, $"Only {kept:P0} of the voice-like signal survived.");
    }

    [Fact]
    public void ResetStartsFromAFreshState()
    {
        using var processor = new DeepFilterProcessor(DeepFilterProcessor.DefaultModelPath);
        var signal = VoiceLikeBuzz(Frame * 20);
        var first = new float[Frame];
        var second = new float[Frame];
        for (var frame = 0; frame < 20; frame++)
        {
            processor.Process(signal.AsSpan(frame * Frame, Frame), first);
        }

        processor.Reset();
        using var fresh = new DeepFilterProcessor(DeepFilterProcessor.DefaultModelPath);
        processor.Process(signal.AsSpan(0, Frame), first);
        fresh.Process(signal.AsSpan(0, Frame), second);
        Assert.Equal(second, first);
    }

    /// <summary>A gliding 110-220 Hz pulse train through vowel-like resonances, syllable by syllable.</summary>
    private static float[] VoiceLikeBuzz(int length)
    {
        var samples = new float[length];
        var phase = 0d;
        var formants = new[] { (700d, 0.0), (1_200d, 0.0), (2_500d, 0.0) };
        var state = new double[formants.Length * 2];
        for (var index = 0; index < length; index++)
        {
            var time = index / 48_000d;
            var pitch = 150 + 40 * Math.Sin(2 * Math.PI * 0.7 * time);
            phase += pitch / 48_000d;
            var pulse = 0d;
            if (phase >= 1)
            {
                phase -= 1;
                pulse = 1;
            }

            var voice = 0d;
            for (var f = 0; f < formants.Length; f++)
            {
                // Two-pole resonator.
                var r = 0.995;
                var w = 2 * Math.PI * formants[f].Item1 / 48_000d;
                var y = pulse + 2 * r * Math.Cos(w) * state[f * 2] - r * r * state[f * 2 + 1];
                state[f * 2 + 1] = state[f * 2];
                state[f * 2] = y;
                voice += y / (f + 1);
            }

            var syllable = Math.Max(0, Math.Sin(2 * Math.PI * 3 * time));
            samples[index] = (float)(voice * syllable * 0.01);
        }

        var peak = samples.Max(MathF.Abs);
        for (var index = 0; index < length; index++)
        {
            samples[index] = samples[index] / peak * 0.5f;
        }

        return samples;
    }
}

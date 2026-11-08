using System.IO;
using Muted.Audio.Windows.Dsp;

namespace Muted.Audio.Windows.Tests;

public sealed class DpdfNetProcessorTests
{
    private const int Frame = 480;

    public static TheoryData<string> Models => new() { "dpdfnet2_48khz_hr", "dpdfnet8_48khz_hr" };

    private static string ModelPath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "models", name + ".onnx");

    [Theory]
    [MemberData(nameof(Models))]
    public void ModelShipsWithTheBuildAndMatchesItsChecksum(string name)
    {
        var model = ModelPath(name);
        Assert.True(File.Exists(model), $"Missing {model}");
        var expected = File.ReadAllText(model + ".sha256").Trim().Split(' ', 2)[0];
        using var stream = File.OpenRead(model);
        var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(Models))]
    public void RemovesStationaryNoise(string name)
    {
        using var processor = new DpdfNetProcessor(ModelPath(name), name);
        var random = new Random(42);
        var input = new float[Frame];
        var output = new float[Frame];
        double inputEnergy = 0, outputEnergy = 0;
        for (var frame = 0; frame < 200; frame++)
        {
            for (var index = 0; index < Frame; index++)
            {
                input[index] = (float)(((random.NextDouble() * 2d) - 1d) * 0.08d);
            }

            processor.Process(input, output);
            if (frame < 30)
            {
                continue;
            }

            inputEnergy += input.Sum(sample => sample * sample);
            outputEnergy += output.Sum(sample => sample * sample);
        }

        Assert.True(outputEnergy < inputEnergy * 0.15d, $"{name}: input={inputEnergy}, output={outputEnergy}");
    }

    [Theory]
    [MemberData(nameof(Models))]
    public void KeepsRealSpeechAndHasTheSharedDelay(string name)
    {
        using var processor = new DpdfNetProcessor(ModelPath(name), name);
        var signal = SpeechFixture.Load();
        var output = new float[signal.Length];
        var frameOutput = new float[Frame];
        for (var frame = 0; frame < signal.Length / Frame; frame++)
        {
            processor.Process(signal.AsSpan(frame * Frame, Frame), frameOutput);
            frameOutput.CopyTo(output, frame * Frame);
        }

        // Real speech survives: the cleaned signal still lines up with the original.
        var start = Frame * 40;
        var lag = IDenoiser.LatencySamples;
        var kept = output.Skip(start + lag).Sum(sample => sample * sample) /
                   signal.Skip(start).Take(signal.Length - (start + lag)).Sum(sample => sample * sample);
        Assert.True(kept > 0.45f, $"{name}: only {kept:P0} of the speech energy survived.");
    }

    [Theory]
    [MemberData(nameof(Models))]
    public void DelayMatchesEveryOtherStudioVoiceModel(string name)
    {
        using var processor = new DpdfNetProcessor(ModelPath(name), name);
        var signal = SpeechFixture.Load();
        var output = new float[signal.Length];
        var frameOutput = new float[Frame];
        for (var frame = 0; frame < signal.Length / Frame; frame++)
        {
            processor.Process(signal.AsSpan(frame * Frame, Frame), frameOutput);
            frameOutput.CopyTo(output, frame * Frame);
        }

        var bestLag = 0;
        var bestScore = double.MinValue;
        for (var lag = 0; lag <= Frame * 8; lag++)
        {
            double score = 0;
            for (var index = Frame * 40; index + lag < output.Length; index++)
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
    }

    [Theory]
    [MemberData(nameof(Models))]
    public void SpeechConfidenceRisesForVoiceAndStaysLowForNoise(string name)
    {
        using var processor = new DpdfNetProcessor(ModelPath(name), name);
        var output = new float[Frame];
        var signal = SpeechFixture.Load();
        var confidence = 0f;
        var frames = signal.Length / Frame;
        for (var frame = 0; frame < frames; frame++)
        {
            processor.Process(signal.AsSpan(frame * Frame, Frame), output);
            if (frame > 30)
            {
                confidence += processor.SpeechConfidence;
            }
        }

        var speech = confidence / (frames - 31);

        processor.Reset();
        var random = new Random(11);
        var noise = new float[Frame];
        confidence = 0f;
        for (var frame = 0; frame < 140; frame++)
        {
            for (var index = 0; index < Frame; index++)
            {
                noise[index] = (float)(((random.NextDouble() * 2d) - 1d) * 0.05d);
            }

            processor.Process(noise, output);
            if (frame > 30)
            {
                confidence += processor.SpeechConfidence;
            }
        }

        var room = confidence / 109f;
        Assert.True(speech > room + 0.3f, $"{name}: speech {speech:0.00}, room {room:0.00}");
    }

    [Fact]
    public void ResetStartsFromTheTrainedNormalisationState()
    {
        using var processor = new DpdfNetProcessor(ModelPath("dpdfnet2_48khz_hr"), "dpdfnet2");
        var signal = SpeechFixture.Load();
        var output = new float[Frame];
        for (var frame = 0; frame < 60; frame++)
        {
            processor.Process(signal.AsSpan(frame * Frame, Frame), output);
        }

        processor.Reset();
        using var fresh = new DpdfNetProcessor(ModelPath("dpdfnet2_48khz_hr"), "dpdfnet2");
        var first = new float[Frame];
        var second = new float[Frame];
        for (var frame = 0; frame < 40; frame++)
        {
            processor.Process(signal.AsSpan(frame * Frame, Frame), first);
            fresh.Process(signal.AsSpan(frame * Frame, Frame), second);
        }

        Assert.Equal(second, first);
    }
}

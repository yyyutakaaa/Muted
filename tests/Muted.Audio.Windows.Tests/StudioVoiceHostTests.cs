using Muted.Audio.Windows.Dsp;
using Muted.Core.Audio;
using Muted.Core.Dsp;

namespace Muted.Audio.Windows.Tests;

public sealed class StudioVoiceHostTests
{
    private const int Frame = 480;

    /// <summary>Feeds frames, like the audio thread, until the host settles on a model.</summary>
    private static void RunUntilSettled(StudioVoiceHost host, StudioModel model, float[] signal, ref int position, float[]? record = null)
    {
        var output = new float[Frame];
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (host.ActiveModel != model || host.IsSwitching)
        {
            Assert.True(DateTime.UtcNow < deadline, $"Never settled on {model}; active {host.ActiveModel}, error {host.Error}.");
            Step(host, signal, ref position, output, record);
            Thread.Sleep(1);
        }
    }

    private static void Step(StudioVoiceHost host, float[] signal, ref int position, float[] output, float[]? record)
    {
        if (position + Frame > signal.Length)
        {
            position = 0;
        }

        if (!host.Process(signal.AsSpan(position, Frame), output))
        {
            Array.Clear(output);
        }

        if (record is not null)
        {
            output.CopyTo(record, position);
        }

        position += Frame;
    }

    [Fact]
    public void SwitchingLoadsEachModelOnceAndSettles()
    {
        using var host = new StudioVoiceHost();
        var signal = SpeechFixture.Load();
        var position = 0;

        host.Request(StudioModel.DeepFilterNet3);
        RunUntilSettled(host, StudioModel.DeepFilterNet3, signal, ref position);
        Assert.Equal(1, host.Loads);

        host.Request(StudioModel.DpdfNet2);
        RunUntilSettled(host, StudioModel.DpdfNet2, signal, ref position);
        Assert.Equal(2, host.Loads);

        // Keep running: a settled host must not load anything more.
        var output = new float[Frame];
        for (var frame = 0; frame < 200; frame++)
        {
            Step(host, signal, ref position, output, null);
            host.Request(StudioModel.DpdfNet2);
        }

        Thread.Sleep(500);
        for (var frame = 0; frame < 50; frame++)
        {
            Step(host, signal, ref position, output, null);
        }

        Assert.Equal(2, host.Loads);
        Assert.Equal(StudioModel.DpdfNet2, host.ActiveModel);
        Assert.False(host.IsSwitching);
    }

    [Fact]
    public void RapidClicksEndOnTheLastChoiceWithoutPilingUpLoads()
    {
        using var host = new StudioVoiceHost();
        var signal = SpeechFixture.Load();
        var position = 0;
        host.Request(StudioModel.DeepFilterNet3);
        RunUntilSettled(host, StudioModel.DeepFilterNet3, signal, ref position);

        host.Request(StudioModel.DpdfNet2);
        host.Request(StudioModel.DpdfNet8);
        host.Request(StudioModel.DpdfNet2);
        host.Request(StudioModel.DpdfNet8);
        RunUntilSettled(host, StudioModel.DpdfNet8, signal, ref position);

        // At most one load for the click that was in flight and one for the last click.
        Assert.InRange(host.Loads, 2, 3);
    }

    [Fact]
    public void TheVoiceKeepsGoingThroughASwitch()
    {
        var signal = SpeechFixture.Load();

        // Reference: Classic alone over the same input.
        using var reference = new DeepFilterProcessor(DenoiserFactory.ModelPath(StudioModel.DeepFilterNet3));
        var expected = new float[signal.Length];
        var frameOut = new float[Frame];
        for (var start = 0; start + Frame <= signal.Length; start += Frame)
        {
            reference.Process(signal.AsSpan(start, Frame), frameOut);
            frameOut.CopyTo(expected, start);
        }

        using var host = new StudioVoiceHost();
        host.Request(StudioModel.DeepFilterNet3);
        var position = 0;
        RunUntilSettled(host, StudioModel.DeepFilterNet3, new float[Frame * 4], ref position);

        // Start the speech from the top with a settled model, then switch halfway.
        host.Reset();
        var actual = new float[signal.Length];
        position = 0;
        var output = new float[Frame];
        var switchFrame = 60;
        for (var frame = 0; frame < signal.Length / Frame; frame++)
        {
            if (frame == switchFrame)
            {
                host.Request(StudioModel.DpdfNet2);
                Thread.Sleep(1_500);
            }

            Step(host, signal, ref position, output, actual);
        }

        // Every 50 ms window from the switch to the end still carries the voice,
        // measured against the same windows of the untouched reference.
        for (var start = switchFrame * Frame; start + (Frame * 5) <= signal.Length; start += Frame * 5)
        {
            var wanted = AudioMath.Rms(expected.AsSpan(start, Frame * 5));
            if (wanted < 0.01f)
            {
                continue;
            }

            var got = AudioMath.Rms(actual.AsSpan(start, Frame * 5));
            Assert.True(got > wanted * 0.4f, $"Dropout at {start / 48.0:0} ms: {got:0.0000} against {wanted:0.0000}.");
        }
    }

    [Fact]
    public void AModelThatCannotLoadIsNotRetriedInALoop()
    {
        var attempts = 0;
        using var host = new StudioVoiceHost(_ =>
        {
            Interlocked.Increment(ref attempts);
            throw new InvalidOperationException("broken model");
        });

        host.Request(StudioModel.DpdfNet8);
        var output = new float[Frame];
        for (var frame = 0; frame < 100; frame++)
        {
            Assert.False(host.Process(new float[Frame], output));
            host.Request(StudioModel.DpdfNet8);
            Thread.Sleep(2);
        }

        Assert.Equal(1, attempts);
        Assert.Equal("broken model", host.Error);
        Assert.Null(host.ActiveModel);
    }
}

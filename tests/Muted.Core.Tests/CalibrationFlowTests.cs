using Muted.Core.Speech;

namespace Muted.Core.Tests;

public sealed class CalibrationFlowTests
{
    private const float Speech = 0.9f;
    private const float Quiet = 0.05f;

    /// <summary>Plays a script of speech probabilities into the flow, 200 ms of UI ticks at a time.</summary>
    private sealed class Script(CalibrationFlow flow)
    {
        private readonly List<float> _frames = [];

        public int Frames => _frames.Count;

        public void Add(float probability, double seconds)
        {
            var frames = (int)Math.Round(seconds * CalibrationFlow.FramesPerSecond);
            for (var index = 0; index < frames; index++)
            {
                _frames.Add(probability);
                if (_frames.Count % 20 == 0)
                {
                    Tick();
                }
            }

            Tick();
        }

        public void Tick()
        {
            var start = Math.Min(flow.PhaseStartFrame, _frames.Count);
            flow.Update(_frames.Count, _frames.Skip(start).ToArray());
        }
    }

    [Fact]
    public void MovesOnAsSoonAsTheEndOfTheSentenceIsHeard()
    {
        var flow = new CalibrationFlow(recognizerAvailable: true);
        var script = new Script(flow);
        script.Add(Quiet, 4);
        Assert.Equal(CalibrationPhase.Reading, flow.Phase);
        Assert.Equal(400, flow.ReadingStartFrame);

        script.Add(Speech, 6);
        flow.Hear(CalibrationPhase.Reading, "The quick brown fox jumps over the lazy dog. She sells seashells");
        script.Add(Speech, 2);
        Assert.Equal(CalibrationPhase.Reading, flow.Phase);
        Assert.InRange(flow.Progress, 0.3, 0.8);

        flow.Hear(CalibrationPhase.Reading, CalibrationFlow.ReadingText);
        script.Add(Quiet, 0.4);
        Assert.Equal(CalibrationPhase.Loud, flow.Phase);

        // Twelve seconds of reading, not the old fixed fifteen.
        Assert.InRange(flow.LoudStartFrame - flow.ReadingStartFrame, 800, 900);

        script.Add(Speech, 1);
        flow.Hear(CalibrationPhase.Loud, "No way! Behind you!");
        script.Add(Quiet, 0.4);
        Assert.Equal(CalibrationPhase.Done, flow.Phase);
        Assert.Equal(1.0, flow.Progress);
    }

    [Fact]
    public void WaitsForTheEndOfTheSentenceThroughShortPauses()
    {
        var flow = new CalibrationFlow(recognizerAvailable: true);
        var script = new Script(flow);
        script.Add(Quiet, 4);
        script.Add(Speech, 3);
        flow.Hear(CalibrationPhase.Reading, "The quick brown fox jumps over the lazy dog.");
        script.Add(Quiet, 1.5);
        Assert.Equal(CalibrationPhase.Reading, flow.Phase);
    }

    [Fact]
    public void IgnoresATranscriptOfAStepThatAlreadyEnded()
    {
        var flow = new CalibrationFlow(recognizerAvailable: true);
        var script = new Script(flow);
        script.Add(Quiet, 4);
        script.Add(Speech, 8);
        flow.Hear(CalibrationPhase.Reading, CalibrationFlow.ReadingText);
        script.Add(Quiet, 0.5);
        Assert.Equal(CalibrationPhase.Loud, flow.Phase);

        flow.Hear(CalibrationPhase.Reading, CalibrationFlow.ReadingText);
        Assert.Equal(string.Empty, flow.Heard);
    }

    [Fact]
    public void WithoutRecognitionAPauseAfterEnoughSpeechEndsTheReading()
    {
        var flow = new CalibrationFlow(recognizerAvailable: false);
        var script = new Script(flow);
        script.Add(Quiet, 4);
        script.Add(Speech, 2);
        script.Add(Quiet, 1.5);
        Assert.Equal(CalibrationPhase.Reading, flow.Phase);

        script.Add(Speech, 3);
        script.Add(Quiet, 1.3);
        Assert.Equal(CalibrationPhase.Loud, flow.Phase);

        script.Add(Speech, 0.8);
        script.Add(Quiet, 1.1);
        Assert.Equal(CalibrationPhase.Done, flow.Phase);
    }

    [Fact]
    public void AlwaysFinishesWithinTheTimeLimits()
    {
        var flow = new CalibrationFlow(recognizerAvailable: true);
        var script = new Script(flow);
        script.Add(Quiet, 4 + 30 + 10 + 0.5);
        Assert.Equal(CalibrationPhase.Done, flow.Phase);
        Assert.True(script.Frames <= CalibrationFlow.MaxSeconds * CalibrationFlow.FramesPerSecond);
    }

    [Fact]
    public void AMisheardEndingStillFinishesAfterALongPause()
    {
        var flow = new CalibrationFlow(recognizerAvailable: true);
        var script = new Script(flow);
        script.Add(Quiet, 4);
        script.Add(Speech, 10);
        flow.Hear(CalibrationPhase.Reading,
            "The quick brown fox jumps over the lazy dog. She sells seashells by the seashore, and on Sundays we play");
        script.Add(Quiet, 1);
        Assert.Equal(CalibrationPhase.Reading, flow.Phase);
        script.Add(Quiet, 1.6);
        Assert.Equal(CalibrationPhase.Loud, flow.Phase);
    }
}

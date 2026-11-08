namespace Muted.Core.Speech;

public enum CalibrationPhase
{
    Quiet,
    Reading,
    Loud,
    Done
}

/// <summary>
/// The steps of "Tune to my voice": a few seconds of quiet, the sentence read aloud,
/// then one loud phrase. It moves on when you are done rather than after a fixed time.
/// Speech recognition says when the end of the text was heard; the speech probability
/// says when you stopped talking. Without speech recognition, a pause after enough
/// speech ends a step. Every step also has a time limit, so it always finishes.
/// All times are in 10 ms frames of recorded audio, not wall-clock time.
/// </summary>
public sealed class CalibrationFlow
{
    public const string ReadingText =
        "The quick brown fox jumps over the lazy dog. She sells sea shells by the sea shore, " +
        "and on Sundays we play games together until late at night.";

    public const string LoudText = "No way, behind you!";

    public const int FramesPerSecond = 100;
    public const int QuietFrames = 4 * FramesPerSecond;
    public const int MaxReadingFrames = 30 * FramesPerSecond;
    public const int MaxLoudFrames = 10 * FramesPerSecond;

    /// <summary>The most audio a calibration can record, in seconds.</summary>
    public const int MaxSeconds = ((QuietFrames + MaxReadingFrames + MaxLoudFrames) / FramesPerSecond) + 2;

    private const float SpeechProbability = 0.5f;

    // The last word fades out after it is recognised; this much quiet keeps its tail in
    // the reading step, where it belongs.
    private const int TailFrames = 30;

    // Without recognition: four seconds of speech, then a pause of 1.2 s, is a read sentence.
    private const int FallbackSpeechFrames = 4 * FramesPerSecond;
    private const int FallbackPauseFrames = 120;

    // Recognition heard most of the text but not the end: a long pause still ends it.
    private const int StalledPauseFrames = 250;
    private const double StalledFraction = 0.6;

    // The loud phrase is short: half a second of speech and then a second of quiet.
    private const int LoudSpeechFrames = 50;
    private const int LoudPauseFrames = 100;

    private readonly ReadingMatcher _reading = new(ReadingText);
    private readonly ReadingMatcher _loud = new(LoudText);
    private int _speechFrames;

    public CalibrationFlow(bool recognizerAvailable)
    {
        RecognizerAvailable = recognizerAvailable;
    }

    public CalibrationPhase Phase { get; private set; } = CalibrationPhase.Quiet;

    /// <summary>True while speech recognition helps; false falls back to pauses alone.</summary>
    public bool RecognizerAvailable { get; set; }

    public int ReadingStartFrame { get; private set; }

    public int LoudStartFrame { get; private set; }

    public int EndFrame { get; private set; }

    public ReadingProgress ReadingProgress { get; private set; }

    public ReadingProgress LoudProgress { get; private set; }

    /// <summary>The latest transcript of the current step, for showing what was heard.</summary>
    public string Heard { get; private set; } = string.Empty;

    /// <summary>Where the current step started, in frames.</summary>
    public int PhaseStartFrame => Phase switch
    {
        CalibrationPhase.Reading => ReadingStartFrame,
        CalibrationPhase.Loud => LoudStartFrame,
        _ => 0
    };

    /// <summary>0..1 for the progress bar.</summary>
    public double Progress { get; private set; }

    /// <summary>Takes a transcript of <paramref name="phase"/>; one for an earlier step is ignored.</summary>
    public void Hear(CalibrationPhase phase, string transcript)
    {
        if (phase != Phase)
        {
            return;
        }

        if (phase == CalibrationPhase.Reading)
        {
            ReadingProgress = _reading.Match(transcript);
            Heard = transcript;
        }
        else if (phase == CalibrationPhase.Loud)
        {
            LoudProgress = _loud.Match(transcript);
            Heard = transcript;
        }
    }

    /// <param name="recordedFrames">How many frames the recorder holds.</param>
    /// <param name="phaseProbabilities">The speech probability of every frame since the current step started.</param>
    public void Update(int recordedFrames, ReadOnlySpan<float> phaseProbabilities)
    {
        var phaseFrames = recordedFrames - PhaseStartFrame;
        _speechFrames = 0;
        var pause = phaseProbabilities.Length;
        for (var index = 0; index < phaseProbabilities.Length; index++)
        {
            if (phaseProbabilities[index] >= SpeechProbability)
            {
                _speechFrames++;
                pause = phaseProbabilities.Length - 1 - index;
            }
        }

        switch (Phase)
        {
            case CalibrationPhase.Quiet:
                Progress = 0.15 * Math.Min(1.0, recordedFrames / (double)QuietFrames);
                if (recordedFrames >= QuietFrames)
                {
                    Enter(CalibrationPhase.Reading, recordedFrames);
                }

                break;

            case CalibrationPhase.Reading:
                var fraction = RecognizerAvailable
                    ? ReadingProgress.Fraction
                    : Math.Min(0.95, _speechFrames / (double)(FallbackSpeechFrames * 2));
                Progress = 0.15 + (0.65 * fraction);
                var heardEnd = ReadingProgress.Finished && pause >= TailFrames;
                var pausedAfterSpeech = !RecognizerAvailable && _speechFrames >= FallbackSpeechFrames && pause >= FallbackPauseFrames;
                var stalled = RecognizerAvailable && ReadingProgress.Fraction >= StalledFraction && pause >= StalledPauseFrames;
                if (heardEnd || pausedAfterSpeech || stalled || phaseFrames >= MaxReadingFrames)
                {
                    Enter(CalibrationPhase.Loud, recordedFrames);
                }

                break;

            case CalibrationPhase.Loud:
                Progress = 0.8 + (0.2 * Math.Min(1.0, phaseFrames / 300.0));
                var saidIt = LoudProgress.Finished && pause >= TailFrames;
                var shoutedAndStopped = _speechFrames >= LoudSpeechFrames && pause >= LoudPauseFrames;
                if (saidIt || shoutedAndStopped || phaseFrames >= MaxLoudFrames)
                {
                    EndFrame = recordedFrames;
                    Phase = CalibrationPhase.Done;
                    Progress = 1.0;
                }

                break;
        }
    }

    /// <summary>Seconds of speech heard in the current step so far.</summary>
    public double SpeechSeconds => _speechFrames / (double)FramesPerSecond;

    private void Enter(CalibrationPhase phase, int frame)
    {
        Phase = phase;
        Heard = string.Empty;
        _speechFrames = 0;
        if (phase == CalibrationPhase.Reading)
        {
            ReadingStartFrame = frame;
        }
        else
        {
            LoudStartFrame = frame;
        }
    }
}

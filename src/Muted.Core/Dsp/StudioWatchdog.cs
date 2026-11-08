namespace Muted.Core.Dsp;

/// <summary>
/// Notices when Studio Voice stops cleaning. A healthy model removes well over 15 dB of
/// room noise while you are quiet; a model whose internal state went wrong passes the
/// noise through and also tells the gate it hears a voice. Resetting the model state
/// fixes it, so this asks for a reset after a few seconds of quiet that was not cleaned.
/// </summary>
public sealed class StudioWatchdog
{
    /// <summary>Two seconds of uncleaned quiet at 100 frames per second.</summary>
    public const int FramesBeforeReset = 200;

    /// <summary>After a reset that did not help, wait ten seconds before the next.</summary>
    public const int FramesBeforeRepeatReset = 1_000;

    private const float QuietProbability = 0.15f;

    /// <summary>About -66 dBFS: a near-silent room has nothing to clean, so it proves nothing.</summary>
    private const float MinimumInputRms = 5e-4f;

    /// <summary>Less than 6 dB removed counts as not cleaned.</summary>
    private const float UncleanedRatio = 0.5f;

    private int _uncleanedFrames;
    private bool _justReset;

    /// <summary>How many resets this watchdog asked for.</summary>
    public long Resets { get; private set; }

    /// <summary>Call once per frame the model processed. True means: reset the model now.</summary>
    /// <param name="inputRms">The frame that went into the model.</param>
    /// <param name="outputRms">What the model made of it.</param>
    /// <param name="voiceProbability">RNNoise's speech probability for the same frame.</param>
    public bool Observe(float inputRms, float outputRms, float voiceProbability)
    {
        if (!float.IsFinite(inputRms) || !float.IsFinite(outputRms) || !float.IsFinite(voiceProbability))
        {
            return false;
        }

        // Only quiet, non-silent frames say anything; speech is meant to pass.
        if (voiceProbability >= QuietProbability || inputRms < MinimumInputRms)
        {
            return false;
        }

        if (outputRms < inputRms * UncleanedRatio)
        {
            _uncleanedFrames = 0;
            _justReset = false;
            return false;
        }

        if (++_uncleanedFrames < (_justReset ? FramesBeforeRepeatReset : FramesBeforeReset))
        {
            return false;
        }

        _uncleanedFrames = 0;
        _justReset = true;
        Resets++;
        return true;
    }

    public void Reset()
    {
        _uncleanedFrames = 0;
        _justReset = false;
    }
}

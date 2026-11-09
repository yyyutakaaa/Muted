namespace Muted.Core.Dsp;

/// <summary>
/// Silences the microphone between words. It opens on an early speech signal and stays
/// open while a second, slower detector confirms the voice, so the start of a word is
/// never cut off. With Studio Voice the audio reaches the gate 50 ms after RNNoise has
/// already heard it; that head start is used to open before the word arrives. Closed, it
/// does not drop to dead silence but to a low floor, and it fades down rather than
/// snapping shut, so the other side never hears the line go dead mid-sentence.
/// </summary>
public sealed class VoiceGate
{
    /// <summary>How far a closed gate turns the sound down: 30 dB.</summary>
    public const float DefaultFloorGain = 0.0316f;

    /// <summary>
    /// With room tone on, a closed gate turns the sound down only 10 dB, so the faint room
    /// under Studio Voice stays audible between words instead of being gated away.
    /// </summary>
    public const float RoomToneFloorGain = 0.316f;

    private const int FrameDurationMilliseconds = 10;

    // Closing takes 50 ms so a word's tail fades out; opening happens within one frame.
    private const int CloseFrames = 5;

    // An early "speech" that the confirming detector never backs up within this time,
    // such as a key click, stops counting until it goes quiet again.
    private const int UnconfirmedLimitFrames = 8;

    // The early detector only has to be half as sure to open: it gets the gate open in
    // time for the first syllable, and the confirming detector decides whether it stays.
    private const float EarlyThresholdScale = 0.5f;

    private int _holdFramesRemaining;
    private int _unconfirmedFrames;
    private bool _earlyRejected;
    private float _gain;

    public float Gain => _gain;

    /// <summary>
    /// The single-detector gate: one signal opens it and keeps it open, with no early
    /// opening, because without a second detector nothing could overrule a false one.
    /// </summary>
    public void Process(
        Span<float> samples,
        float voiceProbability,
        float threshold,
        int holdMilliseconds,
        bool enabled) =>
        Process(samples, 0f, voiceProbability, threshold, holdMilliseconds, enabled, 0f);

    /// <param name="samples">The audio to gate, in place.</param>
    /// <param name="earlyProbability">A speech probability that runs ahead of the audio, which opens the gate; 0 when there is none.</param>
    /// <param name="confirmProbability">A speech probability aligned with the audio, which keeps it open.</param>
    /// <param name="threshold">How sure a detector must be, 0.05 to 0.99.</param>
    /// <param name="holdMilliseconds">How long the gate stays open after the voice stops.</param>
    /// <param name="enabled">False lets everything through.</param>
    /// <param name="floorGain">The gain of a closed gate: 0 is silence.</param>
    public void Process(
        Span<float> samples,
        float earlyProbability,
        float confirmProbability,
        float threshold,
        int holdMilliseconds,
        bool enabled,
        float floorGain)
    {
        if (!enabled)
        {
            _holdFramesRemaining = 0;
            _unconfirmedFrames = 0;
            _earlyRejected = false;
            _gain = 1f;
            return;
        }

        threshold = Math.Clamp(threshold, 0.05f, 0.99f);
        floorGain = Math.Clamp(floorGain, 0f, 1f);
        var holdFrames = Math.Max(0, (int)Math.Ceiling(holdMilliseconds / (double)FrameDurationMilliseconds));

        var confirmed = confirmProbability >= threshold;
        var early = earlyProbability >= threshold * EarlyThresholdScale;
        if (!early)
        {
            _earlyRejected = false;
        }

        if (confirmed)
        {
            _unconfirmedFrames = 0;
            _earlyRejected = false;
        }
        else if (early && !_earlyRejected && ++_unconfirmedFrames > UnconfirmedLimitFrames)
        {
            // The early detector heard something the confirming one never did.
            _earlyRejected = true;
            _holdFramesRemaining = 0;
        }

        float target;
        if (confirmed || (early && !_earlyRejected))
        {
            if (confirmed)
            {
                _holdFramesRemaining = holdFrames;
            }

            target = 1f;
        }
        else if (_holdFramesRemaining > 0)
        {
            _holdFramesRemaining--;
            target = 1f;
        }
        else
        {
            target = floorGain;
        }

        if (!early && !confirmed)
        {
            _unconfirmedFrames = 0;
        }

        // Open at once; close over a few frames.
        var start = _gain;
        var end = target >= start ? target : Math.Max(target, start - ((1f - floorGain) / CloseFrames));
        var step = samples.Length == 0 ? 0f : (end - start) / samples.Length;
        var gain = start;
        for (var index = 0; index < samples.Length; index++)
        {
            gain += step;
            samples[index] *= gain;
        }

        _gain = end;
    }

    public void Reset(bool open = false)
    {
        _holdFramesRemaining = 0;
        _unconfirmedFrames = 0;
        _earlyRejected = false;
        _gain = open ? 1f : 0f;
    }
}

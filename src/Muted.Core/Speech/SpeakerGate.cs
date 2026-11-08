namespace Muted.Core.Speech;

/// <summary>
/// "Only my voice": closes the microphone while the voice it hears is not yours. The
/// speaker check runs off the audio thread and hands over a similarity score; this
/// turns that score into a smooth gain on every 10 ms frame. It fails open: without a
/// fresh score (silence, the model still loading, too little speech yet) it lets the
/// sound through, so it can never cut you off just because it is unsure.
/// </summary>
public sealed class SpeakerGate
{
    /// <summary>How far a blocked voice is turned down: 30 dB.</summary>
    public const float BlockedGain = 0.0316f;

    // Closing takes 60 ms so a word in progress fades instead of clicking; opening takes
    // 15 ms so your first syllable after someone else talked is not swallowed.
    private const float CloseSeconds = 0.06f;
    private const float OpenSeconds = 0.015f;
    private const float Hysteresis = 0.10f;

    private readonly float _closeStep;
    private readonly float _openStep;
    private float _gain = 1f;
    private bool _blocking;

    public SpeakerGate(int sampleRate)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        _closeStep = (1f - BlockedGain) / (CloseSeconds * sampleRate);
        _openStep = (1f - BlockedGain) / (OpenSeconds * sampleRate);
    }

    /// <summary>True while another voice is being held back.</summary>
    public bool IsBlocking => _blocking;

    public float CurrentGain => _gain;

    /// <summary>
    /// Below this similarity a voice counts as someone else. Strictness 0 is lenient,
    /// 1 is strict; the middle suits most microphones.
    /// </summary>
    public static float BlockBelow(float strictness) => 0.25f + (0.40f * Math.Clamp(strictness, 0f, 1f));

    public void Reset()
    {
        _gain = 1f;
        _blocking = false;
    }

    /// <param name="frame">Audio to gate, in place.</param>
    /// <param name="similarity">The latest similarity with your voice, or NaN when there is none.</param>
    /// <param name="strictness">0..1, see <see cref="BlockBelow"/>.</param>
    /// <param name="enabled">False lets everything through and ramps back to full volume.</param>
    public void Process(Span<float> frame, float similarity, float strictness, bool enabled)
    {
        if (!enabled || float.IsNaN(similarity))
        {
            _blocking = false;
        }
        else
        {
            var threshold = BlockBelow(strictness);
            if (_blocking)
            {
                // Hysteresis: it takes clearly more of your voice to open again than it
                // took to close, so a borderline score does not flutter.
                _blocking = similarity < threshold + Hysteresis;
            }
            else
            {
                _blocking = similarity < threshold;
            }
        }

        var target = _blocking ? BlockedGain : 1f;
        for (var index = 0; index < frame.Length; index++)
        {
            if (_gain > target)
            {
                _gain = MathF.Max(target, _gain - _closeStep);
            }
            else if (_gain < target)
            {
                _gain = MathF.Min(target, _gain + _openStep);
            }

            frame[index] *= _gain;
        }
    }
}

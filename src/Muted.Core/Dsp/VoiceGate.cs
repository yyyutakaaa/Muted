namespace Muted.Core.Dsp;

public sealed class VoiceGate
{
    private const int FrameDurationMilliseconds = 10;
    private int _holdFramesRemaining;
    private float _gain;

    public float Gain => _gain;

    public void Process(
        Span<float> samples,
        float voiceProbability,
        float threshold,
        int holdMilliseconds,
        bool enabled)
    {
        if (!enabled)
        {
            _holdFramesRemaining = 0;
            _gain = 1f;
            return;
        }

        threshold = Math.Clamp(threshold, 0.05f, 0.99f);
        var holdFrames = Math.Max(0, (int)Math.Ceiling(holdMilliseconds / (double)FrameDurationMilliseconds));

        float target;
        if (voiceProbability >= threshold)
        {
            _holdFramesRemaining = holdFrames;
            target = 1f;
        }
        else if (_holdFramesRemaining > 0)
        {
            _holdFramesRemaining--;
            target = 1f;
        }
        else
        {
            target = 0f;
        }

        var start = _gain;
        var step = samples.Length == 0 ? 0f : (target - start) / samples.Length;
        var gain = start;
        for (var index = 0; index < samples.Length; index++)
        {
            gain += step;
            samples[index] *= gain;
        }

        _gain = target;
    }

    public void Reset(bool open = false)
    {
        _holdFramesRemaining = 0;
        _gain = open ? 1f : 0f;
    }
}

namespace Muted.Core.Dsp;

/// <summary>
/// Second-order Butterworth high-pass, used to strip desk rumble and handling
/// noise that RNNoise leaves alone because it sits below the speech band.
/// </summary>
public sealed class HighPassFilter
{
    public const float MinimumFrequency = 40f;
    public const float MaximumFrequency = 200f;

    private readonly int _sampleRate;
    private float _frequency;
    private float _b0;
    private float _b1;
    private float _b2;
    private float _a1;
    private float _a2;
    private float _x1;
    private float _x2;
    private float _y1;
    private float _y2;

    public HighPassFilter(int sampleRate, float frequency = 80f)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        _sampleRate = sampleRate;
        SetFrequency(frequency);
    }

    public float Frequency => _frequency;

    public void SetFrequency(float frequency)
    {
        frequency = Math.Clamp(frequency, MinimumFrequency, MaximumFrequency);
        if (Math.Abs(frequency - _frequency) < 0.01f)
        {
            return;
        }

        _frequency = frequency;

        // RBJ audio cookbook high-pass with a Butterworth Q.
        const double q = 0.70710678;
        var omega = 2.0 * Math.PI * frequency / _sampleRate;
        var cosOmega = Math.Cos(omega);
        var alpha = Math.Sin(omega) / (2.0 * q);
        var a0 = 1.0 + alpha;

        _b0 = (float)(((1.0 + cosOmega) / 2.0) / a0);
        _b1 = (float)((-(1.0 + cosOmega)) / a0);
        _b2 = _b0;
        _a1 = (float)((-2.0 * cosOmega) / a0);
        _a2 = (float)((1.0 - alpha) / a0);
    }

    public void Process(Span<float> samples)
    {
        var b0 = _b0;
        var b1 = _b1;
        var b2 = _b2;
        var a1 = _a1;
        var a2 = _a2;
        var x1 = _x1;
        var x2 = _x2;
        var y1 = _y1;
        var y2 = _y2;

        for (var index = 0; index < samples.Length; index++)
        {
            var x0 = samples[index];
            var y0 = (b0 * x0) + (b1 * x1) + (b2 * x2) - (a1 * y1) - (a2 * y2);
            x2 = x1;
            x1 = x0;
            y2 = y1;
            y1 = y0;
            samples[index] = y0;
        }

        // Denormals would otherwise keep the filter burning cycles once it goes quiet.
        _x1 = Flush(x1);
        _x2 = Flush(x2);
        _y1 = Flush(y1);
        _y2 = Flush(y2);
    }

    public void Reset()
    {
        _x1 = 0f;
        _x2 = 0f;
        _y1 = 0f;
        _y2 = 0f;
    }

    private static float Flush(float value) =>
        float.IsSubnormal(value) || !float.IsFinite(value) ? 0f : value;
}

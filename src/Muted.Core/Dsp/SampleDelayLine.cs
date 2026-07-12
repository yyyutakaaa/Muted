namespace Muted.Core.Dsp;

public sealed class SampleDelayLine
{
    private readonly float[] _samples;
    private int _position;

    public SampleDelayLine(int delaySamples)
    {
        if (delaySamples <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(delaySamples));
        }

        _samples = new float[delaySamples];
    }

    public void Process(ReadOnlySpan<float> input, Span<float> output)
    {
        if (output.Length < input.Length)
        {
            throw new ArgumentException("Output buffer is too small.", nameof(output));
        }

        for (var index = 0; index < input.Length; index++)
        {
            output[index] = _samples[_position];
            _samples[_position] = input[index];
            _position++;
            if (_position == _samples.Length)
            {
                _position = 0;
            }
        }
    }

    public void Reset()
    {
        Array.Clear(_samples);
        _position = 0;
    }
}

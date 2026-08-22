using System.Threading;

namespace Muted.Core.Dsp;

/// <summary>
/// A rolling history of frame peaks the UI can draw. The audio thread pushes one
/// value per frame and never blocks; readers copy a snapshot, oldest first.
/// </summary>
public sealed class WaveformScope
{
    private readonly float[] _values;
    private long _writeSequence;

    public WaveformScope(int capacity = 256)
    {
        if (capacity < 8)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        var size = 8;
        while (size < capacity)
        {
            size = checked(size * 2);
        }

        _values = new float[size];
    }

    public int Capacity => _values.Length;

    /// <summary>Called from the realtime thread once per processed frame.</summary>
    public void Push(float value)
    {
        if (!float.IsFinite(value))
        {
            value = 0f;
        }

        var sequence = Volatile.Read(ref _writeSequence);
        _values[(int)(sequence & (_values.Length - 1))] = Math.Clamp(value, 0f, 1f);
        Volatile.Write(ref _writeSequence, sequence + 1);
    }

    /// <summary>
    /// Copies the most recent values into <paramref name="destination"/>, oldest first.
    /// Slots that were never written read as zero.
    /// </summary>
    public void CopyTo(Span<float> destination)
    {
        var count = Math.Min(destination.Length, _values.Length);
        var sequence = Volatile.Read(ref _writeSequence);
        var start = sequence - count;

        destination[..(destination.Length - count)].Clear();
        var offset = destination.Length - count;
        for (var index = 0; index < count; index++)
        {
            var position = start + index;
            destination[offset + index] = position < 0
                ? 0f
                : _values[(int)(position & (_values.Length - 1))];
        }
    }

    public void Clear()
    {
        Array.Clear(_values);
        Volatile.Write(ref _writeSequence, 0);
    }
}

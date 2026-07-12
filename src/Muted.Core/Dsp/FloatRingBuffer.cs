using System.Threading;

namespace Muted.Core.Dsp;

/// <summary>
/// A fixed-size, allocation-free single-producer/single-consumer float buffer.
/// </summary>
public sealed class FloatRingBuffer
{
    private readonly float[] _buffer;
    private readonly int _mask;
    private long _readSequence;
    private long _writeSequence;
    private long _droppedSamples;

    public FloatRingBuffer(int minimumCapacity)
    {
        if (minimumCapacity < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumCapacity));
        }

        var capacity = 2;
        while (capacity < minimumCapacity)
        {
            capacity = checked(capacity * 2);
        }

        _buffer = new float[capacity];
        _mask = capacity - 1;
    }

    public int Capacity => _buffer.Length;

    public int Count
    {
        get
        {
            var count = Volatile.Read(ref _writeSequence) - Volatile.Read(ref _readSequence);
            return (int)Math.Clamp(count, 0L, Capacity);
        }
    }

    public long DroppedSamples => Interlocked.Read(ref _droppedSamples);

    public int Write(ReadOnlySpan<float> source)
    {
        var writeSequence = _writeSequence;
        var readSequence = Volatile.Read(ref _readSequence);
        var available = Capacity - (int)Math.Clamp(writeSequence - readSequence, 0L, Capacity);
        var writeCount = Math.Min(source.Length, available);

        if (writeCount > 0)
        {
            var writeIndex = (int)(writeSequence & _mask);
            var first = Math.Min(writeCount, Capacity - writeIndex);
            source[..first].CopyTo(_buffer.AsSpan(writeIndex, first));
            if (first < writeCount)
            {
                source.Slice(first, writeCount - first).CopyTo(_buffer);
            }

            Volatile.Write(ref _writeSequence, writeSequence + writeCount);
        }

        if (writeCount < source.Length)
        {
            Interlocked.Add(ref _droppedSamples, source.Length - writeCount);
        }

        return writeCount;
    }

    public int Read(Span<float> destination)
    {
        var readSequence = _readSequence;
        var writeSequence = Volatile.Read(ref _writeSequence);
        var available = (int)Math.Clamp(writeSequence - readSequence, 0L, Capacity);
        var readCount = Math.Min(destination.Length, available);

        if (readCount == 0)
        {
            return 0;
        }

        var readIndex = (int)(readSequence & _mask);
        var first = Math.Min(readCount, Capacity - readIndex);
        _buffer.AsSpan(readIndex, first).CopyTo(destination);
        if (first < readCount)
        {
            _buffer.AsSpan(0, readCount - first).CopyTo(destination[first..]);
        }

        Volatile.Write(ref _readSequence, readSequence + readCount);
        return readCount;
    }

    /// <summary>
    /// Advances the consumer without copying samples. Call only from the single consumer.
    /// </summary>
    public int Discard(int sampleCount)
    {
        if (sampleCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleCount));
        }

        var readSequence = _readSequence;
        var writeSequence = Volatile.Read(ref _writeSequence);
        var available = (int)Math.Clamp(writeSequence - readSequence, 0L, Capacity);
        var discardCount = Math.Min(sampleCount, available);
        if (discardCount > 0)
        {
            Volatile.Write(ref _readSequence, readSequence + discardCount);
        }

        return discardCount;
    }

    public void Clear()
    {
        Volatile.Write(ref _readSequence, Volatile.Read(ref _writeSequence));
    }
}

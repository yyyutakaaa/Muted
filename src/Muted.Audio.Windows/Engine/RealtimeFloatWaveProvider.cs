using System.Runtime.InteropServices;
using Muted.Core.Dsp;
using NAudio.Wave;

namespace Muted.Audio.Windows.Engine;

internal sealed class RealtimeFloatWaveProvider : IWaveProvider
{
    private readonly FloatRingBuffer _buffer;
    private readonly int _targetBufferedSamples;
    private readonly int _highWaterSamples;
    private int _startupSamplesToDrain;
    private long _underrunSamples;
    private long _trimmedSamples;
    private int _isLive;

    public RealtimeFloatWaveProvider(
        int sampleRate,
        int capacitySamples,
        int targetBufferedSamples,
        int highWaterSamples,
        int startupBufferedSamples)
    {
        if (targetBufferedSamples <= 0 ||
            highWaterSamples <= targetBufferedSamples ||
            startupBufferedSamples < targetBufferedSamples ||
            startupBufferedSamples > capacitySamples)
        {
            throw new ArgumentOutOfRangeException(nameof(targetBufferedSamples));
        }

        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
        _buffer = new FloatRingBuffer(capacitySamples);
        _targetBufferedSamples = targetBufferedSamples;
        _highWaterSamples = highWaterSamples;
        _startupSamplesToDrain = startupBufferedSamples - targetBufferedSamples;
    }

    public WaveFormat WaveFormat { get; }

    public int BufferedSamples => _buffer.Count;

    public int TargetBufferedSamples => _targetBufferedSamples;

    public long DroppedSamples => _buffer.DroppedSamples + Interlocked.Read(ref _trimmedSamples);

    public long UnderrunSamples => Interlocked.Read(ref _underrunSamples);

    public int Write(ReadOnlySpan<float> samples) => _buffer.Write(samples);

    public void SetLive(bool live) => Volatile.Write(ref _isLive, live ? 1 : 0);

    public int Read(byte[] buffer, int offset, int count)
    {
        var usableBytes = count - (count % sizeof(float));
        var destination = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(offset, usableBytes));
        var buffered = _buffer.Count;
        var startupSamplesToDrain = _startupSamplesToDrain;
        if (startupSamplesToDrain <= 0 && buffered > _highWaterSamples)
        {
            var trimmed = _buffer.Discard(buffered - _targetBufferedSamples);
            Interlocked.Add(ref _trimmedSamples, trimmed);
        }

        var read = _buffer.Read(destination);
        if (startupSamplesToDrain > 0 && read > 0)
        {
            _startupSamplesToDrain = Math.Max(0, startupSamplesToDrain - read);
        }
        if (read < destination.Length)
        {
            destination[read..].Clear();
            if (Volatile.Read(ref _isLive) != 0)
            {
                Interlocked.Add(ref _underrunSamples, destination.Length - read);
            }
        }

        if (usableBytes < count)
        {
            buffer.AsSpan(offset + usableBytes, count - usableBytes).Clear();
        }

        return count;
    }
}

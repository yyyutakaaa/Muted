using System.Numerics;

namespace Muted.Core.Dsp;

/// <summary>
/// In-place radix-2 complex FFT with precomputed twiddles. .NET has no FFT of its
/// own, and the echo canceller needs one on every 10 ms frame, so this allocates
/// nothing per call and keeps real and imaginary parts in separate spans.
/// </summary>
public sealed class Fft
{
    private readonly int[] _reversed;
    private readonly float[] _cos;
    private readonly float[] _sin;

    public Fft(int size)
    {
        if (size < 2 || (size & (size - 1)) != 0)
        {
            throw new ArgumentException("The FFT size must be a power of two.", nameof(size));
        }

        Size = size;
        var bits = BitOperations.Log2((uint)size);
        _reversed = new int[size];
        for (var index = 0; index < size; index++)
        {
            _reversed[index] = (int)(ReverseBits((uint)index) >> (32 - bits));
        }

        _cos = new float[size / 2];
        _sin = new float[size / 2];
        for (var index = 0; index < size / 2; index++)
        {
            var angle = 2.0 * Math.PI * index / size;
            _cos[index] = (float)Math.Cos(angle);
            _sin[index] = (float)-Math.Sin(angle);
        }
    }

    public int Size { get; }

    public void Forward(Span<float> real, Span<float> imaginary) =>
        Transform(real, imaginary, inverse: false);

    /// <summary>Inverse transform, scaled by 1/N so a round trip is the identity.</summary>
    public void Inverse(Span<float> real, Span<float> imaginary) =>
        Transform(real, imaginary, inverse: true);

    private void Transform(Span<float> real, Span<float> imaginary, bool inverse)
    {
        if (real.Length != Size || imaginary.Length != Size)
        {
            throw new ArgumentException($"The FFT works on exactly {Size} samples.");
        }

        for (var index = 0; index < Size; index++)
        {
            var target = _reversed[index];
            if (target > index)
            {
                (real[index], real[target]) = (real[target], real[index]);
                (imaginary[index], imaginary[target]) = (imaginary[target], imaginary[index]);
            }
        }

        for (var length = 2; length <= Size; length <<= 1)
        {
            var half = length >> 1;
            var stride = Size / length;
            for (var start = 0; start < Size; start += length)
            {
                for (var offset = 0; offset < half; offset++)
                {
                    var twiddle = offset * stride;
                    var wr = _cos[twiddle];
                    var wi = inverse ? -_sin[twiddle] : _sin[twiddle];
                    var low = start + offset;
                    var high = low + half;

                    var tr = (real[high] * wr) - (imaginary[high] * wi);
                    var ti = (real[high] * wi) + (imaginary[high] * wr);

                    real[high] = real[low] - tr;
                    imaginary[high] = imaginary[low] - ti;
                    real[low] += tr;
                    imaginary[low] += ti;
                }
            }
        }

        if (!inverse)
        {
            return;
        }

        var scale = 1f / Size;
        for (var index = 0; index < Size; index++)
        {
            real[index] *= scale;
            imaginary[index] *= scale;
        }
    }

    private static uint ReverseBits(uint value)
    {
        value = ((value & 0x55555555u) << 1) | ((value >> 1) & 0x55555555u);
        value = ((value & 0x33333333u) << 2) | ((value >> 2) & 0x33333333u);
        value = ((value & 0x0F0F0F0Fu) << 4) | ((value >> 4) & 0x0F0F0F0Fu);
        value = ((value & 0x00FF00FFu) << 8) | ((value >> 8) & 0x00FF00FFu);
        return (value << 16) | (value >> 16);
    }
}

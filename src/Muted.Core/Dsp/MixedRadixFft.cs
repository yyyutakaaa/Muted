namespace Muted.Core.Dsp;

/// <summary>
/// Complex FFT for sizes that are not a power of two. DPDFNet works on 960-sample
/// frames, and 960 is 64 × 15, so the transform is one Cooley-Tukey split: a radix-2
/// FFT of 64 points for every one of the 15 offsets, a twiddle pass, then a direct
/// 15-point DFT for every one of the 64 outputs. Everything is allocated once.
/// </summary>
public sealed class MixedRadixFft
{
    private readonly int _size;
    private readonly int _power;          // the radix-2 part (64)
    private readonly int _direct;         // the direct part (15)
    private readonly int[] _bitReverse;   // for the radix-2 part
    private readonly float[] _cos;        // twiddles of the radix-2 part
    private readonly float[] _sin;
    private readonly float[] _directCos;  // twiddles of the direct part
    private readonly float[] _directSin;
    private readonly float[] _stepCos;    // Cooley-Tukey twiddles between the two parts
    private readonly float[] _stepSin;
    private readonly float[] _columnRe;   // scratch: one transform's worth of complex data
    private readonly float[] _columnIm;
    private readonly float[] _outRe;      // step 3 scatters, so it cannot write into _column
    private readonly float[] _outIm;
    private readonly float[] _stageRe;
    private readonly float[] _stageIm;

    public MixedRadixFft(int size)
    {
        if (size < 4)
        {
            throw new ArgumentException("The FFT size must be at least 4.", nameof(size));
        }

        // The largest power of two that divides the size, capped so a tiny direct DFT
        // is left over rather than an enormous one.
        _size = size;
        _power = 1;
        while (_power * 8 < size && size % (_power * 2) == 0)
        {
            _power *= 2;
        }

        _direct = size / _power;
        if ((long)_power * _direct != size)
        {
            throw new ArgumentException($"Unsupported FFT size {size}.", nameof(size));
        }

        var powerBits = 0;
        for (var value = _power; value > 1; value >>= 1)
        {
            powerBits++;
        }

        _bitReverse = new int[_power];
        for (var index = 0; index < _power; index++)
        {
            _bitReverse[index] = (int)(ReverseBits((uint)index) >> (32 - powerBits));
        }

        _cos = new float[_power / 2];
        _sin = new float[_power / 2];
        for (var index = 0; index < _power / 2; index++)
        {
            var angle = 2.0 * Math.PI * index / _power;
            _cos[index] = (float)Math.Cos(angle);
            _sin[index] = (float)-Math.Sin(angle);
        }

        _directCos = new float[_direct];
        _directSin = new float[_direct];
        for (var index = 0; index < _direct; index++)
        {
            var angle = 2.0 * Math.PI * index / _direct;
            _directCos[index] = (float)Math.Cos(angle);
            _directSin[index] = (float)-Math.Sin(angle);
        }

        _stepCos = new float[_direct * _power];
        _stepSin = new float[_direct * _power];
        for (var offset = 0; offset < _direct; offset++)
        {
            for (var bin = 0; bin < _power; bin++)
            {
                var angle = 2.0 * Math.PI * offset * bin / _size;
                _stepCos[(offset * _power) + bin] = (float)Math.Cos(angle);
                _stepSin[(offset * _power) + bin] = (float)-Math.Sin(angle);
            }
        }

        _columnRe = new float[_power * _direct];
        _columnIm = new float[_power * _direct];
        _outRe = new float[_power * _direct];
        _outIm = new float[_power * _direct];
        _stageRe = new float[Math.Max(_power, _direct)];
        _stageIm = new float[Math.Max(_power, _direct)];
    }

    public int Size => _size;

    /// <summary>The lowest bin above the middle; the spectrum is conjugate-symmetric above it.</summary>
    public int HalfSize => (_size / 2) + 1;

    /// <summary>Forward transform of real samples, filling bins 0 through <see cref="HalfSize"/>.</summary>
    public void ForwardReal(ReadOnlySpan<float> samples, Span<float> real, Span<float> imaginary)
    {
        if (samples.Length != _size || real.Length < HalfSize || imaginary.Length < HalfSize)
        {
            throw new ArgumentException($"This FFT works on exactly {_size} samples.");
        }

        for (var index = 0; index < _size; index++)
        {
            _columnRe[index] = samples[index];
            _columnIm[index] = 0f;
        }

        Transform(inverse: false);

        for (var index = 0; index < HalfSize; index++)
        {
            real[index] = _columnRe[index];
            imaginary[index] = _columnIm[index];
        }
    }

    /// <summary>Inverse transform of a full spectrum, scaled by 1/N, leaving real samples.</summary>
    public void InverseReal(ReadOnlySpan<float> real, ReadOnlySpan<float> imaginary, Span<float> samples)
    {
        if (real.Length < HalfSize || imaginary.Length < HalfSize || samples.Length != _size)
        {
            throw new ArgumentException($"This FFT works on exactly {_size} samples.");
        }

        var scale = 1f / _size;
        for (var index = 0; index < HalfSize; index++)
        {
            _columnRe[index] = real[index] * scale;
            _columnIm[index] = imaginary[index] * scale;
        }

        // Bins 1..N/2-1 repeat mirrored and conjugated; both ends stay as they are.
        for (var index = 1; index < _size / 2; index++)
        {
            _columnRe[_size - index] = real[index] * scale;
            _columnIm[_size - index] = -imaginary[index] * scale;
        }

        Transform(inverse: true);

        for (var index = 0; index < _size; index++)
        {
            samples[index] = _columnRe[index];
        }
    }

    /// <summary>Transform of the scratch spectrum, in place, in natural order.</summary>
    private void Transform(bool inverse)
    {
        // (1) A radix-2 transform of the power-of-two part for every offset.
        // Sample n = direct*i + offset, so one radix-2 transform covers all i for a fixed offset.
        for (var offset = 0; offset < _direct; offset++)
        {
            for (var index = 0; index < _power; index++)
            {
                var source = (_direct * index) + offset;
                _stageRe[_bitReverse[index]] = _columnRe[source];
                _stageIm[_bitReverse[index]] = _columnIm[source];
            }

            Radix2(inverse);

            for (var bin = 0; bin < _power; bin++)
            {
                var target = (bin * _direct) + offset;
                _columnRe[target] = _stageRe[bin];
                _columnIm[target] = _stageIm[bin];
            }
        }

        // (2) Twiddle between the two parts.
        for (var offset = 0; offset < _direct; offset++)
        {
            for (var bin = 0; bin < _power; bin++)
            {
                var twiddle = (offset * _power) + bin;
                var cosine = _stepCos[twiddle];
                var sine = inverse ? -_stepSin[twiddle] : _stepSin[twiddle];
                var target = (bin * _direct) + offset;
                var real = _columnRe[target];
                var imaginary = _columnIm[target];
                _columnRe[target] = (real * cosine) - (imaginary * sine);
                _columnIm[target] = (real * sine) + (imaginary * cosine);
            }
        }

        // (3) Direct DFT of the remaining size for every bin of the first part.
        for (var bin = 0; bin < _power; bin++)
        {
            var start = bin * _direct;
            for (var index = 0; index < _direct; index++)
            {
                _stageRe[index] = _columnRe[start + index];
                _stageIm[index] = _columnIm[start + index];
            }

            for (var output = 0; output < _direct; output++)
            {
                float sumRe = 0f, sumIm = 0f;
                for (var index = 0; index < _direct; index++)
                {
                    var twiddle = (index * output) % _direct;
                    var cosine = _directCos[twiddle];
                    var sine = inverse ? -_directSin[twiddle] : _directSin[twiddle];
                    var real = _stageRe[index];
                    var imaginary = _stageIm[index];
                    sumRe += (real * cosine) - (imaginary * sine);
                    sumIm += (real * sine) + (imaginary * cosine);
                }

                // Output k = P*k2 + k1 scatters across the spectrum, so it lands in its own
                // buffer: writing into _column would overwrite inputs of later bins.
                var target = (_power * output) + bin;
                _outRe[target] = sumRe;
                _outIm[target] = sumIm;
            }
        }

        Array.Copy(_outRe, _columnRe, _size);
        Array.Copy(_outIm, _columnIm, _size);
    }

    private void Radix2(bool inverse)
    {
        for (var length = 2; length <= _power; length <<= 1)
        {
            var half = length >> 1;
            var stride = _power / length;
            for (var start = 0; start < _power; start += length)
            {
                for (var offset = 0; offset < half; offset++)
                {
                    var twiddle = offset * stride;
                    var wr = _cos[twiddle];
                    var wi = inverse ? -_sin[twiddle] : _sin[twiddle];
                    var low = start + offset;
                    var high = low + half;

                    var tr = (_stageRe[high] * wr) - (_stageIm[high] * wi);
                    var ti = (_stageRe[high] * wi) + (_stageIm[high] * wr);

                    _stageRe[high] = _stageRe[low] - tr;
                    _stageIm[high] = _stageIm[low] - ti;
                    _stageRe[low] += tr;
                    _stageIm[low] += ti;
                }
            }
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

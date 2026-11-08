using Muted.Core.Dsp;

namespace Muted.Core.Speech;

/// <summary>
/// The 80-band Kaldi filter bank the speaker model was trained on (WeSpeaker's
/// torchaudio.compliance.kaldi.fbank call): 25 ms Hamming frames every 10 ms of 16 kHz
/// audio at 16-bit scale, DC removal, pre-emphasis, a 512-point power spectrum, log mel
/// energies, and finally the mean of every band subtracted. Laid out as [frame, band].
/// </summary>
public sealed class SpeakerFeatures
{
    public const int SampleRate = 16_000;
    public const int Bands = 80;
    private const int FrameLength = 400;
    private const int Shift = 160;
    private const int FftSize = 512;
    private const float PreEmphasis = 0.97f;

    /// <summary>Kaldi floors energies at the single-precision epsilon before the log.</summary>
    private const double LogFloor = 1.1920928955078125e-07;

    private static readonly float[] Window = BuildWindow();
    private static readonly float[] Filters = BuildFilters();

    private readonly Fft _fft = new(FftSize);
    private readonly float[] _real = new float[FftSize];
    private readonly float[] _imaginary = new float[FftSize];

    public static int FrameCount(int samples) => samples < FrameLength ? 0 : 1 + ((samples - FrameLength) / Shift);

    public float[] Compute(ReadOnlySpan<float> audio)
    {
        var frames = FrameCount(audio.Length);
        var features = new float[frames * Bands];
        if (frames == 0)
        {
            return features;
        }

        for (var frame = 0; frame < frames; frame++)
        {
            var samples = audio.Slice(frame * Shift, FrameLength);
            var mean = 0f;
            for (var index = 0; index < FrameLength; index++)
            {
                mean += samples[index];
            }

            mean /= FrameLength;

            // Pre-emphasis runs on the DC-free frame; the first sample uses itself as
            // its predecessor, as Kaldi does.
            var previous = samples[0] - mean;
            for (var index = 0; index < FrameLength; index++)
            {
                var current = samples[index] - mean;
                var emphasised = current - (PreEmphasis * previous);
                previous = current;
                _real[index] = emphasised * 32_768f * Window[index];
                _imaginary[index] = 0f;
            }

            _real.AsSpan(FrameLength).Clear();
            _imaginary.AsSpan(FrameLength).Clear();
            _fft.Forward(_real, _imaginary);

            for (var band = 0; band < Bands; band++)
            {
                var energy = 0.0;
                var row = band * (FftSize / 2);
                for (var bin = 0; bin < FftSize / 2; bin++)
                {
                    var weight = Filters[row + bin];
                    if (weight != 0f)
                    {
                        energy += weight * ((_real[bin] * _real[bin]) + (_imaginary[bin] * _imaginary[bin]));
                    }
                }

                features[(frame * Bands) + band] = (float)Math.Log(Math.Max(energy, LogFloor));
            }
        }

        // Cepstral mean normalisation over the whole window.
        for (var band = 0; band < Bands; band++)
        {
            var sum = 0.0;
            for (var frame = 0; frame < frames; frame++)
            {
                sum += features[(frame * Bands) + band];
            }

            var mean = (float)(sum / frames);
            for (var frame = 0; frame < frames; frame++)
            {
                features[(frame * Bands) + band] -= mean;
            }
        }

        return features;
    }

    private static float[] BuildWindow()
    {
        var window = new float[FrameLength];
        for (var index = 0; index < FrameLength; index++)
        {
            window[index] = (float)(0.54 - (0.46 * Math.Cos(2 * Math.PI * index / (FrameLength - 1))));
        }

        return window;
    }

    private static float[] BuildFilters()
    {
        // Kaldi's triangular bins, equally spaced on 1127 ln(1 + f / 700) between 20 Hz
        // and the Nyquist frequency, evaluated at the centre of each FFT bin.
        static double Mel(double hz) => 1127.0 * Math.Log(1.0 + (hz / 700.0));
        var low = Mel(20.0);
        var high = Mel(SampleRate / 2.0);
        var delta = (high - low) / (Bands + 1);
        var bins = FftSize / 2;
        var filters = new float[Bands * bins];
        for (var band = 0; band < Bands; band++)
        {
            var left = low + (band * delta);
            var centre = left + delta;
            var right = centre + delta;
            for (var bin = 0; bin < bins; bin++)
            {
                var mel = Mel(bin * (double)SampleRate / FftSize);
                if (mel > left && mel < right)
                {
                    filters[(band * bins) + bin] = (float)(mel <= centre
                        ? (mel - left) / (centre - left)
                        : (right - mel) / (right - centre));
                }
            }
        }

        return filters;
    }
}

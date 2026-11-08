using Muted.Core.Dsp;

namespace Muted.Core.Speech;

/// <summary>
/// Whisper's input: an 80-band log-mel spectrogram of exactly 30 seconds of 16 kHz
/// audio, laid out as [mel band, frame]. Follows OpenAI's reference: a 400-sample Hann
/// window every 160 samples on reflect-padded audio, Slaney mel filters, log10, the top
/// 8 decades kept, then scaled to about -1..1.
/// </summary>
public sealed class WhisperFeatures
{
    public const int SampleRate = 16_000;
    public const int MelBands = 80;
    public const int Frames = 3_000;
    public const int Samples = SampleRate * 30;
    private const int WindowSize = 400;
    private const int Hop = 160;
    private const int Bins = (WindowSize / 2) + 1;

    private static readonly float[] Filters = BuildFilters();
    private static readonly float[] Window = BuildWindow();

    private readonly MixedRadixFft _fft = new(WindowSize);
    private readonly float[] _frame = new float[WindowSize];
    private readonly float[] _real = new float[Bins];
    private readonly float[] _imaginary = new float[Bins];
    private readonly float[] _power = new float[Bins];

    /// <summary>Computes the features of <paramref name="audio"/>, padded or cut to 30 seconds.</summary>
    public float[] Compute(ReadOnlySpan<float> audio)
    {
        if (audio.Length > Samples)
        {
            audio = audio[^Samples..];
        }

        var features = new float[MelBands * Frames];
        var max = float.NegativeInfinity;
        for (var frame = 0; frame < Frames; frame++)
        {
            var start = (frame * Hop) - (WindowSize / 2);
            if (start >= audio.Length)
            {
                // Only the zero padding is left: every band sits on the floor.
                for (var band = 0; band < MelBands; band++)
                {
                    features[(band * Frames) + frame] = -10f;
                }

                max = MathF.Max(max, -10f);
                continue;
            }

            for (var index = 0; index < WindowSize; index++)
            {
                _frame[index] = Sample(audio, start + index) * Window[index];
            }

            _fft.ForwardReal(_frame, _real, _imaginary);
            for (var bin = 0; bin < Bins; bin++)
            {
                _power[bin] = (_real[bin] * _real[bin]) + (_imaginary[bin] * _imaginary[bin]);
            }

            for (var band = 0; band < MelBands; band++)
            {
                var energy = 0f;
                var row = band * Bins;
                for (var bin = 0; bin < Bins; bin++)
                {
                    energy += Filters[row + bin] * _power[bin];
                }

                var value = MathF.Log10(MathF.Max(energy, 1e-10f));
                features[(band * Frames) + frame] = value;
                max = MathF.Max(max, value);
            }
        }

        var floor = max - 8f;
        for (var index = 0; index < features.Length; index++)
        {
            features[index] = (MathF.Max(features[index], floor) + 4f) / 4f;
        }

        return features;
    }

    /// <summary>The audio as if it were zero-padded to 30 seconds and reflected at both ends.</summary>
    private static float Sample(ReadOnlySpan<float> audio, int index)
    {
        if (index < 0)
        {
            index = -index;
        }
        else if (index >= Samples)
        {
            index = (2 * (Samples - 1)) - index;
        }

        return index < audio.Length ? audio[index] : 0f;
    }

    private static float[] BuildWindow()
    {
        // Periodic Hann, as torch.hann_window builds it.
        var window = new float[WindowSize];
        for (var index = 0; index < WindowSize; index++)
        {
            window[index] = (float)(0.5 - (0.5 * Math.Cos(2 * Math.PI * index / WindowSize)));
        }

        return window;
    }

    private static float[] BuildFilters()
    {
        // librosa.filters.mel(sr=16000, n_fft=400, n_mels=80): Slaney's mel scale and
        // area normalisation.
        var mels = new double[MelBands + 2];
        var top = HzToMel(SampleRate / 2.0);
        for (var index = 0; index < mels.Length; index++)
        {
            mels[index] = MelToHz(top * index / (MelBands + 1));
        }

        var filters = new float[MelBands * Bins];
        for (var band = 0; band < MelBands; band++)
        {
            var lower = mels[band];
            var centre = mels[band + 1];
            var upper = mels[band + 2];
            var norm = 2.0 / (upper - lower);
            for (var bin = 0; bin < Bins; bin++)
            {
                var hz = bin * (SampleRate / 2.0) / (Bins - 1);
                var rising = (hz - lower) / (centre - lower);
                var falling = (upper - hz) / (upper - centre);
                filters[(band * Bins) + bin] = (float)(Math.Max(0, Math.Min(rising, falling)) * norm);
            }
        }

        return filters;
    }

    private const double LinearStep = 200.0 / 3.0;
    private const double LogStartHz = 1_000.0;
    private const double LogStartMel = LogStartHz / LinearStep;
    // ln(6.4) / 27, written out: a static field would be initialised after the filters that use it.
    private const double LogStep = 0.06875177742094912;

    private static double HzToMel(double hz) => hz >= LogStartHz
        ? LogStartMel + (Math.Log(hz / LogStartHz) / LogStep)
        : hz / LinearStep;

    private static double MelToHz(double mel) => mel >= LogStartMel
        ? LogStartHz * Math.Exp(LogStep * (mel - LogStartMel))
        : mel * LinearStep;
}

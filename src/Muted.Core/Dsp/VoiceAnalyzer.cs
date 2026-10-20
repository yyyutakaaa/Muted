using Muted.Core.Audio;

namespace Muted.Core.Dsp;

/// <summary>What the calibration heard. Levels are raw microphone dBFS, before any boost.</summary>
public sealed record VoiceMeasurement(
    float NoiseFloorDb,
    float SpeechLevelDb,
    float SpeechPeakDb,
    float LoudPeakDb,
    float PitchHz,
    float SibilanceDb,
    float DynamicRangeDb,
    float RumbleShareDb,
    float QuietVoiceProbability,
    float SpeechVoiceProbability,
    double SpeechSeconds,
    bool Clipped);

/// <summary>Settings worked out from a <see cref="VoiceMeasurement"/>.</summary>
public sealed record VoiceRecommendation(
    float InputGain,
    float VoiceThreshold,
    float HighPassFrequency,
    float ShoutThresholdDb,
    float Warmth,
    float Proximity,
    float Smoothness,
    float Compression,
    bool Reliable,
    IReadOnlyList<string> Notes);

/// <summary>
/// Offline analysis of a calibration take: a few seconds of silence, then normal
/// reading, then one deliberately loud phrase. Runs off the audio thread, so it is
/// free to allocate. Every rule here is deliberately simple and bounded: it picks a
/// sensible starting point, it does not try to be clever.
/// </summary>
public static class VoiceAnalyzer
{
    /// <summary>Where speech should sit after the microphone boost, as RMS in dBFS.</summary>
    public const float TargetSpeechDb = -22f;

    /// <summary>Normal speech peaks are kept at least this far below full scale.</summary>
    public const float PeakHeadroomDb = -3f;

    public const float MinimumGainDb = -12.04f;
    public const float MaximumGainDb = 12.04f;

    private const int SampleRate = CalibrationRecorder.SampleRate;
    private const int FrameSize = CalibrationRecorder.FrameSize;
    private const int SettleFrames = 30;
    private const float SpeechProbability = 0.6f;
    private const float SilentDb = -100f;

    public static VoiceMeasurement Measure(CalibrationRecording recording, double quietSeconds, double speechEndSeconds)
    {
        ArgumentNullException.ThrowIfNull(recording);
        var frames = Math.Min(recording.Frames, recording.Samples.Length / FrameSize);
        var quietEnd = Math.Clamp((int)(quietSeconds * SampleRate / FrameSize), 0, frames);
        var speechEnd = Math.Clamp((int)(speechEndSeconds * SampleRate / FrameSize), quietEnd, frames);

        var rmsDb = new float[frames];
        var peakDb = new float[frames];
        for (var frame = 0; frame < frames; frame++)
        {
            var span = Frame(recording, frame);
            rmsDb[frame] = ToDb(AudioMath.Rms(span));
            peakDb[frame] = ToDb(AudioMath.Peak(span));
        }

        // Silence: skip the first moments, while someone is still settling in.
        var quiet = new List<int>();
        for (var frame = Math.Min(SettleFrames, quietEnd / 2); frame < quietEnd; frame++)
        {
            quiet.Add(frame);
        }

        var noiseFloor = quiet.Count == 0 ? SilentDb : Percentile(quiet.Select(f => rmsDb[f]), 0.5);
        var quietProbability = quiet.Count == 0 ? 0f : Percentile(quiet.Select(f => recording.VoiceProbabilities[f]), 0.95);

        var speech = new List<int>();
        for (var frame = quietEnd; frame < speechEnd; frame++)
        {
            if (recording.VoiceProbabilities[frame] >= SpeechProbability && rmsDb[frame] > noiseFloor + 6f)
            {
                speech.Add(frame);
            }
        }

        var speechLevel = SilentDb;
        var speechPeak = SilentDb;
        var dynamicRange = 0f;
        var speechProbability = 0f;
        var clipped = false;
        if (speech.Count > 0)
        {
            var meanPower = speech.Average(f => Math.Pow(10, rmsDb[f] / 10.0));
            speechLevel = (float)(10.0 * Math.Log10(Math.Max(meanPower, 1e-10)));
            speechPeak = Percentile(speech.Select(f => peakDb[f]), 0.99);
            dynamicRange = Percentile(speech.Select(f => rmsDb[f]), 0.9) - Percentile(speech.Select(f => rmsDb[f]), 0.1);
            speechProbability = Percentile(speech.Select(f => recording.VoiceProbabilities[f]), 0.5);
            clipped = speech.Any(f => peakDb[f] > -0.15f);
        }

        var loudPeak = SilentDb;
        for (var frame = speechEnd; frame < frames; frame++)
        {
            loudPeak = MathF.Max(loudPeak, peakDb[frame]);
        }

        return new VoiceMeasurement(
            noiseFloor,
            speechLevel,
            speechPeak,
            loudPeak,
            MeasurePitch(recording, speech),
            MeasureSibilance(recording, speech),
            dynamicRange,
            MeasureRumbleShare(recording, quiet),
            quietProbability,
            speechProbability,
            speech.Count * FrameSize / (double)SampleRate,
            clipped);
    }

    public static VoiceRecommendation Recommend(VoiceMeasurement measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        var notes = new List<string>();
        var reliable = measurement.SpeechSeconds >= 3.0;
        if (!reliable)
        {
            notes.Add("Too little speech was heard. Move closer to the microphone and try again.");
        }

        // Boost: bring speech to the target, but never push normal peaks into clipping.
        var gainDb = TargetSpeechDb - measurement.SpeechLevelDb;
        gainDb = MathF.Min(gainDb, PeakHeadroomDb - measurement.SpeechPeakDb);
        if (gainDb > MaximumGainDb && reliable)
        {
            notes.Add("Your microphone is very quiet, even at full boost. Raise its level in the Windows sound settings.");
        }

        gainDb = Math.Clamp(gainDb, MinimumGainDb, MaximumGainDb);
        if (!reliable)
        {
            gainDb = 0f;
        }

        if (measurement.Clipped)
        {
            notes.Add("Your microphone clips while you talk normally. Lower its level in the Windows sound settings and calibrate again.");
        }

        // Gate: just above how speech-like the room sounds when you are quiet.
        var threshold = Math.Clamp(measurement.QuietVoiceProbability + 0.12f, 0.3f, 0.85f);
        if (measurement.SpeechVoiceProbability > 0.4f)
        {
            threshold = MathF.Min(threshold, measurement.SpeechVoiceProbability - 0.1f);
        }

        threshold = Math.Clamp(threshold, 0.05f, 0.99f);

        if (measurement.NoiseFloorDb + gainDb > -45f)
        {
            notes.Add("The room is fairly noisy. Keep noise suppression on.");
        }

        // High-pass: keep the low end for warmth, unless the desk or room rumbles.
        var rumble = measurement.RumbleShareDb > -3f && measurement.NoiseFloorDb > -70f;
        var highPass = rumble ? 100f : 70f;
        if (rumble)
        {
            notes.Add("There is low rumble in the room, so the high-pass is set a little higher.");
        }

        // Shout guard: between your normal peaks and your shout, never right on top of normal speech.
        var normalPeak = measurement.SpeechPeakDb + gainDb;
        var loud = MathF.Min(measurement.LoudPeakDb + gainDb, 0f);
        float shout;
        if (loud >= normalPeak + 6f)
        {
            shout = normalPeak + MathF.Max(3f, 0.4f * (loud - normalPeak));
        }
        else
        {
            shout = normalPeak + 6f;
            notes.Add("No clearly louder phrase was heard, so the shout limit is 6 dB above your normal peaks.");
        }

        shout = Math.Clamp(shout, ShoutGuard.MinimumThresholdDb, ShoutGuard.MaximumThresholdDb);

        // Character: lower voices need less low-end help before they turn muddy.
        float warmth = 0.6f, proximity = 0.5f;
        if (float.IsFinite(measurement.PitchHz))
        {
            var pitch = Math.Clamp((measurement.PitchHz - 90f) / 130f, 0f, 1f);
            warmth = 0.45f + (0.35f * pitch);
            proximity = 0.4f + (0.3f * pitch);
        }

        var smoothness = 0.6f;
        if (float.IsFinite(measurement.SibilanceDb))
        {
            smoothness = 0.35f + (0.5f * Math.Clamp((measurement.SibilanceDb + 28f) / 16f, 0f, 1f));
        }

        var compression = 0.3f + (0.45f * Math.Clamp((measurement.DynamicRangeDb - 6f) / 14f, 0f, 1f));

        return new VoiceRecommendation(
            MathF.Pow(10f, gainDb / 20f),
            threshold,
            highPass,
            shout,
            warmth,
            proximity,
            smoothness,
            compression,
            reliable,
            notes);
    }

    private static ReadOnlySpan<float> Frame(CalibrationRecording recording, int frame) =>
        recording.Samples.AsSpan(frame * FrameSize, FrameSize);

    private static float ToDb(float amplitude) =>
        amplitude <= 1e-5f ? SilentDb : 20f * MathF.Log10(amplitude);

    private static float Percentile(IEnumerable<float> values, double percentile)
    {
        var sorted = values.Where(float.IsFinite).OrderBy(value => value).ToArray();
        if (sorted.Length == 0)
        {
            return 0f;
        }

        var index = (int)Math.Round(percentile * (sorted.Length - 1));
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    /// <summary>
    /// Median fundamental over the speech frames, by normalised autocorrelation on a
    /// 12 kHz copy. Returns NaN when too few frames are clearly voiced.
    /// </summary>
    private static float MeasurePitch(CalibrationRecording recording, IReadOnlyList<int> speech)
    {
        const int decimation = 4;
        const int rate = SampleRate / decimation;
        const int window = 240;
        const int minimumLag = rate / 400;
        const int maximumLag = rate / 70;

        var samples = recording.Samples;
        var decimated = new float[samples.Length / decimation];
        for (var index = 0; index < decimated.Length; index++)
        {
            var start = index * decimation;
            decimated[index] = (samples[start] + samples[start + 1] + samples[start + 2] + samples[start + 3]) * 0.25f;
        }

        var pitches = new List<float>();
        var correlations = new float[maximumLag + 1];
        foreach (var frame in speech)
        {
            var start = frame * FrameSize / decimation;
            if (start + window + maximumLag > decimated.Length)
            {
                continue;
            }

            var best = 0f;
            for (var lag = minimumLag; lag <= maximumLag; lag++)
            {
                double cross = 0, energyA = 0, energyB = 0;
                for (var n = 0; n < window; n++)
                {
                    var a = decimated[start + n];
                    var b = decimated[start + n + lag];
                    cross += a * b;
                    energyA += a * a;
                    energyB += b * b;
                }

                var r = energyA <= 1e-12 || energyB <= 1e-12 ? 0f : (float)(cross / Math.Sqrt(energyA * energyB));
                correlations[lag] = r;
                best = MathF.Max(best, r);
            }

            if (best < 0.6f)
            {
                continue;
            }

            // The shortest lag that is nearly as good as the best avoids octave-down errors.
            for (var lag = minimumLag; lag <= maximumLag; lag++)
            {
                if (correlations[lag] >= best * 0.9f && IsLocalPeak(correlations, lag, minimumLag, maximumLag))
                {
                    pitches.Add(rate / (float)lag);
                    break;
                }
            }
        }

        return pitches.Count < 20 ? float.NaN : Percentile(pitches, 0.5);
    }

    private static bool IsLocalPeak(float[] values, int index, int minimum, int maximum) =>
        (index == minimum || values[index] >= values[index - 1]) &&
        (index == maximum || values[index] >= values[index + 1]);

    /// <summary>Share of energy in 5–9 kHz, in dB, at the 90th percentile of speech frames.</summary>
    private static float MeasureSibilance(CalibrationRecording recording, IReadOnlyList<int> speech)
    {
        const int size = 512;
        var fft = new Fft(size);
        var real = new float[size];
        var imaginary = new float[size];
        var window = Hann(FrameSize);
        var lowBin = (int)(5_000f * size / SampleRate);
        var highBin = (int)(9_000f * size / SampleRate);
        var ratios = new List<float>();

        foreach (var frame in speech)
        {
            var span = Frame(recording, frame);
            Array.Clear(real);
            Array.Clear(imaginary);
            for (var index = 0; index < FrameSize; index++)
            {
                real[index] = span[index] * window[index];
            }

            fft.Forward(real, imaginary);
            double total = 0, sibilant = 0;
            for (var bin = 1; bin < size / 2; bin++)
            {
                var power = (real[bin] * real[bin]) + (imaginary[bin] * imaginary[bin]);
                total += power;
                if (bin >= lowBin && bin <= highBin)
                {
                    sibilant += power;
                }
            }

            if (total > 1e-12)
            {
                ratios.Add((float)(10.0 * Math.Log10(Math.Max(sibilant / total, 1e-6))));
            }
        }

        return ratios.Count == 0 ? float.NaN : Percentile(ratios, 0.9);
    }

    /// <summary>Share of the room noise below 90 Hz, in dB (0 dB = all of it).</summary>
    private static float MeasureRumbleShare(CalibrationRecording recording, IReadOnlyList<int> quiet)
    {
        const int size = 4096;
        if (quiet.Count * FrameSize < size)
        {
            return -60f;
        }

        var fft = new Fft(size);
        var real = new float[size];
        var imaginary = new float[size];
        var window = Hann(size);
        var cutoff = (int)(90f * size / SampleRate);
        double total = 0, low = 0;
        var first = quiet[0] * FrameSize;
        var last = (quiet[^1] + 1) * FrameSize;

        for (var start = first; start + size <= last; start += size / 2)
        {
            Array.Clear(imaginary);
            for (var index = 0; index < size; index++)
            {
                real[index] = recording.Samples[start + index] * window[index];
            }

            fft.Forward(real, imaginary);
            for (var bin = 1; bin < size / 2; bin++)
            {
                var power = (real[bin] * real[bin]) + (imaginary[bin] * imaginary[bin]);
                total += power;
                if (bin <= cutoff)
                {
                    low += power;
                }
            }
        }

        return total <= 1e-12 ? -60f : (float)(10.0 * Math.Log10(Math.Max(low / total, 1e-6)));
    }

    private static float[] Hann(int length)
    {
        var window = new float[length];
        for (var index = 0; index < length; index++)
        {
            window[index] = 0.5f - (0.5f * MathF.Cos(2f * MathF.PI * index / (length - 1)));
        }

        return window;
    }
}

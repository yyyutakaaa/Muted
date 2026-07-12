namespace Muted.Core.Audio;

public readonly record struct AudioMetrics(
    float InputPeak,
    float OutputPeak,
    float VoiceProbability,
    float ProcessingLoad,
    double BufferedMilliseconds,
    long DroppedInputSamples,
    long DroppedOutputSamples,
    long OutputUnderrunSamples)
{
    public static AudioMetrics Empty => new();
}

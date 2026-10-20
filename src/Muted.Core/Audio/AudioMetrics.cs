namespace Muted.Core.Audio;

public readonly record struct AudioMetrics(
    float InputPeak,
    float OutputPeak,
    float VoiceProbability,
    float ProcessingLoad,
    double BufferedMilliseconds,
    long DroppedInputSamples,
    long DroppedOutputSamples,
    long OutputUnderrunSamples,
    float InputRms = 0f,
    float OutputRms = 0f,
    float NoiseReductionDb = 0f,
    float LimiterReductionDb = 0f,
    float AutoGainDb = 0f,
    float EchoReductionDb = 0f,
    bool MonitorActive = false,
    bool EchoActive = false,
    float CompressorReductionDb = 0f,
    bool ShoutGuardEngaged = false,
    long ShoutGuardTriggers = 0,
    float TrackedSpeechDb = -100f,
    float TrackedNoiseDb = -100f,
    float TrackedQuietProbability = 0f,
    long TrackedSpeechFrames = 0)
{
    public static AudioMetrics Empty => new();
}

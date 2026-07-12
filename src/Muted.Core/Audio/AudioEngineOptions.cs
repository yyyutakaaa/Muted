namespace Muted.Core.Audio;

public sealed record AudioEngineOptions(
    string? InputDeviceId,
    string? OutputDeviceId,
    int LatencyMilliseconds,
    SuppressionOptions Suppression)
{
    public AudioEngineOptions Normalize() => this with
    {
        LatencyMilliseconds = Math.Clamp(LatencyMilliseconds, 20, 100),
        Suppression = Suppression.Normalize()
    };
}

public sealed record SuppressionOptions(
    bool Enabled = true,
    float WetMix = 1f,
    bool VoiceGateEnabled = true,
    float VoiceThreshold = 0.55f,
    int VoiceHoldMilliseconds = 250,
    float InputGain = 1f,
    float OutputGain = 1f)
{
    public SuppressionOptions Normalize() => this with
    {
        WetMix = Math.Clamp(WetMix, 0f, 1f),
        VoiceThreshold = Math.Clamp(VoiceThreshold, 0.05f, 0.99f),
        VoiceHoldMilliseconds = Math.Clamp(VoiceHoldMilliseconds, 0, 2_000),
        InputGain = Math.Clamp(InputGain, 0.25f, 4f),
        OutputGain = Math.Clamp(OutputGain, 0.25f, 4f)
    };
}

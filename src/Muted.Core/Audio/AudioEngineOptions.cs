namespace Muted.Core.Audio;

public sealed record AudioEngineOptions(
    string? InputDeviceId,
    string? OutputDeviceId,
    int LatencyMilliseconds,
    SuppressionOptions Suppression,
    MonitorOptions? Monitor = null)
{
    public AudioEngineOptions Normalize() => this with
    {
        LatencyMilliseconds = Math.Clamp(LatencyMilliseconds, 20, 100),
        Suppression = Suppression.Normalize(),
        Monitor = (Monitor ?? MonitorOptions.Disabled).Normalize()
    };
}

/// <summary>Optional second render path so you can hear your own processed voice.</summary>
public sealed record MonitorOptions(
    bool Enabled = false,
    string? DeviceId = null,
    float Volume = 0.6f)
{
    public static MonitorOptions Disabled { get; } = new();

    public bool IsActive => Enabled && !string.IsNullOrWhiteSpace(DeviceId);

    public MonitorOptions Normalize() => this with
    {
        Volume = Math.Clamp(Volume, 0f, 1f)
    };
}

public sealed record SuppressionOptions(
    bool Enabled = true,
    float WetMix = 1f,
    bool VoiceGateEnabled = true,
    float VoiceThreshold = 0.55f,
    int VoiceHoldMilliseconds = 250,
    float InputGain = 1f,
    float OutputGain = 1f,
    bool IsMuted = false,
    bool HighPassEnabled = false,
    float HighPassFrequency = 80f,
    bool LimiterEnabled = true,
    bool AutoGainEnabled = false,
    float AutoGainTargetDb = -18f)
{
    public SuppressionOptions Normalize() => this with
    {
        WetMix = Math.Clamp(WetMix, 0f, 1f),
        VoiceThreshold = Math.Clamp(VoiceThreshold, 0.05f, 0.99f),
        VoiceHoldMilliseconds = Math.Clamp(VoiceHoldMilliseconds, 0, 2_000),
        InputGain = Math.Clamp(InputGain, 0.25f, 4f),
        OutputGain = Math.Clamp(OutputGain, 0.25f, 4f),
        HighPassFrequency = Math.Clamp(HighPassFrequency, 40f, 200f),
        AutoGainTargetDb = Math.Clamp(AutoGainTargetDb, -30f, -6f)
    };
}

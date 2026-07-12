using Muted.Core.Audio;

namespace Muted.Core.Settings;

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string? InputDeviceId { get; init; }
    public string? OutputDeviceId { get; init; }
    public bool FollowDefaultInput { get; init; } = true;
    public bool SuppressionEnabled { get; init; } = true;
    public float WetMix { get; init; } = 1f;
    public bool VoiceGateEnabled { get; init; } = true;
    public float VoiceThreshold { get; init; } = 0.55f;
    public int VoiceHoldMilliseconds { get; init; } = 250;
    public int TargetLatencyMilliseconds { get; init; } = 40;
    public bool StartWithWindows { get; init; }
    public bool StartMinimized { get; init; }
    public bool MinimizeToTray { get; init; } = true;
    public bool WasRunningOnExit { get; init; }

    public AppSettings Normalize() => this with
    {
        SchemaVersion = CurrentSchemaVersion,
        WetMix = Math.Clamp(WetMix, 0f, 1f),
        VoiceThreshold = Math.Clamp(VoiceThreshold, 0.05f, 0.99f),
        VoiceHoldMilliseconds = Math.Clamp(VoiceHoldMilliseconds, 0, 2_000),
        TargetLatencyMilliseconds = Math.Clamp(TargetLatencyMilliseconds, 20, 100)
    };

    public SuppressionOptions ToSuppressionOptions() => new(
        SuppressionEnabled,
        WetMix,
        VoiceGateEnabled,
        VoiceThreshold,
        VoiceHoldMilliseconds);
}

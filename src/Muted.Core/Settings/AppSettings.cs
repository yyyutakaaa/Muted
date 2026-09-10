using Muted.Core.Audio;

namespace Muted.Core.Settings;

public enum AppTheme
{
    System,
    Dark,
    Light
}

public enum UpdateChannel
{
    Stable,
    Beta
}

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 4;
    public const int MaximumProfiles = 20;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string? InputDeviceId { get; init; }
    public string? OutputDeviceId { get; init; }
    public bool FollowDefaultInput { get; init; }
    public bool SuppressionEnabled { get; init; } = true;
    public float WetMix { get; init; } = 1f;
    public bool VoiceGateEnabled { get; init; } = true;
    public float VoiceThreshold { get; init; } = 0.55f;
    public int VoiceHoldMilliseconds { get; init; } = 250;
    public float InputGain { get; init; } = 1f;
    public float OutputGain { get; init; } = 1f;
    public bool HighPassEnabled { get; init; }
    public float HighPassFrequency { get; init; } = 80f;
    public bool LimiterEnabled { get; init; } = true;
    public bool AutoGainEnabled { get; init; }
    public float AutoGainTargetDb { get; init; } = -18f;
    public bool EchoCancellationEnabled { get; init; }
    public string? EchoReferenceDeviceId { get; init; }
    public float EchoStrength { get; init; } = 0.5f;
    public bool MonitorEnabled { get; init; }
    public string? MonitorDeviceId { get; init; }
    public float MonitorVolume { get; init; } = 0.6f;
    public int TargetLatencyMilliseconds { get; init; } = 40;
    public bool StartWithWindows { get; init; }
    public bool StartMinimized { get; init; }
    public bool MinimizeToTray { get; init; } = true;
    public bool AutoRecoverDevices { get; init; } = true;
    public bool StartMuted { get; init; }
    public bool WasRunningOnExit { get; init; }

    /// <summary>Follows Windows by default, and keeps following while it changes.</summary>
    public AppTheme Theme { get; init; } = AppTheme.System;

    /// <summary>
    /// Trades Muted's violet for whatever highlight colour Windows is set to. Off by
    /// default, because the accent is the one place the app's own colour shows.
    /// </summary>
    public bool UseSystemAccentColor { get; init; }
    public bool CompactMode { get; init; }

    /// <summary>
    /// Reveals the rest of the processing chain and the live numbers. Off by default:
    /// most people want a microphone that works, not a mixing desk.
    /// </summary>
    public bool AdvancedMode { get; init; }
    public UpdateChannel UpdateChannel { get; init; } = UpdateChannel.Stable;
    public string? SkippedUpdateVersion { get; init; }
    public string? ActiveProfileId { get; init; }
    public IReadOnlyList<HotkeyBinding> Hotkeys { get; init; } = HotkeyBinding.CreateDefaults();
    public IReadOnlyList<AudioProfile> Profiles { get; init; } = [];

    public AppSettings Normalize()
    {
        var profiles = (Profiles ?? [])
            .Where(profile => profile is not null)
            .Select(profile => profile.Normalize())
            .DistinctBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumProfiles)
            .ToList();

        if (profiles.Count == 0)
        {
            profiles.AddRange(AudioProfile.CreateDefaults(InputDeviceId, OutputDeviceId));
            profiles[0] = (profiles[0] with
            {
                SuppressionEnabled = SuppressionEnabled,
                WetMix = WetMix,
                VoiceGateEnabled = VoiceGateEnabled,
                VoiceThreshold = VoiceThreshold,
                VoiceHoldMilliseconds = VoiceHoldMilliseconds,
                InputGain = InputGain,
                OutputGain = OutputGain,
                HighPassEnabled = HighPassEnabled,
                HighPassFrequency = HighPassFrequency,
                LimiterEnabled = LimiterEnabled,
                AutoGainEnabled = AutoGainEnabled,
                AutoGainTargetDb = AutoGainTargetDb
            }).Normalize();
        }

        var activeProfileId = profiles.Any(profile =>
                string.Equals(profile.Id, ActiveProfileId, StringComparison.OrdinalIgnoreCase))
            ? ActiveProfileId
            : profiles[0].Id;

        // Schema 4 rebuilt the palette around one violet. A settings file older than
        // that predates the change, so the Windows accent override is cleared once and
        // the new colours actually get seen; turning it back on is a click away.
        var useSystemAccentColor = SchemaVersion >= 4 && UseSystemAccentColor;

        return this with
        {
            SchemaVersion = CurrentSchemaVersion,
            UseSystemAccentColor = useSystemAccentColor,
            WetMix = Math.Clamp(WetMix, 0f, 1f),
            VoiceThreshold = Math.Clamp(VoiceThreshold, 0.05f, 0.99f),
            VoiceHoldMilliseconds = Math.Clamp(VoiceHoldMilliseconds, 0, 2_000),
            InputGain = Math.Clamp(InputGain, 0.25f, 4f),
            OutputGain = Math.Clamp(OutputGain, 0.25f, 4f),
            HighPassFrequency = Math.Clamp(HighPassFrequency, 40f, 200f),
            AutoGainTargetDb = Math.Clamp(AutoGainTargetDb, -30f, -6f),
            EchoStrength = Math.Clamp(EchoStrength, 0f, 1f),
            MonitorVolume = Math.Clamp(MonitorVolume, 0f, 1f),
            TargetLatencyMilliseconds = Math.Clamp(TargetLatencyMilliseconds, 20, 100),
            Theme = Enum.IsDefined(Theme) ? Theme : AppTheme.System,
            UpdateChannel = Enum.IsDefined(UpdateChannel) ? UpdateChannel : UpdateChannel.Stable,
            ActiveProfileId = activeProfileId,
            Hotkeys = HotkeyBinding.Complete(Hotkeys),
            Profiles = profiles
        };
    }
}

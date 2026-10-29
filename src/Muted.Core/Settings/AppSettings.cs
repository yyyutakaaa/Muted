using Muted.Core.Audio;
using Muted.Core.Dsp;

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

public enum OverlayPosition
{
    TopLeft,
    TopCenter,
    TopRight,
    BottomLeft,
    BottomRight
}

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 6;
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
    public bool VoiceCharacterEnabled { get; init; }
    public float Warmth { get; init; } = 0.6f;
    public float Proximity { get; init; } = 0.5f;
    public float Smoothness { get; init; } = 0.6f;
    public float Compression { get; init; } = 0.5f;
    public float Saturation { get; init; } = 0.3f;
    public bool ShoutGuardEnabled { get; init; }
    public float ShoutThresholdDb { get; init; } = -6f;
    public ShoutGuardMode ShoutGuardMode { get; init; } = ShoutGuardMode.Silence;

    /// <summary>Lets Muted keep the boost and the gate right while you talk.</summary>
    public bool AutoTuneEnabled { get; init; }
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
    public bool SpeakingTrayEnabled { get; init; } = true;
    public bool MuteReminderEnabled { get; init; } = true;
    public bool PushToTalkSoundsEnabled { get; init; }
    public bool HealthWarningsEnabled { get; init; } = true;
    public UpdateChannel UpdateChannel { get; init; } = UpdateChannel.Stable;
    public string? SkippedUpdateVersion { get; init; }
    public string? ActiveProfileId { get; init; }
    public IReadOnlyList<HotkeyBinding> Hotkeys { get; init; } = HotkeyBinding.CreateDefaults();
    public IReadOnlyList<AudioProfile> Profiles { get; init; } = [];

    /// <summary>
    /// True once the first-start guide was finished or skipped. Settings from before
    /// the guide existed count as set up when they already point at an output.
    /// </summary>
    public bool SetupCompleted { get; init; }

    /// <summary>A small always-on-top dot that shows mute and speaking, also over full-screen games.</summary>
    public bool OverlayEnabled { get; init; }
    public OverlayPosition OverlayPosition { get; init; } = OverlayPosition.TopCenter;

    /// <summary>Applies a profile while a listed program runs.</summary>
    public bool AutoProfileEnabled { get; init; }
    public IReadOnlyList<AutoProfileRule> AutoProfileRules { get; init; } = [];

    public LifetimeStatistics Lifetime { get; init; } = new();

    /// <summary>
    /// Cleans what you hear: a second pipeline from a capture device (usually the
    /// recording side of a second cable your call app plays into) to your headphones.
    /// </summary>
    public bool ListeningEnabled { get; init; }
    public string? ListeningInputDeviceId { get; init; }
    public string? ListeningOutputDeviceId { get; init; }
    public float ListeningStrength { get; init; } = 1f;

    public AppSettings Normalize()
    {
        var profiles = (Profiles ?? [])
            .Where(profile => profile is not null)
            .Select(profile => (SchemaVersion < 5 ? profile with
            {
                EchoCancellationEnabled = EchoCancellationEnabled,
                EchoReferenceDeviceId = EchoReferenceDeviceId,
                EchoStrength = EchoStrength
            } : profile).Normalize())
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
                AutoGainTargetDb = AutoGainTargetDb,
                EchoCancellationEnabled = EchoCancellationEnabled,
                EchoReferenceDeviceId = EchoReferenceDeviceId,
                EchoStrength = EchoStrength,
                VoiceCharacterEnabled = VoiceCharacterEnabled,
                Warmth = Warmth,
                Proximity = Proximity,
                Smoothness = Smoothness,
                Compression = Compression,
                Saturation = Saturation,
                ShoutGuardEnabled = ShoutGuardEnabled,
                ShoutThresholdDb = ShoutThresholdDb,
                ShoutGuardMode = ShoutGuardMode
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

        var profileIds = profiles.Select(profile => profile.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rules = (AutoProfileRules ?? [])
            .Where(rule => rule is not null)
            .Select(rule => rule.Normalize())
            .Where(rule => rule.ProcessName.Length > 0 && profileIds.Contains(rule.ProfileId))
            .DistinctBy(rule => rule.ProcessName, StringComparer.Ordinal)
            .Take(AutoProfileRule.MaximumRules)
            .ToArray();

        return this with
        {
            SchemaVersion = CurrentSchemaVersion,
            SetupCompleted = SetupCompleted || (SchemaVersion < 6 && !string.IsNullOrWhiteSpace(OutputDeviceId)),
            OverlayPosition = Enum.IsDefined(OverlayPosition) ? OverlayPosition : OverlayPosition.TopCenter,
            AutoProfileRules = rules,
            Lifetime = (Lifetime ?? new LifetimeStatistics()).Normalize(),
            ListeningStrength = float.IsFinite(ListeningStrength) ? Math.Clamp(ListeningStrength, 0f, 1f) : 1f,
            UseSystemAccentColor = useSystemAccentColor,
            WetMix = Math.Clamp(WetMix, 0f, 1f),
            VoiceThreshold = Math.Clamp(VoiceThreshold, 0.05f, 0.99f),
            VoiceHoldMilliseconds = Math.Clamp(VoiceHoldMilliseconds, 0, 2_000),
            InputGain = Math.Clamp(InputGain, 0.25f, 4f),
            OutputGain = Math.Clamp(OutputGain, 0.25f, 4f),
            HighPassFrequency = Math.Clamp(HighPassFrequency, 40f, 200f),
            AutoGainTargetDb = Math.Clamp(AutoGainTargetDb, -30f, -6f),
            EchoStrength = Math.Clamp(EchoStrength, 0f, 1f),
            Warmth = Amount(Warmth),
            Proximity = Amount(Proximity),
            Smoothness = Amount(Smoothness),
            Compression = Amount(Compression),
            Saturation = Amount(Saturation),
            ShoutThresholdDb = float.IsFinite(ShoutThresholdDb)
                ? Math.Clamp(ShoutThresholdDb, ShoutGuard.MinimumThresholdDb, ShoutGuard.MaximumThresholdDb)
                : -6f,
            ShoutGuardMode = Enum.IsDefined(ShoutGuardMode) ? ShoutGuardMode : ShoutGuardMode.Silence,
            MonitorVolume = Math.Clamp(MonitorVolume, 0f, 1f),
            TargetLatencyMilliseconds = Math.Clamp(TargetLatencyMilliseconds, 20, 100),
            Theme = Enum.IsDefined(Theme) ? Theme : AppTheme.System,
            UpdateChannel = Enum.IsDefined(UpdateChannel) ? UpdateChannel : UpdateChannel.Stable,
            ActiveProfileId = activeProfileId,
            Hotkeys = HotkeyBinding.Complete(Hotkeys),
            Profiles = profiles
        };
    }

    private static float Amount(float value) => float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;
}

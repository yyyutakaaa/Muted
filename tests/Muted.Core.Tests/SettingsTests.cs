using Muted.Core.Audio;
using Muted.Core.Settings;

namespace Muted.Core.Tests;

public sealed class SettingsTests
{
    [Fact]
    public void StudioVoice_IsOffByDefaultAndSurvivesSaving()
    {
        Assert.False(new AppSettings().StudioVoice);
        Assert.False(new SuppressionOptions().StudioVoice);

        var json = System.Text.Json.JsonSerializer.Serialize(new AppSettings { StudioVoice = true });
        var loaded = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!.Normalize();

        Assert.True(loaded.StudioVoice);
        Assert.True(new SuppressionOptions(StudioVoice: true).Normalize().StudioVoice);
    }

    [Fact]
    public void StudioModel_SurvivesSavingAndDefaultsToTheLightestModel()
    {
        Assert.Equal(StudioModel.DeepFilterNet3, new AppSettings().StudioModel);
        Assert.Equal(StudioModel.DeepFilterNet3, new SuppressionOptions().Normalize().StudioModel);

        var json = System.Text.Json.JsonSerializer.Serialize(new AppSettings
        {
            StudioVoice = true,
            StudioModel = StudioModel.DpdfNet8
        });
        var loaded = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!.Normalize();

        Assert.Equal(StudioModel.DpdfNet8, loaded.StudioModel);
        Assert.Equal(StudioModel.DpdfNet8, new SuppressionOptions(StudioModel: StudioModel.DpdfNet8).Normalize().StudioModel);
    }

    [Fact]
    public void UnknownModelFallsBackInsteadOfThrowing()
    {
        Assert.Equal(StudioModel.DeepFilterNet3, new AppSettings { StudioModel = (StudioModel)99 }.Normalize().StudioModel);
    }

    [Fact]
    public void SettingsFromTheRemovedTuningSwitchStillLoad()
    {
        // An earlier build saved a TuningMode; it is gone now and must simply be ignored.
        var loaded = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            "{\"StudioVoice\":true,\"StudioModel\":1,\"TuningMode\":1,\"InputGain\":1.2}")!.Normalize();

        Assert.True(loaded.StudioVoice);
        Assert.Equal(StudioModel.DpdfNet2, loaded.StudioModel);
        Assert.Equal(1.2f, loaded.InputGain);
    }

    private static AudioProfile Profile(string id, string name, float threshold = 0.42f) =>
        new() { Id = id, Name = name, VoiceThreshold = threshold };

    [Fact]
    public void Normalize_KeepsOneProfilePerName()
    {
        // The saved file of a real user: the defaults plus the preset buttons.
        var settings = new AppSettings
        {
            ActiveProfileId = "preset-natural",
            Profiles =
            [
                Profile("balanced", "Balanced"),
                Profile("meeting", "Meeting"),
                Profile("gaming", "Gaming"),
                Profile("preset-meeting", "Meeting", 0.5f),
                Profile("preset-natural", "Natural"),
                Profile("preset-gaming", "gaming", 0.7f)
            ]
        }.Normalize();

        Assert.Equal(["Balanced", "Meeting", "Gaming", "Natural"], settings.Profiles.Select(p => p.Name.Trim()).Select(n => char.ToUpper(n[0]) + n[1..].ToLowerInvariant()).ToArray());
        Assert.Equal(settings.Profiles.Count, settings.Profiles.Select(p => p.Name.ToLowerInvariant()).Distinct().Count());

        // Nobody picked the one in use, so the preset profile wins over the plain default.
        Assert.Equal(0.5f, settings.Profiles.Single(p => p.Name == "Meeting").VoiceThreshold);
        Assert.Equal("preset-natural", settings.ActiveProfileId);
    }

    [Fact]
    public void Normalize_KeepsTheProfileThatIsInUse_AndFollowsReferencesToTheOneThatWasDropped()
    {
        var settings = new AppSettings
        {
            ActiveProfileId = "meeting",
            AutoProfileEnabled = true,
            AutoProfileRules =
            [
                new AutoProfileRule { ProcessName = "teams", ProfileId = "preset-meeting" },
                new AutoProfileRule { ProcessName = "zoom", ProfileId = "meeting" }
            ],
            Profiles = [Profile("meeting", "Meeting", 0.3f), Profile("preset-meeting", "Meeting", 0.5f), Profile("gaming", "Gaming")]
        }.Normalize();

        // What you are hearing now does not change.
        var meeting = settings.Profiles.Single(p => p.Name == "Meeting");
        Assert.Equal("meeting", meeting.Id);
        Assert.Equal(0.3f, meeting.VoiceThreshold);
        Assert.Equal("meeting", settings.ActiveProfileId);

        // A rule that pointed at the dropped profile now points at the one that stayed.
        Assert.All(settings.AutoProfileRules, rule => Assert.Equal("meeting", rule.ProfileId));
        Assert.Equal(2, settings.AutoProfileRules.Count);
    }

    [Fact]
    public void Normalize_FollowsTheActiveProfileWhenItWasTheDroppedOne()
    {
        var settings = new AppSettings
        {
            ActiveProfileId = "gaming",
            Profiles = [Profile("balanced", "Balanced"), Profile("gaming", "Gaming"), Profile("preset-gaming", "Gaming", 0.7f)]
        }.Normalize();

        // The active one is the one that stays, because you were using it.
        Assert.Equal("gaming", settings.ActiveProfileId);
        Assert.DoesNotContain(settings.Profiles, p => p.Id == "preset-gaming");
    }

    [Fact]
    public void Normalize_LeavesDistinctProfilesAlone()
    {
        var profiles = AudioProfile.CreateDefaults();
        var settings = new AppSettings { Profiles = profiles }.Normalize();

        Assert.Equal(profiles.Select(p => p.Id), settings.Profiles.Select(p => p.Id));
    }

    [Fact]
    public void Normalize_ClampsUnsafeValues()
    {
        var settings = new AppSettings
        {
            SchemaVersion = -1,
            WetMix = 5,
            VoiceThreshold = -2,
            VoiceHoldMilliseconds = 99_000,
            TargetLatencyMilliseconds = 1
        };

        var normalized = settings.Normalize();

        Assert.Equal(AppSettings.CurrentSchemaVersion, normalized.SchemaVersion);
        Assert.Equal(1f, normalized.WetMix);
        Assert.Equal(0.05f, normalized.VoiceThreshold);
        Assert.Equal(2_000, normalized.VoiceHoldMilliseconds);
        Assert.Equal(20, normalized.TargetLatencyMilliseconds);
    }

    [Fact]
    public void Normalize_MigratesLegacySettingsIntoProfiles()
    {
        var settings = new AppSettings
        {
            InputDeviceId = "microphone",
            OutputDeviceId = "cable",
            SuppressionEnabled = false,
            WetMix = 0.35f,
            VoiceGateEnabled = false,
            VoiceThreshold = 0.72f,
            VoiceHoldMilliseconds = 600
        };

        var normalized = settings.Normalize();

        Assert.Equal(3, normalized.Profiles.Count);
        Assert.Equal("balanced", normalized.ActiveProfileId);
        var current = normalized.Profiles[0];
        Assert.Equal("microphone", current.InputDeviceId);
        Assert.Equal("cable", current.OutputDeviceId);
        Assert.False(current.SuppressionEnabled);
        Assert.Equal(0.35f, current.WetMix);
        Assert.False(current.VoiceGateEnabled);
        Assert.Equal(0.72f, current.VoiceThreshold);
        Assert.Equal(600, current.VoiceHoldMilliseconds);
    }

    [Fact]
    public void Normalize_ClampsAndDeduplicatesProfiles()
    {
        var settings = new AppSettings
        {
            ActiveProfileId = "same",
            Profiles =
            [
                new AudioProfile
                {
                    Id = "same",
                    Name = "  My profile  ",
                    WetMix = -2,
                    VoiceThreshold = 8,
                    VoiceHoldMilliseconds = -10
                },
                new AudioProfile { Id = "same", Name = "Duplicate" }
            ]
        };

        var normalized = settings.Normalize();

        var profile = Assert.Single(normalized.Profiles);
        Assert.Equal("My profile", profile.Name);
        Assert.Equal(0f, profile.WetMix);
        Assert.Equal(0.99f, profile.VoiceThreshold);
        Assert.Equal(0, profile.VoiceHoldMilliseconds);
        Assert.Equal("same", normalized.ActiveProfileId);
    }

    [Fact]
    public void Normalize_ClampsTheProcessingChain()
    {
        var settings = new AppSettings
        {
            InputGain = 99,
            OutputGain = 0,
            HighPassFrequency = 5_000,
            AutoGainTargetDb = 40,
            MonitorVolume = 3,
            Theme = (AppTheme)42,
            UpdateChannel = (UpdateChannel)9
        };

        var normalized = settings.Normalize();

        Assert.Equal(4f, normalized.InputGain);
        Assert.Equal(0.25f, normalized.OutputGain);
        Assert.Equal(200f, normalized.HighPassFrequency);
        Assert.Equal(-6f, normalized.AutoGainTargetDb);
        Assert.Equal(1f, normalized.MonitorVolume);
        Assert.Equal(AppTheme.System, normalized.Theme);
        Assert.Equal(UpdateChannel.Stable, normalized.UpdateChannel);
    }

    [Fact]
    public void Normalize_ClearsTheWindowsAccentOverrideFromOlderSettings()
    {
        var older = new AppSettings { SchemaVersion = 3, UseSystemAccentColor = true };
        var current = new AppSettings { UseSystemAccentColor = true };

        Assert.False(older.Normalize().UseSystemAccentColor);
        Assert.True(current.Normalize().UseSystemAccentColor);
    }

    [Fact]
    public void Normalize_CarriesTheProcessingChainIntoTheFirstProfile()
    {
        var settings = new AppSettings
        {
            InputGain = 2f,
            HighPassEnabled = true,
            HighPassFrequency = 120f,
            LimiterEnabled = false,
            AutoGainEnabled = true,
            AutoGainTargetDb = -12f
        };

        var profile = settings.Normalize().Profiles[0];

        Assert.Equal(2f, profile.InputGain);
        Assert.True(profile.HighPassEnabled);
        Assert.Equal(120f, profile.HighPassFrequency);
        Assert.False(profile.LimiterEnabled);
        Assert.True(profile.AutoGainEnabled);
        Assert.Equal(-12f, profile.AutoGainTargetDb);
    }

    [Fact]
    public void Normalize_FillsInEveryShortcut()
    {
        var settings = new AppSettings
        {
            Hotkeys =
            [
                new HotkeyBinding
                {
                    Action = HotkeyAction.PushToTalk,
                    VirtualKey = 118,
                    Modifiers = HotkeyModifiers.Control,
                    Enabled = true
                }
            ]
        };

        var normalized = settings.Normalize();

        Assert.Equal(Enum.GetValues<HotkeyAction>().Length, normalized.Hotkeys.Count);
        var pushToTalk = normalized.Hotkeys.Single(binding => binding.Action == HotkeyAction.PushToTalk);
        Assert.True(pushToTalk.IsAssigned);
        Assert.True(pushToTalk.IsHold);
        Assert.All(
            normalized.Hotkeys.Where(binding => binding.Action != HotkeyAction.PushToTalk),
            binding => Assert.False(binding.IsAssigned));
    }

    [Fact]
    public void Hotkey_WithoutAKeyCannotBeEnabled()
    {
        var binding = new HotkeyBinding
        {
            Action = HotkeyAction.ToggleMute,
            VirtualKey = 0,
            Enabled = true
        }.Normalize();

        Assert.False(binding.Enabled);
        Assert.False(binding.IsAssigned);
    }

    [Fact]
    public void Monitor_WithoutADeviceIsNotActive()
    {
        var monitor = new MonitorOptions(Enabled: true, DeviceId: null).Normalize();

        Assert.False(monitor.IsActive);
    }
}

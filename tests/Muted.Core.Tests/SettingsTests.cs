using Muted.Core.Audio;
using Muted.Core.Settings;

namespace Muted.Core.Tests;

public sealed class SettingsTests
{
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
        Assert.Equal(AppTheme.Dark, normalized.Theme);
        Assert.Equal(UpdateChannel.Stable, normalized.UpdateChannel);
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

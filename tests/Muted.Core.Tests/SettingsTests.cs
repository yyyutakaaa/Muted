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
    public void Profile_CreatesMutedSuppressionOptions()
    {
        var profile = new AudioProfile
        {
            SuppressionEnabled = false,
            WetMix = 0.4f,
            VoiceGateEnabled = false
        };

        var options = profile.ToSuppressionOptions(isMuted: true);

        Assert.False(options.Enabled);
        Assert.Equal(0.4f, options.WetMix);
        Assert.False(options.VoiceGateEnabled);
        Assert.True(options.IsMuted);
    }
}

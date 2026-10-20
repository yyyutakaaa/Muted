using Muted.Core.Audio;
using Muted.Core.Dsp;
using Muted.Core.Settings;

namespace Muted.Core.Tests;

public sealed class VoiceSettingsTests
{
    [Fact]
    public void SuppressionNormalize_FillsInDisabledVoiceStages()
    {
        var normalized = new SuppressionOptions().Normalize();

        Assert.NotNull(normalized.Character);
        Assert.False(normalized.Character!.Enabled);
        Assert.NotNull(normalized.ShoutGuard);
        Assert.False(normalized.ShoutGuard!.Enabled);
    }

    [Fact]
    public void VoiceOptionsNormalize_ClampsAmountsAndThreshold()
    {
        var character = new VoiceCharacterOptions(true, 2f, -1f, float.NaN, 0.5f, 9f).Normalize();
        var shout = new ShoutGuardOptions(true, -90f, (ShoutGuardMode)42).Normalize();

        Assert.Equal(1f, character.Warmth);
        Assert.Equal(0f, character.Proximity);
        Assert.Equal(0f, character.Smoothness);
        Assert.Equal(0.5f, character.Compression);
        Assert.Equal(1f, character.Saturation);
        Assert.Equal(ShoutGuard.MinimumThresholdDb, shout.ThresholdDb);
        Assert.Equal(ShoutGuardMode.Silence, shout.Mode);
    }

    [Fact]
    public void ProfileNormalize_ClampsVoiceFields()
    {
        var profile = new AudioProfile
        {
            Warmth = 3f,
            Saturation = float.PositiveInfinity,
            ShoutThresholdDb = 12f,
            ShoutGuardMode = (ShoutGuardMode)7
        }.Normalize();

        Assert.Equal(1f, profile.Warmth);
        Assert.Equal(0f, profile.Saturation);
        Assert.Equal(ShoutGuard.MaximumThresholdDb, profile.ShoutThresholdDb);
        Assert.Equal(ShoutGuardMode.Silence, profile.ShoutGuardMode);
    }

    [Fact]
    public void LegacySettings_CarryVoiceFieldsIntoFirstProfile()
    {
        var settings = new AppSettings
        {
            VoiceCharacterEnabled = true,
            Warmth = 0.8f,
            ShoutGuardEnabled = true,
            ShoutThresholdDb = -10f,
            ShoutGuardMode = ShoutGuardMode.Duck
        }.Normalize();

        var first = settings.Profiles[0];
        Assert.True(first.VoiceCharacterEnabled);
        Assert.Equal(0.8f, first.Warmth);
        Assert.True(first.ShoutGuardEnabled);
        Assert.Equal(-10f, first.ShoutThresholdDb);
        Assert.Equal(ShoutGuardMode.Duck, first.ShoutGuardMode);
    }

    [Fact]
    public void ProfileExchange_RoundTripsVoiceFields()
    {
        var profile = new AudioProfile
        {
            Name = "Warm",
            VoiceCharacterEnabled = true,
            Warmth = 0.75f,
            Proximity = 0.7f,
            Smoothness = 0.7f,
            Compression = 0.6f,
            Saturation = 0.35f,
            ShoutGuardEnabled = true,
            ShoutThresholdDb = -8f,
            ShoutGuardMode = ShoutGuardMode.Duck
        };

        var imported = ProfileExchange.Import(ProfileExchange.Export([profile])).Single();

        Assert.True(imported.VoiceCharacterEnabled);
        Assert.Equal(0.75f, imported.Warmth);
        Assert.Equal(0.35f, imported.Saturation);
        Assert.True(imported.ShoutGuardEnabled);
        Assert.Equal(-8f, imported.ShoutThresholdDb);
        Assert.Equal(ShoutGuardMode.Duck, imported.ShoutGuardMode);
    }
}

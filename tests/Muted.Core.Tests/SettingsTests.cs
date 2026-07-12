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
}

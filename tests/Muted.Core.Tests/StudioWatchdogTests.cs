using Muted.Core.Dsp;

namespace Muted.Core.Tests;

public sealed class StudioWatchdogTests
{
    [Fact]
    public void ResetsAfterTwoSecondsOfUncleanedQuiet()
    {
        var watchdog = new StudioWatchdog();
        for (var frame = 1; frame < StudioWatchdog.FramesBeforeReset; frame++)
        {
            Assert.False(watchdog.Observe(0.002f, 0.002f, 0.05f));
        }

        Assert.True(watchdog.Observe(0.002f, 0.002f, 0.05f));
        Assert.Equal(1, watchdog.Resets);
    }

    [Fact]
    public void HealthyCleaningNeverResets()
    {
        var watchdog = new StudioWatchdog();
        for (var frame = 0; frame < 5_000; frame++)
        {
            Assert.False(watchdog.Observe(0.002f, 0.0001f, 0.05f));
        }
    }

    [Fact]
    public void SpeechAndNearSilenceAreIgnored()
    {
        var watchdog = new StudioWatchdog();
        for (var frame = 0; frame < 5_000; frame++)
        {
            Assert.False(watchdog.Observe(0.05f, 0.05f, 0.9f));
            Assert.False(watchdog.Observe(0.0001f, 0.0001f, 0.05f));
        }
    }

    [Fact]
    public void WaitsLongerBeforeRepeatingAResetThatDidNotHelp()
    {
        var watchdog = new StudioWatchdog();
        var resets = 0;
        for (var frame = 0; frame < StudioWatchdog.FramesBeforeReset + StudioWatchdog.FramesBeforeRepeatReset - 1; frame++)
        {
            resets += watchdog.Observe(0.002f, 0.002f, 0.05f) ? 1 : 0;
        }

        Assert.Equal(1, resets);
        Assert.True(watchdog.Observe(0.002f, 0.002f, 0.05f));
    }
}

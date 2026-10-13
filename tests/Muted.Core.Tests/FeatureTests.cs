using System.Text.Json;
using Muted.Core.Audio;
using Muted.Core.Settings;

namespace Muted.Core.Tests;

public sealed class FeatureTests
{
    [Fact]
    public void MuteReminderRequiresSustainedManualMuteAndHasCooldown()
    {
        var tracker = new AudioFeedbackTracker();
        tracker.Start(0);
        var metrics = AudioMetrics.Empty with { VoiceProbability = .9f, InputPeak = .2f };
        // Armed push-to-talk is effectively muted, but not a forgotten manual mute.
        for (var i = 1; i <= 20; i++)
            Assert.False(tracker.Update(i * .25, metrics, false, true, true, true).RemindMute);
        var reminders = 0;
        for (var i = 21; i <= 200; i++)
            if (tracker.Update(i * .25, metrics, true, true, true, true).RemindMute) reminders++;
        Assert.Equal(1, reminders);
        Assert.False(tracker.Update(51, metrics, true, true, false, true).RemindMute);
    }

    [Fact]
    public void HealthWarningIgnoresStartupAndRecoversWithoutRepeatedNotifications()
    {
        var tracker = new AudioFeedbackTracker();
        tracker.Start(0);
        var highLoad = AudioMetrics.Empty with { ProcessingLoad = .95f };
        for (var i = 1; i < 8; i++)
            Assert.False(tracker.Update(i * .25, highLoad, false, false, true, true).Unhealthy);
        var alerts = 0;
        AudioFeedback last = default;
        for (var i = 8; i <= 40; i++)
        {
            last = tracker.Update(i * .25, highLoad, false, false, true, true);
            if (last.NotifyHealth) alerts++;
        }
        Assert.True(last.Unhealthy);
        Assert.Equal(1, alerts);
        Assert.False(tracker.Update(10.25, AudioMetrics.Empty, false, false, true, true).Unhealthy);
    }

    [Fact]
    public void DropoutWarningUsesNewDropsRatherThanLifetimeCounter()
    {
        var tracker = new AudioFeedbackTracker();
        tracker.Start(0);
        var dropped = AudioMetrics.Empty with { OutputUnderrunSamples = 480 };
        tracker.Update(3, dropped, false, false, true, true);
        var result = tracker.Update(3.5, dropped, false, false, true, true);
        Assert.True(result.Unhealthy);
        Assert.False(tracker.Update(6, dropped, false, false, true, true).Unhealthy);
    }

    [Fact]
    public void LegacyProfilesRetainGlobalEchoSettingsDuringMigration()
    {
        var settings = new AppSettings { SchemaVersion = 4, EchoCancellationEnabled = true,
            EchoReferenceDeviceId = "speakers", EchoStrength = .7f,
            Profiles = [new AudioProfile { Id = "custom", Name = "Custom" }] };
        var normalized = settings.Normalize();
        Assert.True(normalized.Profiles[0].EchoCancellationEnabled);
        Assert.Equal("speakers", normalized.Profiles[0].EchoReferenceDeviceId);
        Assert.Equal(.7f, normalized.Profiles[0].EchoStrength);
        Assert.Equal(normalized.Profiles[0], normalized.Normalize().Profiles[0]);
        Assert.False(normalized.PushToTalkSoundsEnabled);
    }

    [Fact]
    public void ExchangePreservesAudioButRemovesDeviceIdsAndRekeysImports()
    {
        var profile = new AudioProfile { Id = "original", Name = "Custom", InputDeviceId = "private-input",
            OutputDeviceId = "private-output", EchoReferenceDeviceId = "private-reference",
            EchoCancellationEnabled = true, EchoStrength = .8f, InputGain = 2 };
        var json = ProfileExchange.Export([profile]);
        Assert.DoesNotContain("private-", json);
        var imported = Assert.Single(ProfileExchange.Import(json));
        Assert.NotEqual(profile.Id, imported.Id);
        Assert.Equal(2, imported.InputGain);
        Assert.True(imported.EchoCancellationEnabled);
        Assert.Equal(.8f, imported.EchoStrength);
        Assert.Null(imported.InputDeviceId);
        Assert.NotEqual(imported.Id, ProfileExchange.Import(json)[0].Id);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"Version\":2,\"Profiles\":[]}")]
    [InlineData("{\"Version\":1,\"Profiles\":[null]}")]
    [InlineData("{\"Version\":1,\"Profiles\":[]}")]
    public void InvalidProfilePackagesAreRejected(string json) =>
        Assert.Throws<InvalidDataException>(() => ProfileExchange.Import(json));

    [Fact]
    public void ImportedValuesAreClampedBeforeUse()
    {
        var json = JsonSerializer.Serialize(new ProfileExchange(1,
            [new AudioProfile { InputGain = 999, EchoStrength = -2 }]));
        var profile = ProfileExchange.Import(json)[0];
        Assert.Equal(4, profile.InputGain);
        Assert.Equal(0, profile.EchoStrength);
    }

    [Fact]
    public void RecordingIsBoundedAndSealedAgainstLateFrames()
    {
        var clip = new AudioClipBuffer(1);
        for (var i = 0; i < 101; i++) clip.Write(Enumerable.Repeat(.25f, 480).ToArray());
        Assert.True(clip.Complete);
        var samples = clip.Finish();
        Assert.Equal(48_000, samples.Length);
        Assert.All(samples, sample => Assert.Equal(.25f, sample));
        clip.Write(new float[480]);
        Assert.Equal(samples, clip.Finish());
    }

    [Fact]
    public void PartialRecordingContainsOnlyCapturedSamples()
    {
        var clip = new AudioClipBuffer(10);
        clip.Write(new float[] { .1f, .2f });
        Assert.False(clip.Complete);
        Assert.Equal(new float[] { .1f, .2f }, clip.Finish());
        clip.Write(new float[] { .3f });
        Assert.Equal(2, clip.Finish().Length);
    }

    [Fact]
    public void RecordingSupportsTwoMinuteManualLimit()
    {
        var clip = new AudioClipBuffer(120);
        Assert.False(clip.Complete);
        clip.Write(new float[480]);
        Assert.Equal(480, clip.Finish().Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioClipBuffer(121));
    }

    [Fact]
    public void SessionCountsSpeechAndSeparateLimiterPeriods()
    {
        var session = new SessionAccumulator();
        session.Add(.9f, false, 10, 2, .8f);
        session.Add(.9f, true, 20, 3, .7f);
        session.Add(.1f, false, 0, 0, .4f);
        session.Add(.9f, false, 10, 1, .9f);
        var stats = session.Snapshot();
        Assert.Equal(.04, stats.Seconds);
        Assert.Equal(.02, stats.SpeakingSeconds);
        Assert.Equal(2, stats.LimiterEvents);
        Assert.Equal(10, stats.AverageNoiseReductionDb);
        Assert.Equal(.9f, stats.PeakInput);
    }
}

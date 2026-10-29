using Muted.Core.Audio;
using Muted.Core.Settings;
using Muted.Core.Support;

namespace Muted.Core.Tests;

public sealed class GrowthFeatureTests
{
    [Theory]
    [InlineData(@"C:\Riot Games\VALORANT\VALORANT.exe", "valorant")]
    [InlineData("Discord.EXE", "discord")]
    [InlineData("  ms-teams  ", "ms-teams")]
    [InlineData("\"obs64.exe\"", "obs64")]
    [InlineData("", "")]
    public void ProcessNames_AreComparedWithoutPathCaseOrExtension(string input, string expected) =>
        Assert.Equal(expected, AutoProfileRule.NormalizeProcessName(input));

    [Fact]
    public void AutoProfile_FirstMatchingRuleWins()
    {
        AutoProfileRule[] rules =
        [
            new() { ProcessName = "valorant.exe", ProfileId = "gaming" },
            new() { ProcessName = "Teams", ProfileId = "meeting" }
        ];

        Assert.Equal("gaming", AutoProfileRule.Match(rules, ["explorer", "ms-teams", "teams", "VALORANT"]));
        Assert.Equal("meeting", AutoProfileRule.Match(rules, ["Teams"]));
        Assert.Null(AutoProfileRule.Match(rules, ["notepad"]));
    }

    [Fact]
    public void Normalize_DropsRulesForMissingProfilesAndDuplicates()
    {
        var profile = new AudioProfile { Id = "p1", Name = "Gaming" };
        var settings = new AppSettings
        {
            Profiles = [profile],
            AutoProfileRules =
            [
                new() { ProcessName = "Game.exe", ProfileId = "p1" },
                new() { ProcessName = "game", ProfileId = "p1" },
                new() { ProcessName = "other", ProfileId = "missing" },
                new() { ProcessName = " ", ProfileId = "p1" }
            ]
        }.Normalize();

        var rule = Assert.Single(settings.AutoProfileRules);
        Assert.Equal("game", rule.ProcessName);
    }

    [Fact]
    public void Normalize_TreatsOlderConfiguredSettingsAsSetUp()
    {
        Assert.True(new AppSettings { SchemaVersion = 5, OutputDeviceId = "cable" }.Normalize().SetupCompleted);
        Assert.False(new AppSettings { SchemaVersion = 5 }.Normalize().SetupCompleted);
        Assert.False(new AppSettings().Normalize().SetupCompleted);
        Assert.False(new AppSettings { OutputDeviceId = "cable" }.Normalize().SetupCompleted);
    }

    [Fact]
    public void Lifetime_AddsOnlyTheNewPartOfARun()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var lifetime = new LifetimeStatistics();
        var first = new SessionStatistics(60, 20, 0, 10, 0.5f);
        lifetime = lifetime.Add(first, default, newSession: true, now);
        var later = new SessionStatistics(120, 50, 0, 10, 0.5f);
        lifetime = lifetime.Add(later, first, newSession: false, now.AddMinutes(1));

        Assert.Equal(120, lifetime.Seconds, 6);
        Assert.Equal(50, lifetime.SpeakingSeconds, 6);
        Assert.Equal(1, lifetime.Sessions);
        Assert.Equal(10, lifetime.AverageNoiseReductionDb, 6);
        Assert.Equal(now, lifetime.Since);
    }

    [Fact]
    public void IssueLink_PrefillsTheBugFormAndStaysShort()
    {
        var link = IssueLink.Build("Microphone: Maono\nCable: CABLE Input & more", "0.6.0");
        Assert.StartsWith("https://github.com/yyyutakaaa/Muted/issues/new?template=bug_report.yml", link);
        Assert.Contains("version=0.6.0", link);
        Assert.Contains("diagnostics=Microphone%3A%20Maono%0ACable%3A%20CABLE%20Input%20%26%20more", link);

        var huge = IssueLink.Build(new string('é', 50_000), "0.6.0");
        Assert.True(huge.Length < 8_000, $"Link was {huge.Length} characters.");
        Assert.Contains(Uri.EscapeDataString("report shortened"), huge);
    }

    [Theory]
    [InlineData(new[] { "--toggle-mute" }, RemoteCommand.ToggleMute, null)]
    [InlineData(new[] { "--minimized", "--mute" }, RemoteCommand.Mute, null)]
    [InlineData(new[] { "/unmute" }, RemoteCommand.Unmute, null)]
    [InlineData(new[] { "--profile", "Warm Radio" }, RemoteCommand.Profile, "Warm Radio")]
    [InlineData(new[] { "--profile=Gaming" }, RemoteCommand.Profile, "Gaming")]
    [InlineData(new[] { "--toggle-listening" }, RemoteCommand.ToggleListening, null)]
    public void CommandLine_ReadsCommands(string[] arguments, RemoteCommand command, string? argument)
    {
        var request = CommandLine.Parse(arguments);
        Assert.NotNull(request);
        Assert.Equal(command, request.Value.Command);
        Assert.Equal(argument, request.Value.Argument);
        Assert.Equal(request, RemoteRequest.Parse(request.Value.Serialize()));
    }

    [Fact]
    public void CommandLine_IgnoresAnythingElse()
    {
        Assert.Null(CommandLine.Parse([]));
        Assert.Null(CommandLine.Parse(["--minimized"]));
        Assert.Null(CommandLine.Parse(["--profile"]));
        Assert.Null(CommandLine.Parse(["toggle-mute"]));
    }

    [Fact]
    public void RemoteRequest_RejectsUnknownLines()
    {
        Assert.Null(RemoteRequest.Parse("format c:"));
        Assert.Null(RemoteRequest.Parse(""));
        Assert.Null(RemoteRequest.Parse("profile " + new string('x', 200)));
    }

    [Fact]
    public void ComparisonClip_PutsBeforeThenGapThenAfter()
    {
        var clip = ComparisonClip.Build([1f, 1f], [2f], gapSeconds: 0.001);
        Assert.Equal(2 + 48 + 1, clip.Length);
        Assert.Equal(1f, clip[1]);
        Assert.Equal(0f, clip[2]);
        Assert.Equal(2f, clip[^1]);
    }
}

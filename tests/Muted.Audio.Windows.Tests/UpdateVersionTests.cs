using Muted.App.Services;

namespace Muted.Audio.Windows.Tests;

public sealed class UpdateVersionTests
{
    private static int Compare(string first, string second)
    {
        Assert.True(UpdateService.TryParseVersion(first, out var firstVersion));
        Assert.True(UpdateService.TryParseVersion(second, out var secondVersion));
        return Math.Sign(UpdateService.CompareReleases(
            firstVersion, UpdateService.ReleaseSuffix(first),
            secondVersion, UpdateService.ReleaseSuffix(second)));
    }

    [Theory]
    [InlineData("v0.10.0-beta2", "v0.10.0-beta1")]
    [InlineData("v0.10.0-beta10", "v0.10.0-beta9")]
    [InlineData("v0.10.0", "v0.10.0-beta2")]
    [InlineData("v0.10.0-rc1", "v0.10.0-beta3")]
    [InlineData("v0.10.1-beta1", "v0.10.0")]
    [InlineData("v0.10.0-beta1", "v0.9.1")]
    public void ANewerReleaseComesAfter(string newer, string older)
    {
        Assert.Equal(1, Compare(newer, older));
        Assert.Equal(-1, Compare(older, newer));
    }

    [Theory]
    [InlineData("v0.10.0-beta2", "0.10.0-beta2")]
    [InlineData("v0.10.0", "0.10.0")]
    public void TheSameReleaseIsNotAnUpdate(string first, string second) => Assert.Equal(0, Compare(first, second));

    [Theory]
    [InlineData("v0.10.0-beta2", "beta2")]
    [InlineData("0.10.0-beta2+24c93f6", "beta2")]
    [InlineData("v0.10.0", "")]
    [InlineData(null, "")]
    public void ReadsTheSuffix(string? tag, string suffix) => Assert.Equal(suffix, UpdateService.ReleaseSuffix(tag));

    [Fact]
    public void NamesAReleaseTheWayPeopleReadIt()
    {
        Assert.Equal("0.10.0 beta2", UpdateService.ReleaseName(new Version(0, 10, 0), "beta2"));
        Assert.Equal("0.10.0", UpdateService.ReleaseName(new Version(0, 10, 0), ""));
    }
}

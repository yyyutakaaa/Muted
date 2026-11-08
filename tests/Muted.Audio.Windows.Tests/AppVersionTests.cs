using Muted.App.Infrastructure;

namespace Muted.Audio.Windows.Tests;

public sealed class AppVersionTests
{
    [Theory]
    [InlineData("0.10.0-beta1+c6bd6a61b39d867769c2bc5bf80414b118f42574", "beta1", "c6bd6a6")]
    [InlineData("0.10.0+c6bd6a61b39d867769c2bc5bf80414b118f42574", "", "c6bd6a6")]
    [InlineData("0.10.0-beta1", "beta1", "")]
    [InlineData("0.10.0", "", "")]
    [InlineData(null, "", "")]
    public void ReadsTheChannelAndTheBuildFromTheInformationalVersion(string? informational, string suffix, string build)
    {
        Assert.Equal(suffix, AppVersion.SuffixOf(informational));
        Assert.Equal(build, AppVersion.BuildOf(informational));
    }
}

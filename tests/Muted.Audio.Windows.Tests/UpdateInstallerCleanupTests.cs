using Muted.App.Services;

namespace Muted.Audio.Windows.Tests;

public sealed class UpdateInstallerCleanupTests
{
    [Theory]
    [InlineData("Muted-Setup-0.8.0.exe", "0.8.0")]
    [InlineData("muted-setup-1.2.3.EXE", "1.2.3")]
    [InlineData("Muted-Setup-0.9.0-beta1.exe", "0.9.0")]
    public void InstallerVersionIsReadFromFileName(string fileName, string expected)
    {
        Assert.True(UpdateService.TryParseInstallerVersion(fileName, out var version));
        Assert.Equal(Version.Parse(expected), version);
    }

    [Theory]
    [InlineData("Muted-Setup-0.8.0.exe.download")]
    [InlineData("Muted-Setup-latest.exe")]
    [InlineData("Other-Setup-0.8.0.exe")]
    public void NonInstallerNamesAreRejected(string fileName)
    {
        Assert.False(UpdateService.TryParseInstallerVersion(fileName, out _));
    }
}

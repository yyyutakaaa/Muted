using Microsoft.Win32;

namespace Muted.App.Services;

internal sealed class StartupService
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Muted";

    public void SetEnabled(bool enabled, bool startMinimized)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true);

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new InvalidOperationException("Muted's executable path is unavailable.");
        }

        var arguments = startMinimized ? " --minimized" : string.Empty;
        key.SetValue(ValueName, $"\"{executable}\"{arguments}", RegistryValueKind.String);
    }
}

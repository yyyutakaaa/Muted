using System.Diagnostics;
using System.IO;

namespace Muted.App.Services;

internal static class Shell
{
    /// <summary>Opens a URL or folder with the user's default handler.</summary>
    public static bool Open(string target)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch (Exception)
        {
            // A missing browser or file association is not worth an error dialog.
            return false;
        }
    }

    public static bool OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception)
        {
            return false;
        }

        return Open(path);
    }
}

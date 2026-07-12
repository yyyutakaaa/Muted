using System.IO;

namespace Muted.App.Services;

internal sealed class FileLog
{
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly string _path;

    public FileLog()
    {
        _directory = AppPaths.DataDirectory;
        _path = Path.Combine(_directory, "Muted.log");
    }

    public void Write(Exception exception, string context)
    {
        WriteLine($"{context}{Environment.NewLine}{exception}");
    }

    private void WriteLine(string message)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(_directory);
                if (File.Exists(_path) && new FileInfo(_path).Length > 1_048_576)
                {
                    File.Move(_path, _path + ".1", overwrite: true);
                }

                File.AppendAllText(
                    _path,
                    $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take down the audio app.
        }
    }
}

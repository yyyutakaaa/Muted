using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Muted.Core.Audio;

namespace Muted.App.Services;

internal enum TrayIconState
{
    Stopped,
    Starting,
    Running,
    Muted,
    Faulted
}

/// <summary>
/// Draws the tray icon per state so the notification area shows at a glance whether
/// Muted is filtering, muted, or off. Icons are cached; each one owns an HICON.
/// </summary>
internal sealed class TrayIconFactory : IDisposable
{
    private readonly Dictionary<TrayIconState, Icon> _cache = [];
    private readonly List<IntPtr> _handles = [];
    private bool _disposed;

    public static TrayIconState StateFor(AudioEngineState engineState, bool isMuted) => engineState switch
    {
        AudioEngineState.Starting or AudioEngineState.Stopping => TrayIconState.Starting,
        AudioEngineState.Running => isMuted ? TrayIconState.Muted : TrayIconState.Running,
        AudioEngineState.Faulted => TrayIconState.Faulted,
        _ => TrayIconState.Stopped
    };

    public Icon Get(TrayIconState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_cache.TryGetValue(state, out var cached))
        {
            return cached;
        }

        var icon = Render(state);
        _cache[state] = icon;
        return icon;
    }

    private Icon Render(TrayIconState state)
    {
        const int size = 32;
        var color = state switch
        {
            TrayIconState.Running => Color.FromArgb(72, 214, 162),
            TrayIconState.Starting => Color.FromArgb(242, 184, 75),
            TrayIconState.Faulted => Color.FromArgb(255, 107, 122),
            TrayIconState.Muted => Color.FromArgb(150, 155, 170),
            _ => Color.FromArgb(150, 155, 170)
        };

        using var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            // The same five-bar mark as the sidebar logo.
            int[] heights = [10, 18, 26, 15, 8];
            using var brush = new SolidBrush(color);
            var x = 3f;
            foreach (var height in heights)
            {
                var y = (size - height) / 2f;
                graphics.FillRectangle(brush, x, y, 4f, height);
                x += 6f;
            }

            if (state == TrayIconState.Muted)
            {
                using var slash = new Pen(Color.FromArgb(255, 107, 122), 4f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                };
                graphics.DrawLine(slash, 4f, 28f, 28f, 4f);
            }
        }

        var handle = bitmap.GetHicon();
        _handles.Add(handle);
        using var temporary = Icon.FromHandle(handle);
        return (Icon)temporary.Clone();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var icon in _cache.Values)
        {
            icon.Dispose();
        }

        _cache.Clear();
        foreach (var handle in _handles)
        {
            DestroyIcon(handle);
        }

        _handles.Clear();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}

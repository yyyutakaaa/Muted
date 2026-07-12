using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using Muted.Core.Audio;

namespace Muted.App.Services;

internal sealed class TrayService : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.ToolStripMenuItem _toggleItem;
    private readonly Icon _appIcon;

    public TrayService()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        var openItem = menu.Items.Add("Open Muted");
        openItem.Font = new Font(openItem.Font, FontStyle.Bold);
        openItem.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);

        _toggleItem = new System.Windows.Forms.ToolStripMenuItem("Start noise suppression");
        _toggleItem.Click += (_, _) => ToggleRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(_toggleItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        var exitItem = menu.Items.Add("Quit");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        _appIcon = LoadAppIcon();

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "Muted — RNNoise",
            Icon = _appIcon,
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? ToggleRequested;

    public event EventHandler? ExitRequested;

    public void UpdateState(AudioEngineState state)
    {
        _toggleItem.Enabled = state is not (AudioEngineState.Starting or AudioEngineState.Stopping);
        _toggleItem.Text = state switch
        {
            AudioEngineState.Starting => "Starting RNNoise…",
            AudioEngineState.Stopping => "Stopping RNNoise…",
            AudioEngineState.Running => "Stop RNNoise",
            _ => "Start RNNoise"
        };
        _notifyIcon.Text = state switch
        {
            AudioEngineState.Starting => "Muted — starting…",
            AudioEngineState.Stopping => "Muted — stopping…",
            AudioEngineState.Running => "Muted — active",
            AudioEngineState.Faulted => "Muted — audio error",
            _ => "Muted — stopped"
        };
    }

    public void ShowBalloon(string title, string message)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.ShowBalloonTip(3_000);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _appIcon.Dispose();
    }

    private static Icon LoadAppIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "icon.png");
        if (!File.Exists(iconPath))
        {
            return (Icon)SystemIcons.Information.Clone();
        }

        using var source = new Bitmap(iconPath);
        using var bitmap = new Bitmap(source, new Size(64, 64));
        var iconHandle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(iconHandle).Clone();
        }
        finally
        {
            DestroyIcon(iconHandle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}

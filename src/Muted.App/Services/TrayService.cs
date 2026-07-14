using System.Drawing;
using System.IO;
using Muted.Core.Audio;
using Muted.Core.Settings;

namespace Muted.App.Services;

internal sealed class TrayService : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.ToolStripMenuItem _toggleItem;
    private readonly System.Windows.Forms.ToolStripMenuItem _muteItem;
    private readonly System.Windows.Forms.ToolStripMenuItem _suppressionItem;
    private readonly System.Windows.Forms.ToolStripMenuItem _profilesItem;
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

        _muteItem = new System.Windows.Forms.ToolStripMenuItem("Mute microphone");
        _muteItem.Click += (_, _) => MuteRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(_muteItem);

        _suppressionItem = new System.Windows.Forms.ToolStripMenuItem("Noise suppression");
        _suppressionItem.Click += (_, _) => SuppressionToggleRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(_suppressionItem);

        _profilesItem = new System.Windows.Forms.ToolStripMenuItem("Profiles");
        menu.Items.Add(_profilesItem);

        var diagnosticsItem = menu.Items.Add("Run diagnostics…");
        diagnosticsItem.Click += (_, _) => DiagnosticsRequested?.Invoke(this, EventArgs.Empty);

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

    public event EventHandler? MuteRequested;

    public event EventHandler? SuppressionToggleRequested;

    public event EventHandler<ProfileRequestedEventArgs>? ProfileRequested;

    public event EventHandler? DiagnosticsRequested;

    public event EventHandler? ExitRequested;

    public void UpdateState(
        AudioEngineState state,
        bool isMuted,
        bool suppressionEnabled,
        IReadOnlyList<AudioProfile> profiles,
        string? activeProfileId)
    {
        var isBusy = state is AudioEngineState.Starting or AudioEngineState.Stopping;
        _toggleItem.Enabled = !isBusy;
        _toggleItem.Text = state switch
        {
            AudioEngineState.Starting => "Starting RNNoise…",
            AudioEngineState.Stopping => "Stopping RNNoise…",
            AudioEngineState.Running => "Stop RNNoise",
            _ => "Start RNNoise"
        };

        _muteItem.Enabled = state == AudioEngineState.Running;
        _muteItem.Checked = isMuted;
        _muteItem.Text = isMuted ? "Unmute microphone" : "Mute microphone";
        _suppressionItem.Enabled = !isBusy;
        _suppressionItem.Checked = suppressionEnabled;
        UpdateProfiles(profiles, activeProfileId, isBusy);

        _notifyIcon.Text = state switch
        {
            AudioEngineState.Starting => "Muted — starting…",
            AudioEngineState.Stopping => "Muted — stopping…",
            AudioEngineState.Running when isMuted => "Muted — microphone muted",
            AudioEngineState.Running => "Muted — active",
            AudioEngineState.Faulted => "Muted — audio error",
            _ => "Muted — stopped"
        };
    }

    private void UpdateProfiles(
        IReadOnlyList<AudioProfile> profiles,
        string? activeProfileId,
        bool isBusy)
    {
        _profilesItem.DropDownItems.Clear();
        _profilesItem.Enabled = profiles.Count > 0 && !isBusy;
        foreach (var profile in profiles)
        {
            var item = new System.Windows.Forms.ToolStripMenuItem(profile.Name)
            {
                Checked = string.Equals(profile.Id, activeProfileId, StringComparison.OrdinalIgnoreCase)
            };
            var profileId = profile.Id;
            item.Click += (_, _) =>
                ProfileRequested?.Invoke(this, new ProfileRequestedEventArgs(profileId));
            _profilesItem.DropDownItems.Add(item);
        }
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
        var iconPath = Path.Combine(AppContext.BaseDirectory, "icon.ico");
        return File.Exists(iconPath)
            ? new Icon(iconPath, 64, 64)
            : (Icon)SystemIcons.Information.Clone();
    }
}

internal sealed class ProfileRequestedEventArgs(string profileId) : EventArgs
{
    public string ProfileId { get; } = profileId;
}

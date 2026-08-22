using System.Drawing;
using Muted.Core.Audio;
using Muted.Core.Settings;

namespace Muted.App.Services;

internal sealed class TrayService : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.ToolStripMenuItem _toggleItem;
    private readonly System.Windows.Forms.ToolStripMenuItem _muteItem;
    private readonly System.Windows.Forms.ToolStripMenuItem _suppressionItem;
    private readonly System.Windows.Forms.ToolStripMenuItem _monitorItem;
    private readonly System.Windows.Forms.ToolStripMenuItem _compactItem;
    private readonly System.Windows.Forms.ToolStripMenuItem _profilesItem;
    private readonly TrayIconFactory _icons = new();
    private TrayIconState _currentState = TrayIconState.Stopped;

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

        _monitorItem = new System.Windows.Forms.ToolStripMenuItem("Hear yourself");
        _monitorItem.Click += (_, _) => MonitorToggleRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(_monitorItem);

        _profilesItem = new System.Windows.Forms.ToolStripMenuItem("Profiles");
        menu.Items.Add(_profilesItem);

        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        _compactItem = new System.Windows.Forms.ToolStripMenuItem("Compact panel");
        _compactItem.Click += (_, _) => CompactToggleRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(_compactItem);

        var diagnosticsItem = menu.Items.Add("Run diagnostics…");
        diagnosticsItem.Click += (_, _) => DiagnosticsRequested?.Invoke(this, EventArgs.Empty);

        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        var exitItem = menu.Items.Add("Quit");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "Muted — RNNoise",
            Icon = _icons.Get(TrayIconState.Stopped),
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? ToggleRequested;

    public event EventHandler? MuteRequested;

    public event EventHandler? SuppressionToggleRequested;

    public event EventHandler? MonitorToggleRequested;

    public event EventHandler? CompactToggleRequested;

    public event EventHandler<ProfileRequestedEventArgs>? ProfileRequested;

    public event EventHandler? DiagnosticsRequested;

    public event EventHandler? ExitRequested;

    public void UpdateState(TrayState state)
    {
        var isBusy = state.EngineState is AudioEngineState.Starting or AudioEngineState.Stopping;
        _toggleItem.Enabled = !isBusy;
        _toggleItem.Text = state.EngineState switch
        {
            AudioEngineState.Starting => "Starting RNNoise…",
            AudioEngineState.Stopping => "Stopping RNNoise…",
            AudioEngineState.Running => "Stop RNNoise",
            _ => "Start RNNoise"
        };

        _muteItem.Enabled = state.EngineState == AudioEngineState.Running;
        _muteItem.Checked = state.IsMuted;
        _muteItem.Text = state.IsMuted ? "Unmute microphone" : "Mute microphone";
        _suppressionItem.Enabled = !isBusy;
        _suppressionItem.Checked = state.SuppressionEnabled;
        _monitorItem.Checked = state.MonitorEnabled;
        _compactItem.Checked = state.CompactMode;
        UpdateProfiles(state.Profiles, state.ActiveProfileId, isBusy);

        _notifyIcon.Text = Truncate(state.EngineState switch
        {
            AudioEngineState.Starting => "Muted — starting…",
            AudioEngineState.Stopping => "Muted — stopping…",
            AudioEngineState.Running when state.IsMuted => "Muted — microphone muted",
            AudioEngineState.Running => $"Muted — active ({state.ActiveProfileName})",
            AudioEngineState.Faulted => "Muted — audio error",
            _ => "Muted — stopped"
        });

        // The icon itself carries the state, so the tray is readable at a glance.
        var iconState = TrayIconFactory.StateFor(state.EngineState, state.IsMuted);
        if (iconState != _currentState)
        {
            _currentState = iconState;
            _notifyIcon.Icon = _icons.Get(iconState);
        }
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

    // Windows silently drops a NotifyIcon tooltip longer than 63 characters.
    private static string Truncate(string text) => text.Length <= 63 ? text : text[..60] + "…";

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icons.Dispose();
    }
}

internal sealed record TrayState(
    AudioEngineState EngineState,
    bool IsMuted,
    bool SuppressionEnabled,
    bool MonitorEnabled,
    bool CompactMode,
    string ActiveProfileName,
    string? ActiveProfileId,
    IReadOnlyList<AudioProfile> Profiles);

internal sealed class ProfileRequestedEventArgs(string profileId) : EventArgs
{
    public string ProfileId { get; } = profileId;
}

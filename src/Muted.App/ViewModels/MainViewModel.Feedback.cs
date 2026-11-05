using System.Diagnostics;
using Muted.App.Services;
using Muted.Core.Audio;
using Muted.Core.Settings;

namespace Muted.App.ViewModels;

/// <summary>Tray speaking state, mute reminders, push-to-talk sounds, health warnings and the in-game indicator.</summary>
internal sealed partial class MainViewModel
{
    private bool _speakingTrayEnabled = true, _muteReminderEnabled = true, _healthWarningsEnabled = true;
    private bool _pushToTalkSoundsEnabled, _traySpeaking, _lastTalkOpen, _suppressTalkCue;
    private string _healthWarning = string.Empty;
    private readonly CancellationTokenSource _toolsLifetime = new();
    private readonly Stopwatch _feedbackClock = Stopwatch.StartNew();
    private readonly AudioFeedbackTracker _feedback = new();
    private bool _cuePlaying;
    private bool _overlayEnabled;
    private OverlayPosition _overlayPosition = OverlayPosition.TopCenter;
    private bool _overlaySpeaking;

    public event EventHandler<string>? NotificationRequested;

    public bool SpeakingTrayEnabled { get => _speakingTrayEnabled; set { if (SetProperty(ref _speakingTrayEnabled, value)) QueueSave(); } }

    public bool MuteReminderEnabled { get => _muteReminderEnabled; set { if (SetProperty(ref _muteReminderEnabled, value)) QueueSave(); } }

    public bool PushToTalkSoundsEnabled { get => _pushToTalkSoundsEnabled; set { if (SetProperty(ref _pushToTalkSoundsEnabled, value)) QueueSave(); } }

    public bool HealthWarningsEnabled { get => _healthWarningsEnabled; set { if (SetProperty(ref _healthWarningsEnabled, value)) QueueSave(); } }

    public bool TraySpeaking { get => _traySpeaking; private set => SetProperty(ref _traySpeaking, value); }

    public string HealthWarning { get => _healthWarning; private set => SetProperty(ref _healthWarning, value); }

    public IReadOnlyList<OverlayPosition> OverlayPositions { get; } = Enum.GetValues<OverlayPosition>();

    public bool OverlayEnabled
    {
        get => _overlayEnabled;
        set
        {
            if (SetProperty(ref _overlayEnabled, value))
            {
                QueueSave();
            }
        }
    }

    public OverlayPosition OverlayPosition
    {
        get => _overlayPosition;
        set
        {
            if (SetProperty(ref _overlayPosition, value))
            {
                QueueSave();
            }
        }
    }

    /// <summary>Speaking right now, kept up to date even while every window is hidden.</summary>
    public bool OverlaySpeaking
    {
        get => _overlaySpeaking;
        private set => SetProperty(ref _overlaySpeaking, value);
    }

    private void LoadFeedbackSettings(AppSettings settings)
    {
        _speakingTrayEnabled = settings.SpeakingTrayEnabled;
        _muteReminderEnabled = settings.MuteReminderEnabled;
        _pushToTalkSoundsEnabled = settings.PushToTalkSoundsEnabled;
        _healthWarningsEnabled = settings.HealthWarningsEnabled;
        foreach (var name in new[] { nameof(SpeakingTrayEnabled), nameof(MuteReminderEnabled), nameof(PushToTalkSoundsEnabled), nameof(HealthWarningsEnabled) })
            OnPropertyChanged(name);
    }

    private void UpdateTalkCue()
    {
        var open = !IsEffectivelyMuted;
        var changed = open != _lastTalkOpen;
        _lastTalkOpen = open;
        if (changed && IsRunning && IsPushToTalkArmed && PushToTalkSoundsEnabled && !_previewMuted &&
            !_suppressTalkCue && !ClipBusy && !_cuePlaying)
            _ = PlayCueAsync(open);
    }

    private async Task PlayCueAsync(bool open)
    {
        _cuePlaying = true;
        try { await LocalAudioPlayer.PlayAsync(LocalAudioPlayer.Cue(open), PlaybackDeviceId(), _toolsLifetime.Token); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { FeatureStatus = "Push-to-talk sound: " + exception.Message; }
        finally { _cuePlaying = false; }
    }

    private void LoadOverlaySettings(AppSettings settings)
    {
        _overlayEnabled = settings.OverlayEnabled;
        _overlayPosition = settings.OverlayPosition;
        OnPropertyChanged(nameof(OverlayEnabled));
        OnPropertyChanged(nameof(OverlayPosition));
    }

    private AppSettings WithOverlaySettings(AppSettings settings) => settings with
    {
        OverlayEnabled = OverlayEnabled,
        OverlayPosition = OverlayPosition
    };
}

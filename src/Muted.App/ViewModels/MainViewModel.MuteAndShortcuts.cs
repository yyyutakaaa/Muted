using System.Collections.ObjectModel;
using Muted.App.Infrastructure;
using Muted.Core.Settings;

namespace Muted.App.ViewModels;

/// <summary>Mute, push to talk and push to mute, and the global keyboard shortcuts.</summary>
internal sealed partial class MainViewModel
{
    private bool _isMuted;
    private bool _pushToTalkHeld;
    private bool _pushToMuteHeld;
    private bool _hotkeysActive;

    public event EventHandler? HotkeysChanged;

    public ObservableCollection<HotkeyBindingViewModel> Hotkeys { get; } = [];

    public RelayCommand ToggleMuteCommand { get; }

    public RelayCommand<HotkeyBindingViewModel> ClearHotkeyCommand { get; }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetProperty(ref _isMuted, value))
            {
                ApplySuppression();
                OnPropertyChanged(nameof(MuteStatusText));
                OnPropertyChanged(nameof(IsEffectivelyMuted));
            }
        }
    }

    public bool IsEffectivelyMuted => _previewMuted || IsMuted || (IsPushToTalkArmed && !_pushToTalkHeld) || _pushToMuteHeld;

    public bool IsPushToTalkArmed => Hotkeys.Any(hotkey =>
        hotkey.Action == HotkeyAction.PushToTalk && hotkey.Enabled && hotkey.IsAssigned);

    public string MuteStatusText => IsEffectivelyMuted ? "Microphone muted" : "Microphone live";

    public bool HotkeysActive
    {
        get => _hotkeysActive;
        set => SetProperty(ref _hotkeysActive, value);
    }

    public string MuteHotkeyText => HotkeyTextFor(HotkeyAction.ToggleMute);

    public string PushToTalkHotkeyText => HotkeyTextFor(HotkeyAction.PushToTalk) is var text && text == "Not set"
        ? "No key set yet"
        : "Hold " + HotkeyTextFor(HotkeyAction.PushToTalk);

    /// <summary>Called by the global shortcut hook, already on the UI thread.</summary>
    public void HandleHotkey(HotkeyAction action, bool isPressed)
    {
        switch (action)
        {
            case HotkeyAction.ToggleMute when isPressed:
                if (ToggleMuteCommand.CanExecute(null))
                {
                    ToggleMuteCommand.Execute(null);
                }

                break;
            case HotkeyAction.PushToTalk:
                _pushToTalkHeld = isPressed;
                RaiseMuteChanged();
                break;
            case HotkeyAction.PushToMute:
                _pushToMuteHeld = isPressed;
                RaiseMuteChanged();
                break;
            case HotkeyAction.ToggleSuppression when isPressed:
                if (ToggleSuppressionCommand.CanExecute(null))
                {
                    ToggleSuppressionCommand.Execute(null);
                }

                break;
            case HotkeyAction.ToggleEngine when isPressed:
                if (ToggleCommand.CanExecute(null))
                {
                    ToggleCommand.Execute(null);
                }

                break;
            case HotkeyAction.ShowWindow when isPressed:
                ShowWindowRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    public IReadOnlyList<HotkeyBinding> GetHotkeyBindings() =>
        Hotkeys.Select(hotkey => hotkey.ToBinding()).ToArray();

    private void RaiseMuteChanged()
    {
        ApplySuppression();
        OnPropertyChanged(nameof(MuteStatusText));
        OnPropertyChanged(nameof(IsEffectivelyMuted));
        UpdateTalkCue();
    }

    private void OnHotkeyBindingChanged()
    {
        OnPropertyChanged(nameof(IsPushToTalkArmed));
        if (!IsPushToTalkArmed)
        {
            _pushToTalkHeld = false;
        }

        RaiseMuteChanged();
        HotkeysChanged?.Invoke(this, EventArgs.Empty);
        if (_initialized)
        {
            QueueSave();
        }
    }

    private string HotkeyTextFor(HotkeyAction action) =>
        Hotkeys.FirstOrDefault(hotkey => hotkey.Action == action) is { IsAssigned: true } hotkey
            ? hotkey.DisplayText
            : "Not set";
}

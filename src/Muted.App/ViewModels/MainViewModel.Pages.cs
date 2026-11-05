using System.ComponentModel;

namespace Muted.App.ViewModels;

/// <summary>Which page is open, and read-outs that are derived from other properties.</summary>
internal sealed partial class MainViewModel
{
    private AppPage _selectedPage = AppPage.Dashboard;

    public event EventHandler? ShowWindowRequested;

    public AppPage SelectedPage
    {
        get => _selectedPage;
        set => SetProperty(ref _selectedPage, value);
    }

    public bool IsAdvancedPageOpen
    {
        get => SelectedPage == AppPage.Advanced;
        set
        {
            if (value) SelectedPage = AppPage.Advanced;
            else if (SelectedPage == AppPage.Advanced) SelectedPage = AppPage.Dashboard;
        }
    }

    public bool IsSettingsPageOpen
    {
        get => SelectedPage == AppPage.Settings;
        set
        {
            if (value) SelectedPage = AppPage.Settings;
            else if (SelectedPage == AppPage.Settings) SelectedPage = AppPage.Dashboard;
        }
    }

    public string ProcessingActionText => IsRunning ? "Stop processing" : "Start processing";

    public string ProcessingStatusText => IsRecovering ? "Reconnecting…"
        : IsRunning ? "Active"
        : IsBusy ? StatusText
        : HasError ? "Unavailable"
        : !HasDevices || !IsRoutingReady ? "Choose devices" : "Stopped";

    private void InitializeDerivedProperties()
    {
        PropertyChanged += OnHighPassOrHotkeyChanged;
        PropertyChanged += OnPageStateSourceChanged;
    }

    private void OnHighPassOrHotkeyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        switch (eventArgs.PropertyName)
        {
            case nameof(HighPassEnabled):
            case nameof(HighPassFrequency):
                OnPropertyChanged(nameof(HighPassChoice));
                break;
            case nameof(IsPushToTalkArmed):
                OnPropertyChanged(nameof(MuteHotkeyText));
                OnPropertyChanged(nameof(PushToTalkHotkeyText));
                break;
        }
    }

    private void OnPageStateSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SelectedPage):
                OnPropertyChanged(nameof(IsAdvancedPageOpen));
                OnPropertyChanged(nameof(IsSettingsPageOpen));
                break;
            case nameof(IsRunning):
                OnPropertyChanged(nameof(ProcessingActionText));
                OnPropertyChanged(nameof(VoiceTestStateText));
                OnPropertyChanged(nameof(ProcessingStatusText));
                break;
            case nameof(IsRecording):
            case nameof(IsClipPlaying):
            case nameof(ClipTimeText):
            case nameof(ClipDuration):
                OnPropertyChanged(nameof(VoiceTestStateText));
                break;
            case nameof(IsBusy):
            case nameof(StatusText):
            case nameof(HasError):
            case nameof(IsRecovering):
            case nameof(HasDevices):
            case nameof(IsRoutingReady):
                OnPropertyChanged(nameof(ProcessingStatusText));
                break;
            case nameof(WetMix):
            case nameof(SuppressionEnabled):
                OnPropertyChanged(nameof(NoiseRemoval));
                OnPropertyChanged(nameof(NoiseRemovalDescription));
                break;
            case nameof(OutputGain):
                OnPropertyChanged(nameof(VoiceLevelDb));
                OnPropertyChanged(nameof(VoiceLevelText));
                break;
        }
    }
}

using System.ComponentModel;

namespace Muted.App.ViewModels;

internal enum NoiseRemovalMode { Gentle, Balanced, Strong }

internal sealed partial class MainViewModel
{
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

    /// <summary>True on the home screen; every other page shows a way back and the on/off pill.</summary>
    public bool IsHomePage => SelectedPage == AppPage.Dashboard;

    /// <summary>The short word next to the status on the Advanced menu.</summary>
    public string ProcessingShortAction => IsRunning ? "Stop" : "Start";

    // The two Processing cards without a switch say in their header what they are set to.
    public string LevelsCardSummary =>
        $"Boost {InputGainText} · high-pass {(HighPassEnabled ? HighPassText : "off")} · " +
        $"automatic level {(AutoGainEnabled ? "on" : "off")} · limiter {(LimiterEnabled ? "on" : "off")}";

    public string BufferCardSummary => LatencyText;

    private static readonly HashSet<string> CardSummarySources =
    [
        nameof(InputGain), nameof(HighPassEnabled), nameof(HighPassFrequency), nameof(AutoGainEnabled),
        nameof(LimiterEnabled), nameof(TargetLatencyMilliseconds)
    ];

    private void RaiseCardSummaries()
    {
        OnPropertyChanged(nameof(LevelsCardSummary));
        OnPropertyChanged(nameof(BufferCardSummary));
    }

    public string VoiceTestStateText => IsRecording ? "Recording…"
        : IsClipPlaying ? "Playing · " + ClipTimeText
        : ClipDuration > 0 ? "Recorded · " + ClipTimeText
        : IsRunning ? "Ready to record" : "Start processing to record";

    public string ProcessingStatusText => IsRecovering ? "Reconnecting…"
        : IsRunning ? "Active"
        : IsBusy ? StatusText
        : HasError ? "Unavailable"
        : !HasDevices || !IsRoutingReady ? "Choose devices" : "Stopped";

    // RNNoise has no model-strength knob: use distinct dry/wet blends.
    // Preserve custom blends until the user explicitly picks a segment.
    public NoiseRemovalMode NoiseRemoval
    {
        get => WetMix < 0.625 ? NoiseRemovalMode.Gentle
            : WetMix < 0.925 ? NoiseRemovalMode.Balanced : NoiseRemovalMode.Strong;
        set
        {
            SuppressionEnabled = true;
            WetMix = value switch
            {
                NoiseRemovalMode.Gentle => 0.45,
                NoiseRemovalMode.Balanced => 0.8,
                _ => 1.0
            };
        }
    }

    public string NoiseRemovalDescription => !SuppressionEnabled
        ? "Noise suppression is off under Processing. Pick a level to turn it on."
        : $"{WetMix:0%} filtered audio. Gentle keeps more room sound; Strong uses only the filtered signal.";

    /// <summary>The RNNoise strength controls only matter while Studio Voice is off.</summary>
    public bool CanAdjustNoiseBlend => SuppressionEnabled && !StudioVoice;

    public double VoiceLevelDb
    {
        get => 20 * Math.Log10(OutputGain);
        set => OutputGain = Math.Pow(10, value / 20);
    }

    public string VoiceLevelText => $"{VoiceLevelDb:+0;-0;0} dB";

    private void RedesignSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is { } name && CardSummarySources.Contains(name))
        {
            RaiseCardSummaries();
        }

        switch (e.PropertyName)
        {
            case nameof(SelectedPage):
                OnPropertyChanged(nameof(IsAdvancedPageOpen));
                OnPropertyChanged(nameof(IsSettingsPageOpen));
                OnPropertyChanged(nameof(IsHomePage));
                break;
            case nameof(IsRunning):
                OnPropertyChanged(nameof(ProcessingActionText));
                OnPropertyChanged(nameof(ProcessingShortAction));
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
                OnPropertyChanged(nameof(CanAdjustNoiseBlend));
                break;
            case nameof(OutputGain):
                OnPropertyChanged(nameof(VoiceLevelDb));
                OnPropertyChanged(nameof(VoiceLevelText));
                break;
        }
    }
}

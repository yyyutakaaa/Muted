using Muted.Core.Audio;

namespace Muted.App.ViewModels;

/// <summary>
/// Studio Voice: an AI model cleans the microphone instead of RNNoise. It only does the
/// noise removal. Every other setting stays exactly as you set it.
/// </summary>
internal sealed partial class MainViewModel
{
    private bool _studioVoice;
    private StudioModel _studioModel = StudioModel.DeepFilterNet3;
    private string _studioVoiceStatusText = string.Empty;
    private bool _studioVoiceUnavailable;
    private bool _studioAutoStepDown = true;
    private bool _studioRoomTone = true;

    public bool StudioVoice
    {
        get => _studioVoice;
        set
        {
            if (SetAudioProperty(ref _studioVoice, value))
            {
                OnPropertyChanged(nameof(CanAdjustNoiseBlend));
                RefreshStudioVoiceStatus();
            }
        }
    }

    /// <summary>Classic, Balanced or Maximum quality. Switching keeps the sound going.</summary>
    public StudioModel StudioModel
    {
        get => _studioModel;
        set
        {
            if (SetAudioProperty(ref _studioModel, value))
            {
                RefreshStudioVoiceStatus();
            }
        }
    }

    /// <summary>
    /// Lets Maximum hand over to Balanced while the PC is too busy to run it on time,
    /// such as during a heavy game, and take over again once there is room.
    /// </summary>
    public bool StudioAutoStepDown
    {
        get => _studioAutoStepDown;
        set
        {
            if (SetAudioProperty(ref _studioAutoStepDown, value))
            {
                RefreshStudioVoiceStatus();
            }
        }
    }

    /// <summary>A faint room sound under Studio Voice instead of dead silence.</summary>
    public bool StudioRoomTone
    {
        get => _studioRoomTone;
        set => SetAudioProperty(ref _studioRoomTone, value);
    }

    /// <summary>The three model choices, for the model descriptions.</summary>
    public IReadOnlyList<StudioModelOption> StudioModelOptions { get; } =
    [
        new(StudioModel.DeepFilterNet3, "Classic", "DeepFilterNet3", "Lightest. Removes fan, hiss and keyboard, and holds up well in quiet rooms."),
        new(StudioModel.DpdfNet2, "Balanced", "DPDFNet 2", "Also removes noise that sits in the same band as your voice, such as a humming fan."),
        new(StudioModel.DpdfNet8, "Maximum quality", "DPDFNet 8", "The strongest of the three. Uses the most processor and keeps the most detail.")
    ];

    /// <summary>The model that is actually cleaning, once the engine has loaded it.</summary>
    public StudioModel? ActiveStudioModel => IsRunning ? _engine.StudioVoiceActiveModel : null;

    /// <summary>The choice that describes what you are hearing right now.</summary>
    public StudioModel EffectiveStudioModel => ActiveStudioModel ?? StudioModel;

    public string StudioModelSummary => StudioModelOptions
        .FirstOrDefault(option => option.Model == StudioModel)?.Summary
        ?? StudioModelOptions[0].Summary;

    public string StudioVoiceStatusText
    {
        get => _studioVoiceStatusText;
        private set => SetProperty(ref _studioVoiceStatusText, value);
    }

    /// <summary>True when the model could not run here and RNNoise took over.</summary>
    public bool StudioVoiceUnavailable
    {
        get => _studioVoiceUnavailable;
        private set => SetProperty(ref _studioVoiceUnavailable, value);
    }

    private string DisplayName(StudioModel model) =>
        StudioModelOptions.FirstOrDefault(option => option.Model == model)?.DisplayName ?? model.ToString();

    private void RefreshStudioVoiceStatus()
    {
        var status = IsRunning ? _engine.StudioVoiceStatus : StudioVoiceStatus.Off;
        StudioVoiceUnavailable = StudioVoice && status == StudioVoiceStatus.Unavailable;
        var modelName = DisplayName(StudioModel);
        StudioVoiceStatusText = !StudioVoice
            ? "Replaces the standard noise removal with an AI model that also removes room echo."
            : !IsRunning
                ? $"{modelName} takes over all noise removal when processing starts."
                : status switch
                {
                    StudioVoiceStatus.Loading => $"Loading {modelName}…",
                    StudioVoiceStatus.Active when _engine.StudioVoiceSteppedDown =>
                        $"Your PC is busy, so Balanced cleans for now. {modelName} comes back by itself when there is room again.",
                    StudioVoiceStatus.Active => ActiveStudioModel is { } active && active != StudioModel
                        ? _engine.StudioVoiceSwitching
                            ? $"Switching to {modelName}; {DisplayName(active)} keeps cleaning until it is ready."
                            : $"{modelName} could not start, so {DisplayName(active)} keeps cleaning. {_engine.StudioVoiceError}"
                        : $"Active. {modelName} does all the noise removal.",
                    StudioVoiceStatus.Unavailable =>
                        $"Not available on this PC, so the standard noise removal is used. {_engine.StudioVoiceError}",
                    _ => "On."
                };

        OnPropertyChanged(nameof(EffectiveStudioModel));
        OnPropertyChanged(nameof(ActiveStudioModel));
        OnPropertyChanged(nameof(StudioModelSummary));
    }
}

/// <summary>One Studio Voice model as the UI shows it.</summary>
internal sealed record StudioModelOption(StudioModel Model, string DisplayName, string Technology, string Summary);

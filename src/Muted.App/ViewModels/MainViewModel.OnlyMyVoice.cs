using Muted.App.Infrastructure;
using Muted.Audio.Windows.Speech;
using Muted.Core.Settings;
using Muted.Core.Speech;

namespace Muted.App.ViewModels;

/// <summary>
/// "Only my voice": after "Tune to my voice" has learned how you sound, Muted holds
/// other voices back, such as someone talking behind you or the TV, while you are not
/// talking yourself.
/// </summary>
internal sealed partial class MainViewModel
{
    private bool _onlyMyVoice;
    private double _speakerStrictness = 0.5;
    private VoiceProfile? _voiceProfile;
    private string _onlyMyVoiceStatusText = string.Empty;

    public RelayCommand ForgetVoiceCommand { get; private set; } = null!;

    public bool OnlyMyVoice
    {
        get => _onlyMyVoice;
        set
        {
            if (SetAudioProperty(ref _onlyMyVoice, value))
            {
                RefreshOnlyMyVoiceStatus();
            }
        }
    }

    /// <summary>0 lets more through, 1 holds back anything that is not clearly you.</summary>
    public double SpeakerStrictness
    {
        get => _speakerStrictness;
        set
        {
            if (SetAudioProperty(ref _speakerStrictness, Math.Clamp(value, 0, 1)))
            {
                OnPropertyChanged(nameof(SpeakerStrictnessText));
            }
        }
    }

    public string SpeakerStrictnessText => SpeakerStrictness switch
    {
        < 0.34 => "Lenient",
        > 0.66 => "Strict",
        _ => "Normal"
    };

    /// <summary>True once "Tune to my voice" has learned your voice.</summary>
    public bool HasVoiceProfile => _voiceProfile is not null;

    public string OnlyMyVoiceStatusText
    {
        get => _onlyMyVoiceStatusText;
        private set => SetProperty(ref _onlyMyVoiceStatusText, value);
    }

    private VoiceProfile? LearnedVoice
    {
        get => _voiceProfile;
        set
        {
            if (ReferenceEquals(_voiceProfile, value))
            {
                return;
            }

            _voiceProfile = value;
            OnPropertyChanged(nameof(HasVoiceProfile));
            ForgetVoiceCommand?.RaiseCanExecuteChanged();
            if (_initialized)
            {
                ApplySuppression();
                QueueSave();
            }

            RefreshOnlyMyVoiceStatus();
        }
    }

    private void InitializeOnlyMyVoice()
    {
        ForgetVoiceCommand = new(ForgetVoice, () => HasVoiceProfile);
    }

    private void LoadOnlyMyVoiceSettings(AppSettings settings)
    {
        _onlyMyVoice = settings.OnlyMyVoice;
        _speakerStrictness = Math.Clamp(settings.SpeakerStrictness, 0, 1);
        _voiceProfile = VoiceProfile.FromBase64(settings.VoiceProfile);
        RefreshOnlyMyVoiceStatus();
    }

    private AppSettings WithOnlyMyVoiceSettings(AppSettings settings) => settings with
    {
        OnlyMyVoice = OnlyMyVoice,
        SpeakerStrictness = (float)SpeakerStrictness,
        VoiceProfile = _voiceProfile?.ToBase64()
    };

    private void ForgetVoice()
    {
        LearnedVoice = null;
        OnlyMyVoice = false;
        CalibrationIntro = "Your voice is forgotten. Run Tune to my voice to learn it again.";
    }

    private void RefreshOnlyMyVoiceStatus()
    {
        OnlyMyVoiceStatusText = !OnlyMyVoice
            ? HasVoiceProfile
                ? "Off. Muted knows your voice, so you can switch this on."
                : "Run Tune to my voice first, so Muted can learn how you sound."
            : !HasVoiceProfile
                ? "Run Tune to my voice first, so Muted can learn how you sound."
                : !IsRunning
                    ? "Waiting for audio"
                    : _engine.OnlyMyVoiceStatus switch
                    {
                        SpeakerCheckStatus.Loading => "Loading the voice model…",
                        SpeakerCheckStatus.Unavailable => $"Not available on this PC, so every voice goes through. {_engine.OnlyMyVoiceError}",
                        _ => _engine.HoldingOtherVoice
                            ? "Holding back another voice."
                            : float.IsNaN(_engine.SpeakerSimilarity)
                                ? "Listening. Talk and it checks that it is you."
                                : "Letting your voice through."
                    };
    }
}

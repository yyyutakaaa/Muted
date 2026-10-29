using Muted.Core.Audio;
using Muted.Core.Dsp;
using Muted.Core.Settings;

namespace Muted.App.ViewModels;

/// <summary>Voice character (the warm radio sound) and the shout guard.</summary>
internal sealed partial class MainViewModel
{
    private bool _voiceCharacterEnabled;
    private double _warmth = 0.6;
    private double _proximity = 0.5;
    private double _smoothness = 0.6;
    private double _compression = 0.5;
    private double _saturation = 0.3;
    private bool _shoutGuardEnabled;
    private double _shoutThresholdDb = -6;
    private ShoutGuardMode _shoutGuardMode = ShoutGuardMode.Silence;
    private string _shoutGuardStatusText = "Not running";
    private string _compressionStatusText = "0.0 dB";

    public bool VoiceCharacterEnabled
    {
        get => _voiceCharacterEnabled;
        set => SetAudioProperty(ref _voiceCharacterEnabled, value);
    }

    public double Warmth
    {
        get => _warmth;
        set
        {
            if (SetAudioProperty(ref _warmth, value))
            {
                OnPropertyChanged(nameof(WarmthText));
            }
        }
    }

    public string WarmthText => FormatAmount(Warmth);

    public double Proximity
    {
        get => _proximity;
        set
        {
            if (SetAudioProperty(ref _proximity, value))
            {
                OnPropertyChanged(nameof(ProximityText));
            }
        }
    }

    public string ProximityText => FormatAmount(Proximity);

    public double Smoothness
    {
        get => _smoothness;
        set
        {
            if (SetAudioProperty(ref _smoothness, value))
            {
                OnPropertyChanged(nameof(SmoothnessText));
            }
        }
    }

    public string SmoothnessText => FormatAmount(Smoothness);

    public double Compression
    {
        get => _compression;
        set
        {
            if (SetAudioProperty(ref _compression, value))
            {
                OnPropertyChanged(nameof(CompressionText));
            }
        }
    }

    public string CompressionText => FormatAmount(Compression);

    public double Saturation
    {
        get => _saturation;
        set
        {
            if (SetAudioProperty(ref _saturation, value))
            {
                OnPropertyChanged(nameof(SaturationText));
            }
        }
    }

    public string SaturationText => FormatAmount(Saturation);

    /// <summary>Live gain reduction of the voice compressor, for the advanced readout.</summary>
    public string CompressionStatusText
    {
        get => _compressionStatusText;
        private set => SetProperty(ref _compressionStatusText, value);
    }

    public bool ShoutGuardEnabled
    {
        get => _shoutGuardEnabled;
        set => SetAudioProperty(ref _shoutGuardEnabled, value);
    }

    public double ShoutThresholdDb
    {
        get => _shoutThresholdDb;
        set
        {
            if (SetAudioProperty(ref _shoutThresholdDb, value))
            {
                OnPropertyChanged(nameof(ShoutThresholdText));
            }
        }
    }

    public string ShoutThresholdText => $"{ShoutThresholdDb:0} dB";

    /// <summary>Off is full silence; on lets a shout through 20 dB quieter.</summary>
    public bool ShoutGuardDucks
    {
        get => _shoutGuardMode == ShoutGuardMode.Duck;
        set => SetAudioProperty(ref _shoutGuardMode, value ? ShoutGuardMode.Duck : ShoutGuardMode.Silence);
    }

    public string ShoutGuardStatusText
    {
        get => _shoutGuardStatusText;
        private set => SetProperty(ref _shoutGuardStatusText, value);
    }

    private static string FormatAmount(double amount) => $"{amount * 100:0}%";

    private VoiceCharacterOptions BuildVoiceCharacterOptions() => new(
        VoiceCharacterEnabled,
        (float)Warmth,
        (float)Proximity,
        (float)Smoothness,
        (float)Compression,
        (float)Saturation);

    private ShoutGuardOptions BuildShoutGuardOptions() =>
        new(ShoutGuardEnabled, (float)ShoutThresholdDb, _shoutGuardMode);

    private void LoadVoiceSettings(AppSettings settings)
    {
        _voiceCharacterEnabled = settings.VoiceCharacterEnabled;
        _warmth = settings.Warmth;
        _proximity = settings.Proximity;
        _smoothness = settings.Smoothness;
        _compression = settings.Compression;
        _saturation = settings.Saturation;
        _shoutGuardEnabled = settings.ShoutGuardEnabled;
        _shoutThresholdDb = settings.ShoutThresholdDb;
        _shoutGuardMode = settings.ShoutGuardMode;
        _autoTuneEnabled = settings.AutoTuneEnabled;
        UpdateAutoTuneStatus();
    }

    private static readonly string[] VoicePropertyNames =
    [
        nameof(VoiceCharacterEnabled), nameof(Warmth), nameof(WarmthText), nameof(Proximity),
        nameof(ProximityText), nameof(Smoothness), nameof(SmoothnessText), nameof(Compression),
        nameof(CompressionText), nameof(Saturation), nameof(SaturationText),
        nameof(ShoutGuardEnabled), nameof(ShoutThresholdDb), nameof(ShoutThresholdText),
        nameof(ShoutGuardDucks), nameof(AutoTuneEnabled)
    ];

    private void ApplyVoiceProfile(AudioProfile profile)
    {
        VoiceCharacterEnabled = profile.VoiceCharacterEnabled;
        Warmth = profile.Warmth;
        Proximity = profile.Proximity;
        Smoothness = profile.Smoothness;
        Compression = profile.Compression;
        Saturation = profile.Saturation;
        ShoutGuardEnabled = profile.ShoutGuardEnabled;
        ShoutThresholdDb = profile.ShoutThresholdDb;
        ShoutGuardDucks = profile.ShoutGuardMode == ShoutGuardMode.Duck;

        // A profile is a new starting point for the automatic tuning.
        ResetAutoTuneBaseline();
    }

    private AudioProfile WithVoiceSettings(AudioProfile profile) => profile with
    {
        VoiceCharacterEnabled = VoiceCharacterEnabled,
        Warmth = (float)Warmth,
        Proximity = (float)Proximity,
        Smoothness = (float)Smoothness,
        Compression = (float)Compression,
        Saturation = (float)Saturation,
        ShoutGuardEnabled = ShoutGuardEnabled,
        ShoutThresholdDb = (float)ShoutThresholdDb,
        ShoutGuardMode = _shoutGuardMode
    };

    private AppSettings WithVoiceSettings(AppSettings settings) => settings with
    {
        VoiceCharacterEnabled = VoiceCharacterEnabled,
        Warmth = (float)Warmth,
        Proximity = (float)Proximity,
        Smoothness = (float)Smoothness,
        Compression = (float)Compression,
        Saturation = (float)Saturation,
        ShoutGuardEnabled = ShoutGuardEnabled,
        ShoutThresholdDb = (float)ShoutThresholdDb,
        ShoutGuardMode = _shoutGuardMode,
        AutoTuneEnabled = AutoTuneEnabled
    };

    private void UpdateVoiceReadouts(AudioMetrics metrics, bool running)
    {
        CompressionStatusText = $"{metrics.CompressorReductionDb:0.0} dB";
        if (!running || !ShoutGuardEnabled)
        {
            ShoutGuardStatusText = ShoutGuardEnabled ? "Waiting for audio" : "Off";
            return;
        }

        var count = metrics.ShoutGuardTriggers;
        var blocked = count switch
        {
            0 => "nothing blocked yet",
            1 => "1 shout blocked",
            _ => $"{count} shouts blocked"
        };
        ShoutGuardStatusText = metrics.ShoutGuardEngaged ? $"Blocking now · {blocked}" : $"Listening · {blocked}";
    }
}

using System.Globalization;
using Muted.App.Infrastructure;
using Muted.Audio.Windows.Devices;
using Muted.Core.Audio;
using Muted.Core.Dsp;

namespace Muted.App.ViewModels;

internal enum NoiseRemovalMode { Gentle, Balanced, Strong }

/// <summary>The processing chain settings: noise removal, gate, gains, filters, echo cancellation and monitoring.</summary>
internal sealed partial class MainViewModel
{
    private const string HighPassOff = "Off";
    private AudioDeviceInfo? _selectedMonitor;
    private bool _suppressionEnabled = true;
    private double _wetMix = 1;
    private bool _voiceGateEnabled;
    private double _voiceSensitivity = 0.55;
    private double _voiceHoldMilliseconds = 250;
    private double _inputGain = 1;
    private double _outputGain = 1;
    private bool _highPassEnabled;
    private double _highPassFrequency = 80;
    private bool _limiterEnabled = true;
    private bool _autoGainEnabled;
    private double _autoGainTargetDb = -18;
    private bool _echoEnabled;
    private double _echoStrength = 0.5;
    private AudioDeviceInfo? _selectedEchoReference;
    private string _echoReductionText = "0.0 dB";
    private bool _monitorEnabled;
    private double _monitorVolume = 0.6;
    private int _targetLatencyMilliseconds = 40;
    private bool _isBypassActive;

    public RelayCommand ToggleSuppressionCommand { get; }

    public AudioDeviceInfo? SelectedMonitor
    {
        get => _selectedMonitor;
        set
        {
            if (!SetProperty(ref _selectedMonitor, value) || !_initialized)
            {
                return;
            }

            OnPropertyChanged(nameof(MonitorFeedbackWarning));
            if (!_isRefreshingDevices)
            {
                PushMonitorOptions();
                if (!_isApplyingProfile)
                {
                    QueueSave();
                }
            }
        }
    }

    public bool SuppressionEnabled
    {
        get => _suppressionEnabled;
        set
        {
            if (SetAudioProperty(ref _suppressionEnabled, value))
            {
                OnPropertyChanged(nameof(SuppressionStatusText));
            }
        }
    }

    public string SuppressionStatusText => IsBypassActive
        ? "Bypassed"
        : SuppressionEnabled ? "RNNoise on" : "RNNoise off";

    public double WetMix
    {
        get => _wetMix;
        set
        {
            if (SetAudioProperty(ref _wetMix, Math.Clamp(value, 0, 1)))
            {
                OnPropertyChanged(nameof(WetMixText));
            }
        }
    }

    public string WetMixText => $"{WetMix * 100:0}% filtered";

    public bool VoiceGateEnabled
    {
        get => _voiceGateEnabled;
        set => SetAudioProperty(ref _voiceGateEnabled, value);
    }

    public double VoiceSensitivity
    {
        get => _voiceSensitivity;
        set
        {
            if (SetAudioProperty(ref _voiceSensitivity, value))
            {
                OnPropertyChanged(nameof(VoiceSensitivityText));
                OnPropertyChanged(nameof(IsVoiceDetected));
            }
        }
    }

    public string VoiceSensitivityText => $"{VoiceSensitivity * 100:0}%";

    public double VoiceHoldMilliseconds
    {
        get => _voiceHoldMilliseconds;
        set
        {
            if (SetAudioProperty(ref _voiceHoldMilliseconds, value))
            {
                OnPropertyChanged(nameof(VoiceHoldText));
            }
        }
    }

    public string VoiceHoldText => $"{VoiceHoldMilliseconds:0} ms";

    public double InputGain
    {
        get => _inputGain;
        set
        {
            if (SetAudioProperty(ref _inputGain, value))
            {
                OnPropertyChanged(nameof(InputGainText));
            }
        }
    }

    public string InputGainText => FormatGain(InputGain);

    public double OutputGain
    {
        get => _outputGain;
        set
        {
            if (SetAudioProperty(ref _outputGain, value))
            {
                OnPropertyChanged(nameof(OutputGainText));
            }
        }
    }

    public string OutputGainText => FormatGain(OutputGain);

    public bool HighPassEnabled
    {
        get => _highPassEnabled;
        set => SetAudioProperty(ref _highPassEnabled, value);
    }

    public double HighPassFrequency
    {
        get => _highPassFrequency;
        set
        {
            if (SetAudioProperty(ref _highPassFrequency, value))
            {
                OnPropertyChanged(nameof(HighPassText));
            }
        }
    }

    public string HighPassText => $"{HighPassFrequency:0} Hz";

    public bool LimiterEnabled
    {
        get => _limiterEnabled;
        set => SetAudioProperty(ref _limiterEnabled, value);
    }

    public bool AutoGainEnabled
    {
        get => _autoGainEnabled;
        set => SetAudioProperty(ref _autoGainEnabled, value);
    }

    public double AutoGainTargetDb
    {
        get => _autoGainTargetDb;
        set
        {
            if (SetAudioProperty(ref _autoGainTargetDb, value))
            {
                OnPropertyChanged(nameof(AutoGainTargetText));
            }
        }
    }

    public string AutoGainTargetText => $"{AutoGainTargetDb:0} dB";

    /// <summary>
    /// Subtracts what your speakers play out of the microphone, so a headset stops
    /// being a requirement.
    /// </summary>
    public bool EchoCancellationEnabled
    {
        get => _echoEnabled;
        set
        {
            if (!SetProperty(ref _echoEnabled, value) || !_initialized)
            {
                return;
            }

            PushEchoOptions();
            QueueSave();
        }
    }

    public AudioDeviceInfo? SelectedEchoReference
    {
        get => _selectedEchoReference;
        set
        {
            if (!SetProperty(ref _selectedEchoReference, value) || !_initialized)
            {
                return;
            }

            if (!_isRefreshingDevices)
            {
                PushEchoOptions();
                QueueSave();
            }
        }
    }

    public double EchoStrength
    {
        get => _echoStrength;
        set
        {
            if (!SetProperty(ref _echoStrength, Math.Clamp(value, 0, 1)) || !_initialized)
            {
                return;
            }

            OnPropertyChanged(nameof(EchoStrengthText));
            PushEchoOptions();
            QueueSave();
        }
    }

    public string EchoStrengthText => EchoStrength <= 0.01
        ? "off"
        : $"{EchoStrength * 100:0}%";

    public string EchoReductionText
    {
        get => _echoReductionText;
        private set => SetProperty(ref _echoReductionText, value);
    }

    public bool MonitorEnabled
    {
        get => _monitorEnabled;
        set
        {
            if (!SetProperty(ref _monitorEnabled, value) || !_initialized)
            {
                return;
            }

            OnPropertyChanged(nameof(MonitorFeedbackWarning));
            PushMonitorOptions();
            if (!_isApplyingProfile)
            {
                QueueSave();
            }
        }
    }

    public double MonitorVolume
    {
        get => _monitorVolume;
        set
        {
            if (!SetProperty(ref _monitorVolume, Math.Clamp(value, 0, 1)) || !_initialized)
            {
                return;
            }

            OnPropertyChanged(nameof(MonitorVolumeText));
            PushMonitorOptions();
            if (!_isApplyingProfile)
            {
                QueueSave();
            }
        }
    }

    public string MonitorVolumeText => $"{MonitorVolume * 100:0}%";

    /// <summary>Monitoring into speakers feeds the room back into the microphone.</summary>
    public bool MonitorFeedbackWarning =>
        MonitorEnabled &&
        SelectedMonitor is not null &&
        !WasapiDeviceCatalog.IsLikelyVirtualCable(SelectedMonitor.Name);

    public int TargetLatencyMilliseconds
    {
        get => _targetLatencyMilliseconds;
        set
        {
            var clamped = Math.Clamp(value, 20, 100);
            if (SetProperty(ref _targetLatencyMilliseconds, clamped) && _initialized)
            {
                OnPropertyChanged(nameof(LatencyText));
                if (!_isApplyingProfile)
                {
                    QueueSave();
                }
            }
        }
    }

    public string LatencyText => $"{TargetLatencyMilliseconds} ms";

    /// <summary>True while the A/B button is held and RNNoise is out of the path.</summary>
    public bool IsBypassActive
    {
        get => _isBypassActive;
        private set
        {
            if (SetProperty(ref _isBypassActive, value))
            {
                ApplySuppression();
                OnPropertyChanged(nameof(SuppressionStatusText));
            }
        }
    }

    /// <summary>The high-pass filter as one dropdown: off, or one of the usual cut-offs.</summary>
    public IReadOnlyList<string> HighPassChoices { get; } =
        [HighPassOff, "60 Hz", "80 Hz", "100 Hz", "120 Hz", "150 Hz"];

    public string HighPassChoice
    {
        get
        {
            if (!HighPassEnabled)
            {
                return HighPassOff;
            }

            // A cut-off set elsewhere with the fine slider shows as the nearest choice.
            return HighPassChoices
                .Skip(1)
                .OrderBy(choice => Math.Abs(ParseHertz(choice) - HighPassFrequency))
                .First();
        }
        set
        {
            if (string.IsNullOrEmpty(value) || value == HighPassChoice)
            {
                return;
            }

            if (value == HighPassOff)
            {
                HighPassEnabled = false;
                return;
            }

            HighPassFrequency = ParseHertz(value);
            HighPassEnabled = true;
        }
    }

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
        ? "Noise removal is off in Technical controls. Select a level to enable it."
        : $"{WetMix:0%} filtered audio. Gentle keeps more room sound; Strong uses only the filtered signal.";

    public double VoiceLevelDb
    {
        get => 20 * Math.Log10(OutputGain);
        set => OutputGain = Math.Pow(10, value / 20);
    }

    public string VoiceLevelText => $"{VoiceLevelDb:+0;-0;0} dB";

    public void BeginBypass() => IsBypassActive = true;

    public void EndBypass() => IsBypassActive = false;

    private void ApplySuppression() => _engine.UpdateSuppression(BuildSuppressionOptions());

    private void PushMonitorOptions() => _ = PushMonitorOptionsAsync();

    private async Task PushMonitorOptionsAsync()
    {
        try
        {
            await _engine.UpdateMonitorAsync(BuildMonitorOptions());
        }
        catch (Exception exception)
        {
            _log.Write(exception, "Update monitor output");
        }
    }

    private SuppressionOptions BuildSuppressionOptions() => new(
        Enabled: SuppressionEnabled && !IsBypassActive,
        WetMix: (float)WetMix,
        VoiceGateEnabled: VoiceGateEnabled,
        VoiceThreshold: (float)VoiceSensitivity,
        VoiceHoldMilliseconds: (int)VoiceHoldMilliseconds,
        InputGain: (float)InputGain,
        OutputGain: (float)OutputGain,
        IsMuted: IsEffectivelyMuted,
        HighPassEnabled: HighPassEnabled,
        HighPassFrequency: (float)HighPassFrequency,
        LimiterEnabled: LimiterEnabled,
        AutoGainEnabled: AutoGainEnabled,
        AutoGainTargetDb: (float)AutoGainTargetDb,
        Character: BuildVoiceCharacterOptions(),
        ShoutGuard: BuildShoutGuardOptions());

    private MonitorOptions BuildMonitorOptions() =>
        new(MonitorEnabled, SelectedMonitor?.Id, (float)MonitorVolume);

    private EchoOptions BuildEchoOptions() =>
        new(EchoCancellationEnabled, SelectedEchoReference?.Id, (float)EchoStrength);

    private void PushEchoOptions() => _ = PushEchoOptionsAsync();

    private async Task PushEchoOptionsAsync()
    {
        try
        {
            await _engine.UpdateEchoAsync(BuildEchoOptions());
        }
        catch (Exception exception)
        {
            _log.Write(exception, "Update echo cancellation");
        }
    }

    private static string FormatGain(double gain) =>
        gain <= 0 ? "muted" : $"{20 * Math.Log10(gain):+0.0;-0.0;0.0} dB";

    private static double ParseHertz(string choice) =>
        double.Parse(choice.Replace(" Hz", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture);
}

using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Muted.App.Infrastructure;
using Muted.App.Services;
using Muted.Audio.Windows.Devices;
using Muted.Audio.Windows.Engine;
using Muted.Core.Audio;
using Muted.Core.Dsp;
using Muted.Core.Settings;

namespace Muted.App.ViewModels;

internal enum AppPage
{
    Dashboard,
    Audio,
    Shortcuts,
    Settings,
    Diagnostics,
    About
}

internal sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private const string NoDevicesError = "Windows did not report any usable audio devices.";
    private const string DeviceReadError = "Audio devices could not be read.";
    private const int MaximumRecoveryAttempts = 6;
    private const int TextTicksPerUpdate = 5;

    public const string VirtualCableUrl = "https://vb-audio.com/Cable/";

    private readonly RealtimeAudioEngine _engine;
    private readonly WasapiDeviceCatalog _deviceCatalog;
    private readonly JsonSettingsStore _settingsStore;
    private readonly StartupService _startupService;
    private readonly UpdateCoordinator _updateCoordinator;
    private readonly DiagnosticsRunner _diagnostics;
    private readonly FileLog _log;
    private readonly SynchronizationContext _uiContext;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly DispatcherTimer _meterTimer;
    private readonly DispatcherTimer _recoveryTimer;
    private AppSettings _settings = new();
    private AudioDeviceInfo? _selectedInput;
    private AudioDeviceInfo? _selectedOutput;
    private AudioDeviceInfo? _selectedMonitor;
    private bool _startWithWindows;
    private bool _minimizeToTray = true;
    private bool _startMinimized;
    private bool _autoRecoverDevices = true;
    private bool _startMuted;
    private bool _followDefaultInput;
    private AppTheme _theme = AppTheme.Dark;
    private bool _useSystemAccentColor;
    private bool _compactMode;
    private UpdateChannel _updateChannel = UpdateChannel.Stable;
    private AppPage _selectedPage = AppPage.Dashboard;
    private AudioEngineState _engineState;
    private string _statusText = "Stopped";
    private string? _errorMessage;
    private bool _initialized;
    private bool _isRefreshingDevices;
    private int _refreshPending;
    private int _deferredDeviceRefresh;
    private int _saveRevision;
    private int _disposed;
    private double _inputLevel;
    private double _outputLevel;
    private double _voiceProbability;
    private string _inputLevelDb = "–∞ dB";
    private string _outputLevelDb = "–∞ dB";
    private string _reductionText = "0.0 dB";
    private string _healthText = "Idle";
    private string _dropoutText = "No dropouts";
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
    private bool _isMuted;
    private bool _isBypassActive;
    private bool _pushToTalkHeld;
    private bool _pushToMuteHeld;
    private bool _hotkeysActive;
    private AudioProfile? _selectedProfile;
    private string? _activeProfileId;
    private string _newProfileName = string.Empty;
    private bool _isApplyingProfile;
    private bool _isDiagnosticsRunning;
    private string _diagnosticStatus = "Run the checks to verify your setup.";
    private string _updateStatusText = "Muted checks for updates when it starts.";
    private string? _skippedUpdateVersion;
    private bool _uiVisible = true;
    private int _textTickCounter;
    private bool _intendedRunning;
    private bool _isRecovering;
    private int _recoveryAttempt;

    public MainViewModel(
        RealtimeAudioEngine engine,
        WasapiDeviceCatalog deviceCatalog,
        JsonSettingsStore settingsStore,
        StartupService startupService,
        UpdateCoordinator updateCoordinator,
        FileLog log)
    {
        _engine = engine;
        _deviceCatalog = deviceCatalog;
        _settingsStore = settingsStore;
        _startupService = startupService;
        _updateCoordinator = updateCoordinator;
        _diagnostics = new DiagnosticsRunner(deviceCatalog);
        _log = log;
        _uiContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("MainViewModel must be created on the UI thread.");

        ToggleCommand = new AsyncRelayCommand(
            ToggleAsync,
            () => !IsBusy && (IsRunning || (HasDevices && IsRoutingReady)));
        ToggleMuteCommand = new RelayCommand(
            () => IsMuted = !IsMuted,
            () => IsRunning && !IsBusy);
        ToggleSuppressionCommand = new RelayCommand(
            () => SuppressionEnabled = !SuppressionEnabled,
            () => !IsBusy);
        ApplyProfileCommand = new AsyncRelayCommand(
            ApplySelectedProfileAsync,
            () => SelectedProfile is not null && !IsBusy);
        SaveProfileCommand = new RelayCommand(
            SaveCurrentProfile,
            () => !string.IsNullOrWhiteSpace(NewProfileName) &&
                Profiles.Count < AppSettings.MaximumProfiles);
        UpdateProfileCommand = new RelayCommand(
            UpdateSelectedProfile,
            () => SelectedProfile is not null && !IsBusy);
        DeleteProfileCommand = new RelayCommand(
            DeleteSelectedProfile,
            () => SelectedProfile is not null &&
                Profiles.Count > 1 &&
                !string.Equals(SelectedProfile.Id, ActiveProfileId, StringComparison.OrdinalIgnoreCase) &&
                !IsBusy);
        RunDiagnosticsCommand = new AsyncRelayCommand(
            RunDiagnosticsAsync,
            () => !IsBusy && !IsDiagnosticsRunning);
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);
        CopyDiagnosticsCommand = new RelayCommand(CopyDiagnosticsReport, () => DiagnosticChecks.Count > 0);
        OpenLogFolderCommand = new RelayCommand(() => Shell.OpenFolder(AppPaths.DataDirectory));
        OpenCableDownloadCommand = new RelayCommand(() => Shell.Open(VirtualCableUrl));
        ResetSettingsCommand = new AsyncRelayCommand(ResetSettingsAsync, () => !IsBusy);
        ShowPageCommand = new RelayCommand<AppPage>(page => SelectedPage = page);
        ClearHotkeyCommand = new RelayCommand<HotkeyBindingViewModel>(binding => binding?.Clear());
        ToggleCompactCommand = new RelayCommand(() => CompactMode = !CompactMode);

        _engine.StateChanged += OnEngineStateChanged;
        _engine.Faulted += OnEngineFaulted;
        _engine.MonitorFaulted += OnMonitorFaulted;
        _engine.EchoFaulted += OnEchoFaulted;
        _deviceCatalog.DevicesChanged += OnDevicesChanged;

        // Background priority: meters may drop a frame, the mouse may not wait for one.
        _meterTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _meterTimer.Tick += OnMeterTick;

        _recoveryTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _recoveryTimer.Tick += OnRecoveryTick;
    }

    public event EventHandler? ShowWindowRequested;

    public event EventHandler? HotkeysChanged;

    public ObservableCollection<AudioDeviceInfo> InputDevices { get; } = [];

    public ObservableCollection<AudioDeviceInfo> OutputDevices { get; } = [];

    public ObservableCollection<AudioProfile> Profiles { get; } = [];

    public ObservableCollection<DiagnosticCheck> DiagnosticChecks { get; } = [];

    public ObservableCollection<HotkeyBindingViewModel> Hotkeys { get; } = [];

    public AsyncRelayCommand ToggleCommand { get; }

    public RelayCommand ToggleMuteCommand { get; }

    public RelayCommand ToggleSuppressionCommand { get; }

    public AsyncRelayCommand ApplyProfileCommand { get; }

    public RelayCommand SaveProfileCommand { get; }

    public RelayCommand UpdateProfileCommand { get; }

    public RelayCommand DeleteProfileCommand { get; }

    public AsyncRelayCommand RunDiagnosticsCommand { get; }

    public AsyncRelayCommand CheckForUpdatesCommand { get; }

    public RelayCommand CopyDiagnosticsCommand { get; }

    public RelayCommand OpenLogFolderCommand { get; }

    public RelayCommand OpenCableDownloadCommand { get; }

    public AsyncRelayCommand ResetSettingsCommand { get; }

    public RelayCommand<AppPage> ShowPageCommand { get; }

    public RelayCommand<HotkeyBindingViewModel> ClearHotkeyCommand { get; }

    public RelayCommand ToggleCompactCommand { get; }

    /// <summary>Frame peaks the live waveform draws.</summary>
    public WaveformScope Scope => _engine.Scope;

    public AppPage SelectedPage
    {
        get => _selectedPage;
        set => SetProperty(ref _selectedPage, value);
    }

    public AudioDeviceInfo? SelectedInput
    {
        get => _selectedInput;
        set
        {
            if (SetProperty(ref _selectedInput, value) && _initialized)
            {
                if (!_isRefreshingDevices)
                {
                    QueueSave();
                }

                OnPropertyChanged(nameof(HasDevices));
                ToggleCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AudioDeviceInfo? SelectedOutput
    {
        get => _selectedOutput;
        set
        {
            if (SetProperty(ref _selectedOutput, value))
            {
                OnPropertyChanged(nameof(IsRoutingReady));
                OnPropertyChanged(nameof(HasDevices));
                ToggleCommand.RaiseCanExecuteChanged();
                if (_initialized && !_isRefreshingDevices)
                {
                    QueueSave();
                }
            }
        }
    }

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

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (_startWithWindows == value)
            {
                return;
            }

            try
            {
                if (_initialized)
                {
                    _startupService.SetEnabled(value, StartMinimized);
                }
            }
            catch (Exception exception)
            {
                _log.Write(exception, "Update startup setting");
                ErrorMessage = "Startup setting could not be updated.";
                return;
            }

            SetProperty(ref _startWithWindows, value);
            QueueSave();
        }
    }

    public bool StartMinimized
    {
        get => _startMinimized;
        set
        {
            if (_startMinimized == value)
            {
                return;
            }

            var previous = _startMinimized;
            SetProperty(ref _startMinimized, value);
            if (_initialized)
            {
                try
                {
                    if (StartWithWindows)
                    {
                        _startupService.SetEnabled(enabled: true, startMinimized: value);
                    }
                }
                catch (Exception exception)
                {
                    _startMinimized = previous;
                    OnPropertyChanged();
                    _log.Write(exception, "Update startup options");
                    ErrorMessage = "Startup setting could not be updated.";
                    return;
                }

                QueueSave();
            }
        }
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set
        {
            if (SetProperty(ref _minimizeToTray, value) && _initialized)
            {
                QueueSave();
            }
        }
    }

    public bool AutoRecoverDevices
    {
        get => _autoRecoverDevices;
        set
        {
            if (SetProperty(ref _autoRecoverDevices, value) && _initialized)
            {
                if (!value)
                {
                    CancelRecovery();
                }

                QueueSave();
            }
        }
    }

    public bool StartMuted
    {
        get => _startMuted;
        set
        {
            if (SetProperty(ref _startMuted, value) && _initialized)
            {
                QueueSave();
            }
        }
    }

    public bool FollowDefaultInput
    {
        get => _followDefaultInput;
        set
        {
            if (!SetProperty(ref _followDefaultInput, value) || !_initialized)
            {
                return;
            }

            OnPropertyChanged(nameof(CanSelectInput));
            if (value && IsStopped)
            {
                RefreshDevicesCore(SelectedInput?.Id, SelectedOutput?.Id);
            }

            QueueSave();
        }
    }

    public AppTheme Theme
    {
        get => _theme;
        set
        {
            if (SetProperty(ref _theme, value) && _initialized)
            {
                QueueSave();
            }
        }
    }

    public bool UseSystemAccentColor
    {
        get => _useSystemAccentColor;
        set
        {
            if (SetProperty(ref _useSystemAccentColor, value) && _initialized)
            {
                QueueSave();
            }
        }
    }

    public bool CompactMode
    {
        get => _compactMode;
        set
        {
            if (SetProperty(ref _compactMode, value) && _initialized)
            {
                QueueSave();
            }
        }
    }

    public UpdateChannel UpdateChannel
    {
        get => _updateChannel;
        set
        {
            if (SetProperty(ref _updateChannel, value) && _initialized)
            {
                QueueSave();
            }
        }
    }

    public AudioEngineState EngineState
    {
        get => _engineState;
        private set
        {
            if (SetProperty(ref _engineState, value))
            {
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(IsStopped));
                OnPropertyChanged(nameof(CanSelectInput));
                OnPropertyChanged(nameof(IsVoiceDetected));
                ToggleCommand.RaiseCanExecuteChanged();
                ToggleMuteCommand.RaiseCanExecuteChanged();
                ToggleSuppressionCommand.RaiseCanExecuteChanged();
                ApplyProfileCommand.RaiseCanExecuteChanged();
                UpdateProfileCommand.RaiseCanExecuteChanged();
                DeleteProfileCommand.RaiseCanExecuteChanged();
                RunDiagnosticsCommand.RaiseCanExecuteChanged();
                ResetSettingsCommand.RaiseCanExecuteChanged();
                UpdateMeterTimer();
            }
        }
    }

    public bool IsRunning => EngineState == AudioEngineState.Running;

    public bool IsBusy => EngineState is AudioEngineState.Starting or AudioEngineState.Stopping;

    public bool IsStopped => !IsRunning && !IsBusy;

    public bool CanSelectInput => IsStopped && !FollowDefaultInput;

    public bool HasDevices => SelectedInput is not null && SelectedOutput is not null;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsRoutingReady => WasapiDeviceCatalog.IsLikelyVirtualCable(SelectedOutput?.Name);

    /// <summary>False when Windows reports no virtual cable at all, which drives onboarding.</summary>
    public bool HasVirtualCable =>
        OutputDevices.Any(device => WasapiDeviceCatalog.IsLikelyVirtualCable(device.Name));

    public bool NeedsVirtualCableSetup => !HasVirtualCable;

    public bool IsRecovering
    {
        get => _isRecovering;
        private set => SetProperty(ref _isRecovering, value);
    }

    public double InputLevel
    {
        get => _inputLevel;
        private set => SetProperty(ref _inputLevel, value);
    }

    public double OutputLevel
    {
        get => _outputLevel;
        private set => SetProperty(ref _outputLevel, value);
    }

    public double VoiceProbability
    {
        get => _voiceProbability;
        private set
        {
            if (SetProperty(ref _voiceProbability, value))
            {
                OnPropertyChanged(nameof(IsVoiceDetected));
            }
        }
    }

    public bool IsVoiceDetected => IsRunning && VoiceProbability >= VoiceSensitivity;

    public string InputLevelDb
    {
        get => _inputLevelDb;
        private set => SetProperty(ref _inputLevelDb, value);
    }

    public string OutputLevelDb
    {
        get => _outputLevelDb;
        private set => SetProperty(ref _outputLevelDb, value);
    }

    public string ReductionText
    {
        get => _reductionText;
        private set => SetProperty(ref _reductionText, value);
    }

    public string HealthText
    {
        get => _healthText;
        private set => SetProperty(ref _healthText, value);
    }

    public string DropoutText
    {
        get => _dropoutText;
        private set => SetProperty(ref _dropoutText, value);
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

    public bool IsEffectivelyMuted => IsMuted || (IsPushToTalkArmed && !_pushToTalkHeld) || _pushToMuteHeld;

    public bool IsPushToTalkArmed => Hotkeys.Any(hotkey =>
        hotkey.Action == HotkeyAction.PushToTalk && hotkey.Enabled && hotkey.IsAssigned);

    public string MuteStatusText => IsEffectivelyMuted ? "Microphone muted" : "Microphone live";

    public bool HotkeysActive
    {
        get => _hotkeysActive;
        set => SetProperty(ref _hotkeysActive, value);
    }

    public AudioProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                ApplyProfileCommand.RaiseCanExecuteChanged();
                UpdateProfileCommand.RaiseCanExecuteChanged();
                DeleteProfileCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? ActiveProfileId
    {
        get => _activeProfileId;
        private set
        {
            if (SetProperty(ref _activeProfileId, value))
            {
                OnPropertyChanged(nameof(ActiveProfileName));
                DeleteProfileCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ActiveProfileName => Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, ActiveProfileId, StringComparison.OrdinalIgnoreCase))?.Name
        ?? "No profile";

    public string NewProfileName
    {
        get => _newProfileName;
        set
        {
            if (SetProperty(ref _newProfileName, value))
            {
                SaveProfileCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsDiagnosticsRunning
    {
        get => _isDiagnosticsRunning;
        private set
        {
            if (SetProperty(ref _isDiagnosticsRunning, value))
            {
                RunDiagnosticsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string DiagnosticStatus
    {
        get => _diagnosticStatus;
        private set => SetProperty(ref _diagnosticStatus, value);
    }

    public string UpdateStatusText
    {
        get => _updateStatusText;
        private set => SetProperty(ref _updateStatusText, value);
    }

    public string? SkippedUpdateVersion => _skippedUpdateVersion;

    public async Task InitializeAsync(AppSettings settings)
    {
        LoadSettings(settings);

        try
        {
            _startupService.SetEnabled(_startWithWindows, _startMinimized);
        }
        catch (Exception exception)
        {
            _startWithWindows = false;
            _log.Write(exception, "Restore startup setting");
            ErrorMessage = "Startup setting could not be restored.";
        }

        RefreshDevicesCore(
            _settings.InputDeviceId,
            _settings.OutputDeviceId,
            _settings.MonitorDeviceId,
            _settings.EchoReferenceDeviceId);
        _initialized = true;

        if (_settings.WasRunningOnExit)
        {
            var savedInputAvailable = !string.IsNullOrWhiteSpace(_settings.InputDeviceId) &&
                InputDevices.Any(device => device.Id == _settings.InputDeviceId);
            var savedOutputAvailable = !string.IsNullOrWhiteSpace(_settings.OutputDeviceId) &&
                OutputDevices.Any(device => device.Id == _settings.OutputDeviceId);
            if ((savedInputAvailable || FollowDefaultInput) && savedOutputAvailable &&
                HasDevices && IsRoutingReady)
            {
                _intendedRunning = true;
                await StartAsync();
            }
            else
            {
                ErrorMessage = "Previous audio devices are unavailable, so Muted stayed stopped.";
            }
        }
    }

    private void LoadSettings(AppSettings settings)
    {
        _settings = settings.Normalize();
        _startWithWindows = _settings.StartWithWindows;
        _startMinimized = _settings.StartMinimized;
        _minimizeToTray = _settings.MinimizeToTray;
        _autoRecoverDevices = _settings.AutoRecoverDevices;
        _startMuted = _settings.StartMuted;
        _followDefaultInput = _settings.FollowDefaultInput;
        _theme = _settings.Theme;
        _useSystemAccentColor = _settings.UseSystemAccentColor;
        _compactMode = _settings.CompactMode;
        _updateChannel = _settings.UpdateChannel;
        _suppressionEnabled = _settings.SuppressionEnabled;
        _wetMix = _settings.WetMix;
        _voiceGateEnabled = _settings.VoiceGateEnabled;
        _voiceSensitivity = _settings.VoiceThreshold;
        _voiceHoldMilliseconds = _settings.VoiceHoldMilliseconds;
        _inputGain = _settings.InputGain;
        _outputGain = _settings.OutputGain;
        _highPassEnabled = _settings.HighPassEnabled;
        _highPassFrequency = _settings.HighPassFrequency;
        _limiterEnabled = _settings.LimiterEnabled;
        _autoGainEnabled = _settings.AutoGainEnabled;
        _autoGainTargetDb = _settings.AutoGainTargetDb;
        _echoEnabled = _settings.EchoCancellationEnabled;
        _echoStrength = _settings.EchoStrength;
        _monitorEnabled = _settings.MonitorEnabled;
        _monitorVolume = _settings.MonitorVolume;
        _targetLatencyMilliseconds = _settings.TargetLatencyMilliseconds;
        _isMuted = _settings.StartMuted;
        _skippedUpdateVersion = _settings.SkippedUpdateVersion;

        Profiles.Clear();
        foreach (var profile in _settings.Profiles)
        {
            Profiles.Add(profile);
        }

        _selectedProfile = Profiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, _settings.ActiveProfileId, StringComparison.OrdinalIgnoreCase))
            ?? Profiles.FirstOrDefault();
        _activeProfileId = _selectedProfile?.Id;

        Hotkeys.Clear();
        foreach (var binding in _settings.Hotkeys)
        {
            Hotkeys.Add(new HotkeyBindingViewModel(binding, OnHotkeyBindingChanged));
        }

        RaiseSettingsPropertiesChanged();
    }

    private void RaiseSettingsPropertiesChanged()
    {
        string[] names =
        [
            nameof(StartWithWindows), nameof(StartMinimized), nameof(MinimizeToTray),
            nameof(AutoRecoverDevices), nameof(StartMuted), nameof(FollowDefaultInput),
            nameof(Theme), nameof(UseSystemAccentColor), nameof(CompactMode),
            nameof(UpdateChannel),
            nameof(SuppressionEnabled), nameof(SuppressionStatusText), nameof(WetMix),
            nameof(WetMixText), nameof(VoiceGateEnabled), nameof(VoiceSensitivity),
            nameof(VoiceSensitivityText), nameof(VoiceHoldMilliseconds), nameof(VoiceHoldText),
            nameof(InputGain), nameof(InputGainText), nameof(OutputGain), nameof(OutputGainText),
            nameof(HighPassEnabled), nameof(HighPassFrequency), nameof(HighPassText),
            nameof(LimiterEnabled), nameof(AutoGainEnabled), nameof(AutoGainTargetDb),
            nameof(AutoGainTargetText), nameof(EchoCancellationEnabled), nameof(EchoStrength),
            nameof(EchoStrengthText), nameof(MonitorEnabled), nameof(MonitorVolume),
            nameof(MonitorVolumeText), nameof(TargetLatencyMilliseconds), nameof(LatencyText),
            nameof(IsMuted), nameof(MuteStatusText), nameof(IsEffectivelyMuted),
            nameof(SelectedProfile), nameof(ActiveProfileId), nameof(ActiveProfileName),
            nameof(CanSelectInput), nameof(IsPushToTalkArmed)
        ];

        foreach (var name in names)
        {
            OnPropertyChanged(name);
        }
    }

    private async Task ToggleAsync()
    {
        if (IsRunning)
        {
            _intendedRunning = false;
            CancelRecovery();
            await StopAsync();
        }
        else
        {
            _intendedRunning = true;
            await StartAsync();
        }
    }

    public void BeginBypass() => IsBypassActive = true;

    public void EndBypass() => IsBypassActive = false;

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

    private async Task ApplySelectedProfileAsync()
    {
        if (SelectedProfile is not null)
        {
            await ApplyProfileAsync(SelectedProfile.Id);
        }
    }

    public async Task ApplyProfileAsync(string profileId)
    {
        var profile = Profiles.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, profileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null || IsBusy)
        {
            return;
        }

        var wasRunning = IsRunning;
        if (wasRunning)
        {
            if (!await StopAsync())
            {
                return;
            }
        }

        _isApplyingProfile = true;
        try
        {
            SelectedProfile = profile;
            if (!string.IsNullOrWhiteSpace(profile.InputDeviceId))
            {
                SelectedInput = InputDevices.FirstOrDefault(device => device.Id == profile.InputDeviceId)
                    ?? SelectedInput;
            }

            if (!string.IsNullOrWhiteSpace(profile.OutputDeviceId))
            {
                SelectedOutput = OutputDevices.FirstOrDefault(device => device.Id == profile.OutputDeviceId)
                    ?? SelectedOutput;
            }

            SuppressionEnabled = profile.SuppressionEnabled;
            WetMix = profile.WetMix;
            VoiceGateEnabled = profile.VoiceGateEnabled;
            VoiceSensitivity = profile.VoiceThreshold;
            VoiceHoldMilliseconds = profile.VoiceHoldMilliseconds;
            InputGain = profile.InputGain;
            OutputGain = profile.OutputGain;
            HighPassEnabled = profile.HighPassEnabled;
            HighPassFrequency = profile.HighPassFrequency;
            LimiterEnabled = profile.LimiterEnabled;
            AutoGainEnabled = profile.AutoGainEnabled;
            AutoGainTargetDb = profile.AutoGainTargetDb;
            ApplySuppression();
            ActiveProfileId = profile.Id;
        }
        finally
        {
            _isApplyingProfile = false;
        }

        if (wasRunning)
        {
            await StartAsync();
        }

        await TrySaveAsync();
    }

    private AudioProfile BuildProfile(string id, string name) => new AudioProfile
    {
        Id = id,
        Name = name,
        InputDeviceId = SelectedInput?.Id,
        OutputDeviceId = SelectedOutput?.Id,
        SuppressionEnabled = SuppressionEnabled,
        WetMix = (float)WetMix,
        VoiceGateEnabled = VoiceGateEnabled,
        VoiceThreshold = (float)VoiceSensitivity,
        VoiceHoldMilliseconds = (int)VoiceHoldMilliseconds,
        InputGain = (float)InputGain,
        OutputGain = (float)OutputGain,
        HighPassEnabled = HighPassEnabled,
        HighPassFrequency = (float)HighPassFrequency,
        LimiterEnabled = LimiterEnabled,
        AutoGainEnabled = AutoGainEnabled,
        AutoGainTargetDb = (float)AutoGainTargetDb
    }.Normalize();

    private void SaveCurrentProfile()
    {
        var name = NewProfileName.Trim();
        if (name.Length == 0 || Profiles.Count >= AppSettings.MaximumProfiles)
        {
            return;
        }

        if (name.Length > AudioProfile.MaximumNameLength)
        {
            name = name[..AudioProfile.MaximumNameLength];
        }

        var profile = BuildProfile(Guid.NewGuid().ToString("N"), name);
        Profiles.Add(profile);
        SelectedProfile = profile;
        ActiveProfileId = profile.Id;
        NewProfileName = string.Empty;
        OnPropertyChanged(nameof(Profiles));
        DeleteProfileCommand.RaiseCanExecuteChanged();
        SaveProfileCommand.RaiseCanExecuteChanged();
        QueueSave();
    }

    /// <summary>Writes the settings that are live right now back into the selected profile.</summary>
    private void UpdateSelectedProfile()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var index = Profiles.IndexOf(SelectedProfile);
        if (index < 0)
        {
            return;
        }

        var updated = BuildProfile(SelectedProfile.Id, SelectedProfile.Name);
        Profiles[index] = updated;
        SelectedProfile = updated;
        ActiveProfileId = updated.Id;
        OnPropertyChanged(nameof(Profiles));
        OnPropertyChanged(nameof(ActiveProfileName));
        QueueSave();
    }

    private void DeleteSelectedProfile()
    {
        if (SelectedProfile is null || Profiles.Count <= 1 ||
            string.Equals(SelectedProfile.Id, ActiveProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Profiles.Remove(SelectedProfile);
        SelectedProfile = Profiles[0];
        OnPropertyChanged(nameof(Profiles));
        DeleteProfileCommand.RaiseCanExecuteChanged();
        SaveProfileCommand.RaiseCanExecuteChanged();
        QueueSave();
    }

    private async Task RunDiagnosticsAsync()
    {
        IsDiagnosticsRunning = true;
        DiagnosticChecks.Clear();
        CopyDiagnosticsCommand.RaiseCanExecuteChanged();
        DiagnosticStatus = "Checking devices and audio signal…";
        var startedForTest = false;

        try
        {
            if (IsStopped)
            {
                RefreshDevicesCore(SelectedInput?.Id, SelectedOutput?.Id, SelectedMonitor?.Id);
            }

            foreach (var check in _diagnostics.CheckDevices(SelectedInput, SelectedOutput, IsRoutingReady))
            {
                DiagnosticChecks.Add(check);
            }

            DiagnosticChecks.Add(_diagnostics.CheckEchoReference(
                SelectedEchoReference,
                EchoCancellationEnabled));
            DiagnosticChecks.Add(_diagnostics.CheckRuntime());

            if (HasDevices && IsRoutingReady)
            {
                if (!IsRunning)
                {
                    startedForTest = await StartAsync();
                    if (!startedForTest)
                    {
                        DiagnosticChecks.Add(new DiagnosticCheck(
                            "Audio pipeline",
                            ErrorMessage ?? "Muted could not start the audio pipeline.",
                            DiagnosticSeverity.Failed));
                    }
                }

                if (IsRunning || startedForTest)
                {
                    var baselineUnderruns = _engine.Metrics.OutputUnderrunSamples;
                    var peak = 0f;
                    var processingLoad = 0f;
                    for (var index = 0; index < 20; index++)
                    {
                        await Task.Delay(100);
                        var sample = _engine.Metrics;
                        peak = Math.Max(peak, sample.InputPeak);
                        processingLoad = Math.Max(processingLoad, sample.ProcessingLoad);
                    }

                    var underruns = Math.Max(0, _engine.Metrics.OutputUnderrunSamples - baselineUnderruns);
                    foreach (var check in _diagnostics.CheckSignal(peak, processingLoad, underruns))
                    {
                        DiagnosticChecks.Add(check);
                    }
                }
            }

            DiagnosticStatus = DiagnosticsRunner.Summarize(DiagnosticChecks);
        }
        catch (Exception exception)
        {
            _log.Write(exception, "Run diagnostics");
            DiagnosticChecks.Add(new DiagnosticCheck(
                "Diagnostic interrupted",
                FriendlyAudioError(exception),
                DiagnosticSeverity.Failed));
            DiagnosticStatus = "The checks could not be completed.";
        }
        finally
        {
            if (startedForTest)
            {
                await StopAsync();
            }

            IsDiagnosticsRunning = false;
            CopyDiagnosticsCommand.RaiseCanExecuteChanged();
        }
    }

    private void CopyDiagnosticsReport()
    {
        try
        {
            System.Windows.Clipboard.SetText(DiagnosticsRunner.BuildReport(
                DiagnosticChecks,
                SelectedInput,
                SelectedOutput,
                _engine.Metrics));
            DiagnosticStatus = "Report copied to the clipboard.";
        }
        catch (Exception exception)
        {
            _log.Write(exception, "Copy diagnostics");
            DiagnosticStatus = "The report could not be copied.";
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        UpdateStatusText = "Checking for updates…";
        var result = await _updateCoordinator.CheckAndPromptAsync(
            showNoUpdateMessage: true,
            System.Windows.Application.Current.MainWindow,
            _skippedUpdateVersion,
            UpdateChannel);
        ReportUpdateStatus(result);
    }

    public void ReportUpdateStatus(UpdatePromptResult result)
    {
        UpdateStatusText = result.Message;
        if (result.SkippedVersion is not null && result.SkippedVersion != _skippedUpdateVersion)
        {
            _skippedUpdateVersion = result.SkippedVersion;
            QueueSave();
        }
    }

    private async Task ResetSettingsAsync()
    {
        var confirmed = System.Windows.MessageBox.Show(
            System.Windows.Application.Current.MainWindow,
            "Reset every Muted setting, profile and shortcut to its default?",
            "Reset Muted",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;
        if (!confirmed)
        {
            return;
        }

        _intendedRunning = false;
        CancelRecovery();
        if (IsRunning)
        {
            await StopAsync();
        }

        _initialized = false;
        try
        {
            LoadSettings(new AppSettings());
            RefreshDevicesCore(null, null, null, null);
        }
        finally
        {
            _initialized = true;
        }

        try
        {
            _startupService.SetEnabled(StartWithWindows, StartMinimized);
        }
        catch (Exception exception)
        {
            _log.Write(exception, "Reset startup setting");
        }

        ApplySuppression();
        HotkeysChanged?.Invoke(this, EventArgs.Empty);
        await TrySaveAsync();
        ErrorMessage = null;
        UpdateStatusText = "Settings were reset to their defaults.";
    }

    private async Task<bool> StartAsync()
    {
        if (!HasDevices)
        {
            ErrorMessage = "Select an input and output device first.";
            return false;
        }

        if (!IsRoutingReady)
        {
            ErrorMessage = "Select a virtual cable output to prevent speaker feedback.";
            return false;
        }

        ErrorMessage = null;
        if (StartMuted && !IsRunning && !_isMuted)
        {
            _isMuted = true;
            OnPropertyChanged(nameof(IsMuted));
            RaiseMuteChanged();
        }

        try
        {
            var options = new AudioEngineOptions(
                SelectedInput?.Id,
                SelectedOutput?.Id,
                TargetLatencyMilliseconds,
                BuildSuppressionOptions(),
                BuildMonitorOptions(),
                BuildEchoOptions());
            await _engine.StartAsync(options);
        }
        catch (DllNotFoundException exception)
        {
            _log.Write(exception, "Load RNNoise");
            ErrorMessage = "rnnoise.dll is missing. Repair or reinstall Muted.";
            return false;
        }
        catch (Exception exception)
        {
            _log.Write(exception, "Start audio engine");
            ErrorMessage = FriendlyAudioError(exception);
            return false;
        }

        _recoveryAttempt = 0;
        IsRecovering = false;
        await TrySaveAsync();
        return true;
    }

    private async Task<bool> StopAsync()
    {
        try
        {
            await _engine.StopAsync();
        }
        catch (Exception exception)
        {
            _log.Write(exception, "Stop audio engine");
            ErrorMessage = "The audio pipeline could not be stopped cleanly.";
            return false;
        }

        IsMuted = StartMuted;
        _pushToMuteHeld = false;
        await TrySaveAsync();
        return true;
    }

    private void RefreshDevicesCore(
        string? preferredInputId,
        string? preferredOutputId,
        string? preferredMonitorId = null,
        string? preferredEchoReferenceId = null)
    {
        var wasRefreshing = _isRefreshingDevices;
        _isRefreshingDevices = true;
        try
        {
            var inputs = _deviceCatalog.GetInputDevices();
            var outputs = _deviceCatalog.GetOutputDevices();

            InputDevices.Clear();
            foreach (var input in inputs)
            {
                InputDevices.Add(input);
            }

            OutputDevices.Clear();
            foreach (var output in outputs)
            {
                OutputDevices.Add(output);
            }

            SelectedInput = FollowDefaultInput
                ? inputs.FirstOrDefault(device => device.IsDefault) ?? inputs.FirstOrDefault()
                : inputs.FirstOrDefault(device => device.Id == preferredInputId)
                    ?? inputs.FirstOrDefault(device => device.IsDefault)
                    ?? inputs.FirstOrDefault();

            SelectedOutput = outputs.FirstOrDefault(device => device.Id == preferredOutputId)
                ?? outputs.FirstOrDefault(device => WasapiDeviceCatalog.IsLikelyVirtualCable(device.Name))
                ?? outputs.FirstOrDefault(device => device.IsDefault)
                ?? outputs.FirstOrDefault();

            // The reference has to be what you actually listen on, never the cable.
            var referenceId = preferredEchoReferenceId ?? SelectedEchoReference?.Id;
            SelectedEchoReference = outputs.FirstOrDefault(device => device.Id == referenceId)
                ?? outputs.FirstOrDefault(device =>
                    device.IsDefault && !WasapiDeviceCatalog.IsLikelyVirtualCable(device.Name))
                ?? outputs.FirstOrDefault(device => !WasapiDeviceCatalog.IsLikelyVirtualCable(device.Name));

            var monitorId = preferredMonitorId ?? SelectedMonitor?.Id;
            SelectedMonitor = outputs.FirstOrDefault(device => device.Id == monitorId)
                ?? outputs.FirstOrDefault(device =>
                    device.IsDefault && !WasapiDeviceCatalog.IsLikelyVirtualCable(device.Name))
                ?? outputs.FirstOrDefault(device => !WasapiDeviceCatalog.IsLikelyVirtualCable(device.Name));

            if (!HasDevices)
            {
                ErrorMessage = NoDevicesError;
            }
            else if (ErrorMessage is NoDevicesError or DeviceReadError)
            {
                ErrorMessage = null;
            }

            OnPropertyChanged(nameof(HasDevices));
            OnPropertyChanged(nameof(HasVirtualCable));
            OnPropertyChanged(nameof(NeedsVirtualCableSetup));
            ToggleCommand.RaiseCanExecuteChanged();
        }
        catch (Exception exception)
        {
            _log.Write(exception, "Refresh audio devices");
            ErrorMessage = DeviceReadError;
        }
        finally
        {
            _isRefreshingDevices = wasRefreshing;
        }
    }

    private void OnDevicesChanged(object? sender, EventArgs eventArgs)
    {
        if (Interlocked.Exchange(ref _refreshPending, 1) != 0)
        {
            return;
        }

        _uiContext.Post(async _ =>
        {
            try
            {
                await Task.Delay(250);
                if (IsStopped)
                {
                    RefreshDevicesCore(SelectedInput?.Id, SelectedOutput?.Id);

                    // A device coming back is the best moment to retry a lost pipeline.
                    if (_intendedRunning && AutoRecoverDevices && HasDevices && IsRoutingReady)
                    {
                        TriggerRecovery(TimeSpan.FromMilliseconds(400));
                    }
                }
                else
                {
                    Interlocked.Exchange(ref _deferredDeviceRefresh, 1);
                    await FollowDefaultInputIfChangedAsync();
                }
            }
            finally
            {
                Interlocked.Exchange(ref _refreshPending, 0);
            }
        }, null);
    }

    /// <summary>Moves a running pipeline over to the new Windows default microphone.</summary>
    private async Task FollowDefaultInputIfChangedAsync()
    {
        if (!FollowDefaultInput || !IsRunning || _isApplyingProfile)
        {
            return;
        }

        AudioDeviceInfo? newDefault;
        try
        {
            newDefault = _deviceCatalog.GetInputDevices().FirstOrDefault(device => device.IsDefault);
        }
        catch (Exception exception)
        {
            _log.Write(exception, "Read default microphone");
            return;
        }

        if (newDefault is null || newDefault.Id == SelectedInput?.Id)
        {
            return;
        }

        _log.WriteMessage($"Following the new default microphone: {newDefault.Name}.");
        if (!await StopAsync())
        {
            return;
        }

        RefreshDevicesCore(newDefault.Id, SelectedOutput?.Id);
        await StartAsync();
    }

    private void OnEngineStateChanged(object? sender, AudioEngineState state) =>
        _uiContext.Post(_ =>
        {
            EngineState = state;
            StatusText = state switch
            {
                AudioEngineState.Starting => "Starting…",
                AudioEngineState.Running => "Active",
                AudioEngineState.Stopping => "Stopping…",
                AudioEngineState.Faulted => IsRecovering ? "Reconnecting…" : "Audio error",
                _ => IsRecovering ? "Reconnecting…" : "Stopped"
            };

            if (state is AudioEngineState.Stopped or AudioEngineState.Faulted &&
                Interlocked.Exchange(ref _deferredDeviceRefresh, 0) != 0)
            {
                RefreshDevicesCore(SelectedInput?.Id, SelectedOutput?.Id);
            }
        }, null);

    private void OnEngineFaulted(object? sender, Exception exception) =>
        _uiContext.Post(_ =>
        {
            _log.Write(exception, "Realtime audio pipeline");
            ErrorMessage = FriendlyAudioError(exception);
            if (_intendedRunning && AutoRecoverDevices)
            {
                TriggerRecovery(RecoveryDelay());
            }
        }, null);

    private void OnMonitorFaulted(object? sender, Exception exception) =>
        _uiContext.Post(_ =>
        {
            _log.Write(exception, "Monitor output");
            _monitorEnabled = false;
            OnPropertyChanged(nameof(MonitorEnabled));
            OnPropertyChanged(nameof(MonitorFeedbackWarning));
            ErrorMessage = "Monitoring stopped because that output could not be used.";
            QueueSave();
        }, null);

    private void OnEchoFaulted(object? sender, Exception exception) =>
        _uiContext.Post(_ =>
        {
            _log.Write(exception, "Echo cancellation");
            _echoEnabled = false;
            OnPropertyChanged(nameof(EchoCancellationEnabled));
            ErrorMessage = exception is NotSupportedException
                ? exception.Message
                : "Echo cancellation stopped because that output could not be captured.";
            QueueSave();
        }, null);

    private TimeSpan RecoveryDelay() => TimeSpan.FromSeconds(Math.Min(16, 1 << _recoveryAttempt));

    private void TriggerRecovery(TimeSpan delay)
    {
        if (_recoveryAttempt >= MaximumRecoveryAttempts)
        {
            IsRecovering = false;
            return;
        }

        IsRecovering = true;
        StatusText = "Reconnecting…";
        _recoveryTimer.Stop();
        _recoveryTimer.Interval = delay;
        _recoveryTimer.Start();
    }

    private void CancelRecovery()
    {
        _recoveryTimer.Stop();
        _recoveryAttempt = 0;
        IsRecovering = false;
    }

    private async void OnRecoveryTick(object? sender, EventArgs eventArgs)
    {
        _recoveryTimer.Stop();
        if (!_intendedRunning || !AutoRecoverDevices || IsRunning || IsBusy ||
            Volatile.Read(ref _disposed) != 0)
        {
            IsRecovering = false;
            return;
        }

        _recoveryAttempt++;
        RefreshDevicesCore(SelectedInput?.Id, SelectedOutput?.Id);
        if (!HasDevices || !IsRoutingReady)
        {
            TriggerRecovery(RecoveryDelay());
            return;
        }

        _log.WriteMessage($"Recovery attempt {_recoveryAttempt} after an audio fault.");
        if (await StartAsync())
        {
            CancelRecovery();
        }
        else
        {
            TriggerRecovery(RecoveryDelay());
        }
    }

    private bool SetAudioProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName))
        {
            return false;
        }

        if (!_initialized)
        {
            return true;
        }

        ApplySuppression();
        if (!_isApplyingProfile)
        {
            QueueSave();
        }

        return true;
    }

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
        AutoGainTargetDb: (float)AutoGainTargetDb);

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

    private AppSettings BuildSettings(bool? wasRunning = null) => new()
    {
        InputDeviceId = SelectedInput?.Id,
        OutputDeviceId = SelectedOutput?.Id,
        FollowDefaultInput = FollowDefaultInput,
        SuppressionEnabled = SuppressionEnabled,
        WetMix = (float)WetMix,
        VoiceGateEnabled = VoiceGateEnabled,
        VoiceThreshold = (float)VoiceSensitivity,
        VoiceHoldMilliseconds = (int)VoiceHoldMilliseconds,
        InputGain = (float)InputGain,
        OutputGain = (float)OutputGain,
        HighPassEnabled = HighPassEnabled,
        HighPassFrequency = (float)HighPassFrequency,
        LimiterEnabled = LimiterEnabled,
        AutoGainEnabled = AutoGainEnabled,
        AutoGainTargetDb = (float)AutoGainTargetDb,
        EchoCancellationEnabled = EchoCancellationEnabled,
        EchoReferenceDeviceId = SelectedEchoReference?.Id,
        EchoStrength = (float)EchoStrength,
        MonitorEnabled = MonitorEnabled,
        MonitorDeviceId = SelectedMonitor?.Id,
        MonitorVolume = (float)MonitorVolume,
        TargetLatencyMilliseconds = TargetLatencyMilliseconds,
        StartWithWindows = StartWithWindows,
        StartMinimized = StartMinimized,
        MinimizeToTray = MinimizeToTray,
        AutoRecoverDevices = AutoRecoverDevices,
        StartMuted = StartMuted,
        WasRunningOnExit = wasRunning ?? IsRunning,
        Theme = Theme,
        UseSystemAccentColor = UseSystemAccentColor,
        CompactMode = CompactMode,
        UpdateChannel = UpdateChannel,
        SkippedUpdateVersion = _skippedUpdateVersion,
        ActiveProfileId = ActiveProfileId,
        Hotkeys = GetHotkeyBindings(),
        Profiles = Profiles.ToArray()
    };

    private void QueueSave()
    {
        if (!_initialized || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var snapshot = BuildSettings();
        var revision = Interlocked.Increment(ref _saveRevision);
        _ = SaveQueuedAsync(snapshot, revision);
    }

    private async Task SaveQueuedAsync(AppSettings snapshot, int revision)
    {
        try
        {
            await Task.Delay(150);
            await _saveGate.WaitAsync();
            try
            {
                if (revision != Volatile.Read(ref _saveRevision) || Volatile.Read(ref _disposed) != 0)
                {
                    return;
                }

                await _settingsStore.SaveAsync(snapshot);
                if (revision == Volatile.Read(ref _saveRevision))
                {
                    _settings = snapshot;
                }
            }
            finally
            {
                _saveGate.Release();
            }
        }
        catch (Exception exception)
        {
            _log.Write(exception, "Save settings");
        }
    }

    private async Task SaveAsync(bool? wasRunning = null)
    {
        var snapshot = BuildSettings(wasRunning);
        var revision = Interlocked.Increment(ref _saveRevision);
        await _saveGate.WaitAsync();
        try
        {
            await _settingsStore.SaveAsync(snapshot);
            if (revision == Volatile.Read(ref _saveRevision))
            {
                _settings = snapshot;
            }
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private async Task TrySaveAsync()
    {
        try
        {
            await SaveAsync();
        }
        catch (Exception exception)
        {
            _log.Write(exception, "Save settings");
        }
    }

    public void SetUiVisible(bool visible)
    {
        _uiVisible = visible;
        UpdateMeterTimer();
    }

    private void UpdateMeterTimer()
    {
        var shouldRun = IsRunning && _uiVisible && Volatile.Read(ref _disposed) == 0;
        if (shouldRun && !_meterTimer.IsEnabled)
        {
            _meterTimer.Start();
        }
        else if (!shouldRun && _meterTimer.IsEnabled)
        {
            _meterTimer.Stop();
        }

        if (!IsRunning)
        {
            _textTickCounter = TextTicksPerUpdate;
            InputLevel = 0;
            OutputLevel = 0;
            VoiceProbability = 0;
            InputLevelDb = "–∞ dB";
            OutputLevelDb = "–∞ dB";
            ReductionText = "0.0 dB";
            EchoReductionText = "0.0 dB";
            HealthText = "Idle";
            DropoutText = "No dropouts";
        }
    }

    private void OnMeterTick(object? sender, EventArgs eventArgs)
    {
        var metrics = _engine.Metrics;
        InputLevel = Smooth(InputLevel, metrics.InputPeak);
        OutputLevel = Smooth(OutputLevel, metrics.OutputPeak);
        VoiceProbability = Smooth(VoiceProbability, metrics.VoiceProbability, decay: 0.10);

        // Meters move at the timer rate; the numbers next to them are read, not watched,
        // so they refresh five times a second instead of formatting five strings per tick.
        if (++_textTickCounter < TextTicksPerUpdate)
        {
            return;
        }

        _textTickCounter = 0;
        InputLevelDb = FormatDecibels(InputLevel);
        OutputLevelDb = FormatDecibels(OutputLevel);
        ReductionText = $"{metrics.NoiseReductionDb:0.0} dB";
        EchoReductionText = metrics.EchoActive
            ? $"{metrics.EchoReductionDb:0.0} dB"
            : "not running";
        HealthText = $"{metrics.BufferedMilliseconds:0} ms buffer · {metrics.ProcessingLoad * 100:0}% load";

        var dropped = metrics.DroppedInputSamples + metrics.DroppedOutputSamples;
        DropoutText = dropped == 0 && metrics.OutputUnderrunSamples == 0
            ? "No dropouts"
            : $"{metrics.OutputUnderrunSamples} underruns · {dropped} dropped";
    }

    private static string FormatDecibels(double level) =>
        level <= 0.001 ? "–∞ dB" : $"{20 * Math.Log10(level):0.0} dB";

    private static string FormatGain(double gain) =>
        gain <= 0 ? "muted" : $"{20 * Math.Log10(gain):+0.0;-0.0;0.0} dB";

    // Peaks rise instantly and fall gradually so the meters read naturally.
    private static double Smooth(double current, double target, double decay = 0.05)
    {
        var clamped = Math.Clamp(target, 0d, 1d);
        return clamped >= current ? clamped : Math.Max(clamped, current - decay);
    }

    private static string FriendlyAudioError(Exception exception)
    {
        var root = exception;
        while (root.InnerException is not null)
        {
            root = root.InnerException;
        }

        return root switch
        {
            UnauthorizedAccessException => "Windows blocked microphone access. Check Privacy > Microphone.",
            COMException => "The audio device is busy, disconnected, or does not support this format.",
            _ => root.Message
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _meterTimer.Stop();
        _meterTimer.Tick -= OnMeterTick;
        _recoveryTimer.Stop();
        _recoveryTimer.Tick -= OnRecoveryTick;
        _deviceCatalog.DevicesChanged -= OnDevicesChanged;
        _engine.StateChanged -= OnEngineStateChanged;
        _engine.Faulted -= OnEngineFaulted;
        _engine.MonitorFaulted -= OnMonitorFaulted;
        _engine.EchoFaulted -= OnEchoFaulted;
        var wasRunning = IsRunning;
        try
        {
            await SaveAsync(wasRunning);
        }
        catch (Exception exception)
        {
            _log.Write(exception, "Save shutdown settings");
        }

        await _engine.DisposeAsync();
    }
}

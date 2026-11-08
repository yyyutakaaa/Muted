using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Muted.App.Infrastructure;
using Muted.App.Services;
using Muted.Audio.Windows.Devices;
using Muted.Audio.Windows.Engine;
using Muted.Core.Settings;

namespace Muted.App.ViewModels;

internal enum AppPage
{
    Dashboard,
    Audio,
    Presets,
    Shortcuts,
    Settings,
    Diagnostics,
    About,
    Advanced
}

internal sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly RealtimeAudioEngine _engine;
    private readonly WasapiDeviceCatalog _deviceCatalog;
    private readonly JsonSettingsStore _settingsStore;
    private readonly StartupService _startupService;
    private readonly UpdateCoordinator _updateCoordinator;
    private readonly DiagnosticsRunner _diagnostics;
    private readonly FileLog _log;
    private readonly SynchronizationContext _uiContext;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private AppSettings _settings = new();
    private bool _initialized;
    private int _saveRevision;
    private int _disposed;

    public AsyncRelayCommand ResetSettingsCommand { get; }

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
        InitializePresetCommands();
        InitializeRecordingCommands();
        InitializeTuning();
        InitializeActivityTimer();
        InitializeSetupGuide();
        InitializeAutoProfiles();
        InitializeStatistics();
        InitializeListening();
        InitializeDerivedProperties();
    }

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
        StartListening();
        StartAutoProfiles();
        RaiseSetupChanged();

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
        LoadFeedbackSettings(_settings);
        _startWithWindows = _settings.StartWithWindows;
        _startMinimized = _settings.StartMinimized;
        _minimizeToTray = _settings.MinimizeToTray;
        _autoRecoverDevices = _settings.AutoRecoverDevices;
        _startMuted = _settings.StartMuted;
        _followDefaultInput = _settings.FollowDefaultInput;
        _theme = _settings.Theme;
        _useSystemAccentColor = _settings.UseSystemAccentColor;
        _compactMode = _settings.CompactMode;
        _advancedMode = _settings.AdvancedMode;
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
        LoadVoiceSettings(_settings);
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

        LoadSetupSettings(_settings);
        LoadOverlaySettings(_settings);
        LoadAutoProfileSettings(_settings);
        LoadStatisticsSettings(_settings);
        LoadListeningSettings(_settings);
        RaiseSettingsPropertiesChanged();
    }

    private void RaiseSettingsPropertiesChanged()
    {
        string[] names =
        [
            nameof(StartWithWindows), nameof(StartMinimized), nameof(MinimizeToTray),
            nameof(AutoRecoverDevices), nameof(StartMuted), nameof(FollowDefaultInput),
            nameof(Theme), nameof(UseSystemAccentColor), nameof(CompactMode), nameof(AdvancedMode),
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

        foreach (var name in names.Concat(VoicePropertyNames))
        {
            OnPropertyChanged(name);
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

    private AppSettings BuildSettings(bool? wasRunning = null)
    {
        var settings = new AppSettings
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
            AdvancedMode = AdvancedMode,
            SpeakingTrayEnabled = SpeakingTrayEnabled,
            MuteReminderEnabled = MuteReminderEnabled,
            PushToTalkSoundsEnabled = PushToTalkSoundsEnabled,
            HealthWarningsEnabled = HealthWarningsEnabled,
            UpdateChannel = UpdateChannel,
            SkippedUpdateVersion = _skippedUpdateVersion,
            ActiveProfileId = ActiveProfileId,
            Hotkeys = GetHotkeyBindings(),
            Profiles = Profiles.ToArray()
        };
        settings = WithVoiceSettings(settings);
        settings = WithSetupSettings(settings);
        settings = WithOverlaySettings(settings);
        settings = WithAutoProfileSettings(settings);
        settings = WithStatisticsSettings(settings);
        return WithListeningSettings(settings);
    }

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
        DisposeTools();
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
            StopAutoProfiles();
            FoldLifetime(save: false);
            await DisposeListeningAsync();
        }
        catch (Exception exception)
        {
            _log.Write(exception, "Stop listening");
        }

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

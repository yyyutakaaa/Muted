using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Muted.App.Infrastructure;
using Muted.App.Services;
using Muted.Audio.Windows.Devices;
using Muted.Audio.Windows.Engine;
using Muted.Core.Audio;
using Muted.Core.Settings;

namespace Muted.App.ViewModels;

internal sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private const string NoDevicesError = "Windows did not report any usable audio devices.";
    private const string DeviceReadError = "Audio devices could not be read.";

    private readonly RealtimeAudioEngine _engine;
    private readonly WasapiDeviceCatalog _deviceCatalog;
    private readonly JsonSettingsStore _settingsStore;
    private readonly StartupService _startupService;
    private readonly FileLog _log;
    private readonly SynchronizationContext _uiContext;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private AppSettings _settings = new();
    private AudioDeviceInfo? _selectedInput;
    private AudioDeviceInfo? _selectedOutput;
    private bool _startWithWindows;
    private bool _minimizeToTray = true;
    private bool _startMinimized;
    private AudioEngineState _engineState;
    private string _statusText = "Stopped";
    private string? _errorMessage;
    private bool _initialized;
    private bool _isRefreshingDevices;
    private int _refreshPending;
    private int _deferredDeviceRefresh;
    private int _saveRevision;
    private int _disposed;
    private readonly DispatcherTimer _meterTimer;
    private double _inputLevel;
    private double _outputLevel;
    private double _voiceProbability;
    private string _inputLevelDb = "–∞ dB";
    private bool _suppressionEnabled = true;
    private double _wetMix = 1;
    private bool _voiceGateEnabled;
    private double _voiceSensitivity = 0.55;
    private int _voiceHoldMilliseconds = 250;
    private bool _isMuted;
    private AudioProfile? _selectedProfile;
    private string? _activeProfileId;
    private string _newProfileName = string.Empty;
    private bool _isApplyingProfile;
    private bool _isDiagnosticsRunning;
    private string _diagnosticStatus = "Run the checks to verify your setup.";
    private bool _uiVisible = true;

    public MainViewModel(
        RealtimeAudioEngine engine,
        WasapiDeviceCatalog deviceCatalog,
        JsonSettingsStore settingsStore,
        StartupService startupService,
        FileLog log)
    {
        _engine = engine;
        _deviceCatalog = deviceCatalog;
        _settingsStore = settingsStore;
        _startupService = startupService;
        _log = log;
        _uiContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("MainViewModel moet op de UI-thread worden gemaakt.");

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
        DeleteProfileCommand = new RelayCommand(
            DeleteSelectedProfile,
            () => SelectedProfile is not null &&
                Profiles.Count > 1 &&
                !string.Equals(SelectedProfile.Id, ActiveProfileId, StringComparison.OrdinalIgnoreCase) &&
                !IsBusy);
        RunDiagnosticsCommand = new AsyncRelayCommand(
            RunDiagnosticsAsync,
            () => !IsBusy && !IsDiagnosticsRunning);

        _engine.StateChanged += OnEngineStateChanged;
        _engine.Faulted += OnEngineFaulted;
        _deviceCatalog.DevicesChanged += OnDevicesChanged;

        _meterTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _meterTimer.Tick += OnMeterTick;
    }

    public ObservableCollection<AudioDeviceInfo> InputDevices { get; } = [];

    public ObservableCollection<AudioDeviceInfo> OutputDevices { get; } = [];

    public ObservableCollection<AudioProfile> Profiles { get; } = [];

    public ObservableCollection<DiagnosticCheck> DiagnosticChecks { get; } = [];

    public AsyncRelayCommand ToggleCommand { get; }

    public RelayCommand ToggleMuteCommand { get; }

    public RelayCommand ToggleSuppressionCommand { get; }

    public AsyncRelayCommand ApplyProfileCommand { get; }

    public RelayCommand SaveProfileCommand { get; }

    public RelayCommand DeleteProfileCommand { get; }

    public AsyncRelayCommand RunDiagnosticsCommand { get; }

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
                ToggleCommand.RaiseCanExecuteChanged();
                ToggleMuteCommand.RaiseCanExecuteChanged();
                ToggleSuppressionCommand.RaiseCanExecuteChanged();
                ApplyProfileCommand.RaiseCanExecuteChanged();
                DeleteProfileCommand.RaiseCanExecuteChanged();
                RunDiagnosticsCommand.RaiseCanExecuteChanged();
                UpdateMeterTimer();
            }
        }
    }

    public bool IsRunning => EngineState == AudioEngineState.Running;

    public bool IsBusy => EngineState is AudioEngineState.Starting or AudioEngineState.Stopping;

    public bool IsStopped => !IsRunning && !IsBusy;

    public bool CanSelectInput => IsStopped;

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
        private set => SetProperty(ref _voiceProbability, value);
    }

    public string InputLevelDb
    {
        get => _inputLevelDb;
        private set => SetProperty(ref _inputLevelDb, value);
    }

    public bool SuppressionEnabled
    {
        get => _suppressionEnabled;
        set
        {
            if (SetProperty(ref _suppressionEnabled, value) && _initialized)
            {
                _engine.UpdateSuppression(BuildSuppressionOptions());
                if (!_isApplyingProfile)
                {
                    QueueSave();
                }
            }
        }
    }

    public double WetMix
    {
        get => _wetMix;
        set
        {
            if (SetProperty(ref _wetMix, Math.Clamp(value, 0, 1)) && _initialized)
            {
                _engine.UpdateSuppression(BuildSuppressionOptions());
                if (!_isApplyingProfile)
                {
                    QueueSave();
                }
            }
        }
    }

    public bool VoiceGateEnabled
    {
        get => _voiceGateEnabled;
        set
        {
            if (SetProperty(ref _voiceGateEnabled, value) && _initialized)
            {
                _engine.UpdateSuppression(BuildSuppressionOptions());
                if (!_isApplyingProfile)
                {
                    QueueSave();
                }
            }
        }
    }

    public double VoiceSensitivity
    {
        get => _voiceSensitivity;
        set
        {
            if (SetProperty(ref _voiceSensitivity, value) && _initialized)
            {
                _engine.UpdateSuppression(BuildSuppressionOptions());
                if (!_isApplyingProfile)
                {
                    QueueSave();
                }
            }
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetProperty(ref _isMuted, value))
            {
                _engine.UpdateSuppression(BuildSuppressionOptions());
                OnPropertyChanged(nameof(MuteStatusText));
            }
        }
    }

    public string MuteStatusText => IsMuted ? "Microphone muted" : "Microphone live";

    public AudioProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                ApplyProfileCommand.RaiseCanExecuteChanged();
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

    public async Task InitializeAsync(AppSettings settings)
    {
        _settings = settings.Normalize();
        _startWithWindows = _settings.StartWithWindows;
        _startMinimized = _settings.StartMinimized;
        _minimizeToTray = _settings.MinimizeToTray;
        _suppressionEnabled = _settings.SuppressionEnabled;
        _wetMix = _settings.WetMix;
        _voiceGateEnabled = _settings.VoiceGateEnabled;
        _voiceSensitivity = _settings.VoiceThreshold;
        _voiceHoldMilliseconds = _settings.VoiceHoldMilliseconds;

        Profiles.Clear();
        foreach (var profile in _settings.Profiles)
        {
            Profiles.Add(profile);
        }

        _selectedProfile = Profiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, _settings.ActiveProfileId, StringComparison.OrdinalIgnoreCase))
            ?? Profiles.FirstOrDefault();
        _activeProfileId = _selectedProfile?.Id;

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

        RefreshDevicesCore(_settings.InputDeviceId, _settings.OutputDeviceId);
        _initialized = true;

        if (_settings.WasRunningOnExit)
        {
            var savedInputAvailable = !string.IsNullOrWhiteSpace(_settings.InputDeviceId) &&
                InputDevices.Any(device => device.Id == _settings.InputDeviceId);
            var savedOutputAvailable = !string.IsNullOrWhiteSpace(_settings.OutputDeviceId) &&
                OutputDevices.Any(device => device.Id == _settings.OutputDeviceId);
            if (savedInputAvailable && savedOutputAvailable && HasDevices && IsRoutingReady)
            {
                await StartAsync();
            }
            else
            {
                ErrorMessage = "Previous audio devices are unavailable, so Muted stayed stopped.";
            }
        }
    }

    private async Task ToggleAsync()
    {
        if (IsRunning)
        {
            await StopAsync();
        }
        else
        {
            await StartAsync();
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
            _voiceHoldMilliseconds = profile.VoiceHoldMilliseconds;
            _engine.UpdateSuppression(BuildSuppressionOptions());
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

        var profile = new AudioProfile
        {
            Name = name,
            InputDeviceId = SelectedInput?.Id,
            OutputDeviceId = SelectedOutput?.Id,
            SuppressionEnabled = SuppressionEnabled,
            WetMix = (float)WetMix,
            VoiceGateEnabled = VoiceGateEnabled,
            VoiceThreshold = (float)VoiceSensitivity,
            VoiceHoldMilliseconds = _voiceHoldMilliseconds
        }.Normalize();

        Profiles.Add(profile);
        SelectedProfile = profile;
        ActiveProfileId = profile.Id;
        NewProfileName = string.Empty;
        OnPropertyChanged(nameof(Profiles));
        DeleteProfileCommand.RaiseCanExecuteChanged();
        SaveProfileCommand.RaiseCanExecuteChanged();
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
        DiagnosticStatus = "Checking devices and audio signal…";
        var startedForTest = false;

        try
        {
            if (IsStopped)
            {
                RefreshDevicesCore(SelectedInput?.Id, SelectedOutput?.Id);
            }

            AddDeviceChecks();
            AddRuntimeChecks();

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
                    var peak = 0f;
                    var processingLoad = 0f;
                    for (var index = 0; index < 20; index++)
                    {
                        await Task.Delay(100);
                        var metrics = _engine.Metrics;
                        peak = Math.Max(peak, metrics.InputPeak);
                        processingLoad = Math.Max(processingLoad, metrics.ProcessingLoad);
                    }

                    DiagnosticChecks.Add(peak > 0.005f
                        ? new DiagnosticCheck(
                            "Microphone signal",
                            $"Signal received ({20 * Math.Log10(peak):0.0} dB peak).",
                            DiagnosticSeverity.Passed)
                        : new DiagnosticCheck(
                            "Microphone signal",
                            "No clear signal was detected. Speak into the selected microphone and run again.",
                            DiagnosticSeverity.Warning));

                    DiagnosticChecks.Add(processingLoad < 0.85f
                        ? new DiagnosticCheck(
                            "Processing headroom",
                            $"RNNoise peak processing load was {processingLoad * 100:0}%.",
                            DiagnosticSeverity.Passed)
                        : new DiagnosticCheck(
                            "Processing headroom",
                            $"RNNoise reached {processingLoad * 100:0}% processing load; audio may stutter.",
                            DiagnosticSeverity.Warning));
                }
            }

            var failures = DiagnosticChecks.Count(check => check.Severity == DiagnosticSeverity.Failed);
            var warnings = DiagnosticChecks.Count(check => check.Severity == DiagnosticSeverity.Warning);
            DiagnosticStatus = failures > 0
                ? $"{failures} problem(s) need attention."
                : warnings > 0
                    ? $"Setup works, with {warnings} warning(s)."
                    : "Everything looks ready.";
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
        }
    }

    private void AddDeviceChecks()
    {
        DiagnosticChecks.Add(SelectedInput is null
            ? new DiagnosticCheck("Microphone", "No input device is selected.", DiagnosticSeverity.Failed)
            : new DiagnosticCheck("Microphone", SelectedInput.Name, DiagnosticSeverity.Passed));

        DiagnosticChecks.Add(SelectedOutput is null
            ? new DiagnosticCheck("Virtual cable", "No output device is selected.", DiagnosticSeverity.Failed)
            : IsRoutingReady
                ? new DiagnosticCheck("Virtual cable", SelectedOutput.Name, DiagnosticSeverity.Passed)
                : new DiagnosticCheck(
                    "Virtual cable",
                    $"{SelectedOutput.Name} does not look like a virtual cable output.",
                    DiagnosticSeverity.Failed));

        AddFormatCheck("Microphone format", SelectedInput);
        AddFormatCheck("Cable format", SelectedOutput);
    }

    private void AddFormatCheck(string title, AudioDeviceInfo? device)
    {
        if (device is null)
        {
            return;
        }

        try
        {
            var format = _deviceCatalog.GetMixFormat(device.Id);
            DiagnosticChecks.Add(format.SampleRate == 48_000
                ? new DiagnosticCheck(title, format.DisplayName, DiagnosticSeverity.Passed)
                : new DiagnosticCheck(
                    title,
                    $"Windows uses {format.DisplayName}. Set the device's default format to 48 kHz.",
                    DiagnosticSeverity.Warning));
        }
        catch (Exception exception)
        {
            DiagnosticChecks.Add(new DiagnosticCheck(title, exception.Message, DiagnosticSeverity.Failed));
        }
    }

    private void AddRuntimeChecks()
    {
        var rnnoisePath = Path.Combine(AppContext.BaseDirectory, "rnnoise.dll");
        DiagnosticChecks.Add(File.Exists(rnnoisePath)
            ? new DiagnosticCheck("RNNoise runtime", "rnnoise.dll is present.", DiagnosticSeverity.Passed)
            : new DiagnosticCheck(
                "RNNoise runtime",
                "rnnoise.dll is missing. Repair or reinstall Muted.",
                DiagnosticSeverity.Failed));
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
        try
        {
            var options = new AudioEngineOptions(
                SelectedInput?.Id,
                SelectedOutput?.Id,
                _settings.TargetLatencyMilliseconds,
                BuildSuppressionOptions());
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

        IsMuted = false;
        await TrySaveAsync();
        return true;
    }

    private void RefreshDevicesCore(string? preferredInputId, string? preferredOutputId)
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

            SelectedInput = inputs.FirstOrDefault(device => device.Id == preferredInputId)
                ?? inputs.FirstOrDefault(device => device.IsDefault)
                ?? inputs.FirstOrDefault();

            SelectedOutput = outputs.FirstOrDefault(device => device.Id == preferredOutputId)
                ?? outputs.FirstOrDefault(device => WasapiDeviceCatalog.IsLikelyVirtualCable(device.Name))
                ?? outputs.FirstOrDefault(device => device.IsDefault)
                ?? outputs.FirstOrDefault();

            if (!HasDevices)
            {
                ErrorMessage = NoDevicesError;
            }
            else if (ErrorMessage is NoDevicesError or DeviceReadError)
            {
                ErrorMessage = null;
            }
            OnPropertyChanged(nameof(HasDevices));
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
                }
                else
                {
                    Interlocked.Exchange(ref _deferredDeviceRefresh, 1);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _refreshPending, 0);
            }
        }, null);
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
                AudioEngineState.Faulted => "Audio error",
                _ => "Stopped"
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
        }, null);

    private SuppressionOptions BuildSuppressionOptions() => new(
        Enabled: SuppressionEnabled,
        WetMix: (float)WetMix,
        VoiceGateEnabled: VoiceGateEnabled,
        VoiceThreshold: (float)VoiceSensitivity,
        VoiceHoldMilliseconds: _voiceHoldMilliseconds,
        IsMuted: IsMuted);

    private AppSettings BuildSettings(bool? wasRunning = null) => new()
    {
        InputDeviceId = SelectedInput?.Id,
        OutputDeviceId = SelectedOutput?.Id,
        FollowDefaultInput = false,
        SuppressionEnabled = SuppressionEnabled,
        WetMix = (float)WetMix,
        VoiceGateEnabled = VoiceGateEnabled,
        VoiceThreshold = (float)VoiceSensitivity,
        VoiceHoldMilliseconds = _voiceHoldMilliseconds,
        TargetLatencyMilliseconds = _settings.TargetLatencyMilliseconds,
        StartWithWindows = StartWithWindows,
        StartMinimized = StartMinimized,
        MinimizeToTray = MinimizeToTray,
        WasRunningOnExit = wasRunning ?? IsRunning,
        ActiveProfileId = ActiveProfileId,
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
            InputLevel = 0;
            OutputLevel = 0;
            VoiceProbability = 0;
            InputLevelDb = "–∞ dB";
        }
    }

    private void OnMeterTick(object? sender, EventArgs eventArgs)
    {
        var metrics = _engine.Metrics;
        InputLevel = Smooth(InputLevel, metrics.InputPeak);
        OutputLevel = Smooth(OutputLevel, metrics.OutputPeak);
        VoiceProbability = Smooth(VoiceProbability, metrics.VoiceProbability, decay: 0.10);
        InputLevelDb = InputLevel <= 0.001
            ? "–∞ dB"
            : $"{20 * Math.Log10(InputLevel):0.0} dB";
    }

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
        _deviceCatalog.DevicesChanged -= OnDevicesChanged;
        _engine.StateChanged -= OnEngineStateChanged;
        _engine.Faulted -= OnEngineFaulted;
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

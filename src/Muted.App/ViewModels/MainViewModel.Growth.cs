using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Threading;
using Muted.App.Infrastructure;
using Muted.App.Services;
using Muted.Audio.Windows.Devices;
using Muted.Audio.Windows.Engine;
using Muted.Core.Audio;
using Muted.Core.Settings;
using Muted.Core.Support;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Muted.App.ViewModels;

/// <summary>
/// The first-start guide, bug reports, the before/after clip, the in-game overlay,
/// automatic profiles, lifetime totals, commands from other programs and cleaning
/// what you hear.
/// </summary>
internal sealed partial class MainViewModel
{
    private const int LifetimeSaveIntervalTicks = 150; // 30 seconds of 200 ms feature ticks

    private readonly ListeningEngine _listening = new();
    private DispatcherTimer _autoProfileTimer = null!;
    private bool _setupCompleted;
    private bool _overlayEnabled;
    private OverlayPosition _overlayPosition = OverlayPosition.TopCenter;
    private bool _autoProfileEnabled;
    private string? _autoProfileActiveRule;
    private string? _profileBeforeAutoSwitch;
    private bool _autoProfileScanning;
    private string _autoProfileStatus = "Add a program and the profile it should switch to.";
    private LifetimeStatistics _lifetime = new();
    private SessionStatistics _lifetimeCounted;
    private bool _lifetimeNewSession;
    private int _lifetimeTicks;
    private float[]? _lastRawClip;
    private bool _listeningEnabled;
    private AudioDeviceInfo? _listeningInput;
    private AudioDeviceInfo? _listeningOutput;
    private double _listeningStrength = 1;
    private string _listeningStatus = "Off.";
    private bool _listeningBusy;
    private string _newRuleProcessName = string.Empty;
    private AudioProfile? _newRuleProfile;

    public ObservableCollection<AutoProfileRuleViewModel> AutoProfileRules { get; } = [];

    public ObservableCollection<string> RunningPrograms { get; } = [];

    public IReadOnlyList<OverlayPosition> OverlayPositions { get; } = Enum.GetValues<OverlayPosition>();

    public RelayCommand RescanDevicesCommand { get; private set; } = null!;
    public RelayCommand FinishSetupCommand { get; private set; } = null!;
    public RelayCommand ShowSetupGuideCommand { get; private set; } = null!;
    public RelayCommand ReportProblemCommand { get; private set; } = null!;
    public RelayCommand ShowDiagnosticsCommand { get; private set; } = null!;
    public RelayCommand SaveComparisonCommand { get; private set; } = null!;
    public RelayCommand AddAutoProfileRuleCommand { get; private set; } = null!;
    public RelayCommand<AutoProfileRuleViewModel> RemoveAutoProfileRuleCommand { get; private set; } = null!;
    public RelayCommand RefreshRunningProgramsCommand { get; private set; } = null!;
    public RelayCommand ResetLifetimeCommand { get; private set; } = null!;

    // ---------------------------------------------------------------- setup guide

    public bool SetupCompleted
    {
        get => _setupCompleted;
        private set
        {
            if (SetProperty(ref _setupCompleted, value))
            {
                OnPropertyChanged(nameof(ShowSetupGuide));
                QueueSave();
            }
        }
    }

    /// <summary>The guide shows on first start, and whenever there is no cable at all.</summary>
    public bool ShowSetupGuide => !SetupCompleted || NeedsVirtualCableSetup;

    public bool SetupMicrophoneReady => SelectedInput is not null;

    /// <summary>
    /// The name to pick in Discord or Teams. VB-CABLE and most cables name the two
    /// ends "Input" and "Output"; the recording side is the "Output" one.
    /// </summary>
    public string CableRecordingName
    {
        get
        {
            var name = SelectedOutput?.Name;
            if (string.IsNullOrWhiteSpace(name) || !IsRoutingReady)
            {
                return "the cable's recording side (usually CABLE Output)";
            }

            var index = name.IndexOf("Input", StringComparison.OrdinalIgnoreCase);
            return index < 0 ? name : name[..index] + "Output" + name[(index + "Input".Length)..];
        }
    }

    public string SetupCallAppText =>
        $"In Discord, Teams or your game, choose {CableRecordingName} as the microphone, and turn the app's own noise suppression off.";

    public string SetupCableText => HasVirtualCable
        ? "Found a virtual cable. Muted sends your cleaned voice into it."
        : "Install VB-CABLE (free), restart Windows if it asks, then press Scan again.";

    public string SetupRoutingText => IsRoutingReady
        ? $"Microphone: {SelectedInput?.Name ?? "none"}. Sending to: {SelectedOutput?.Name}."
        : "Pick your microphone, and the cable's playback side (CABLE Input) as output, under Advanced.";

    private void RaiseSetupChanged()
    {
        foreach (var name in new[]
                 {
                     nameof(ShowSetupGuide), nameof(SetupMicrophoneReady), nameof(CableRecordingName), nameof(SetupCallAppText),
                     nameof(SetupCableText), nameof(SetupRoutingText)
                 })
        {
            OnPropertyChanged(name);
        }
    }

    private void RescanDevices()
    {
        if (IsStopped)
        {
            RefreshDevicesCore(SelectedInput?.Id, SelectedOutput?.Id);
        }

        RaiseSetupChanged();
    }

    // ---------------------------------------------------------------- bug report

    private void ReportProblem()
    {
        try
        {
            var report = DiagnosticsRunner.BuildReport(DiagnosticChecks, SelectedInput, SelectedOutput, _engine.Metrics);
            var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3);
            if (!Shell.Open(IssueLink.Build(report, version)))
            {
                DiagnosticStatus = "No browser opened. Copy the report and open github.com/yyyutakaaa/Muted/issues.";
                return;
            }

            DiagnosticStatus = DiagnosticChecks.Count == 0
                ? "Opened a bug report in your browser. Run the checks first for a more useful report."
                : "Opened a bug report in your browser with the setup check filled in. Nothing is sent until you submit it.";
        }
        catch (Exception exception)
        {
            _log.Write(exception, "Open bug report");
            DiagnosticStatus = "The bug report could not be opened.";
        }
    }

    // ---------------------------------------------------------------- before/after

    private void SaveComparison()
    {
        if (_lastClip is not { Length: > 0 } after || _lastRawClip is not { Length: > 0 } before)
        {
            return;
        }

        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "WAV audio (*.wav)|*.wav",
                FileName = "Muted-before-after.wav"
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            File.WriteAllBytes(dialog.FileName, LocalAudioPlayer.WaveBytes(ComparisonClip.Build(before, after)));
            FeatureStatus = "Saved: first without Muted, then a short pause, then with Muted. Share it anywhere you like.";
        }
        catch (Exception exception)
        {
            FeatureStatus = "Save failed: " + exception.Message;
        }
    }

    // ---------------------------------------------------------------- overlay

    public bool OverlayEnabled
    {
        get => _overlayEnabled;
        set
        {
            if (SetProperty(ref _overlayEnabled, value))
            {
                QueueSave();
            }
        }
    }

    public OverlayPosition OverlayPosition
    {
        get => _overlayPosition;
        set
        {
            if (SetProperty(ref _overlayPosition, value))
            {
                QueueSave();
            }
        }
    }

    private bool _overlaySpeaking;

    /// <summary>Speaking right now, kept up to date even while every window is hidden.</summary>
    public bool OverlaySpeaking
    {
        get => _overlaySpeaking;
        private set => SetProperty(ref _overlaySpeaking, value);
    }

    // ---------------------------------------------------------------- automatic profiles

    public bool AutoProfileEnabled
    {
        get => _autoProfileEnabled;
        set
        {
            if (!SetProperty(ref _autoProfileEnabled, value))
            {
                return;
            }

            if (value)
            {
                _autoProfileTimer.Start();
                AutoProfileStatus = "Watching for the programs below.";
            }
            else
            {
                _autoProfileTimer.Stop();
                _autoProfileActiveRule = null;
                _profileBeforeAutoSwitch = null;
                AutoProfileStatus = "Off.";
            }

            QueueSave();
        }
    }

    public string AutoProfileStatus
    {
        get => _autoProfileStatus;
        private set => SetProperty(ref _autoProfileStatus, value);
    }

    public string NewRuleProcessName
    {
        get => _newRuleProcessName;
        set
        {
            if (SetProperty(ref _newRuleProcessName, value))
            {
                AddAutoProfileRuleCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AudioProfile? NewRuleProfile
    {
        get => _newRuleProfile;
        set
        {
            if (SetProperty(ref _newRuleProfile, value))
            {
                AddAutoProfileRuleCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private void AddAutoProfileRule()
    {
        var name = AutoProfileRule.NormalizeProcessName(NewRuleProcessName);
        if (name.Length == 0 || NewRuleProfile is null)
        {
            return;
        }

        var existing = AutoProfileRules.FirstOrDefault(rule => rule.ProcessName == name);
        if (existing is not null)
        {
            AutoProfileRules.Remove(existing);
        }

        AutoProfileRules.Add(new AutoProfileRuleViewModel(name, NewRuleProfile.Id, NewRuleProfile.Name));
        NewRuleProcessName = string.Empty;
        AddAutoProfileRuleCommand.RaiseCanExecuteChanged();
        AutoProfileStatus = $"Muted switches to {NewRuleProfile.Name} while {name} runs.";
        QueueSave();
        if (AutoProfileEnabled)
        {
            _ = ScanProgramsAsync();
        }
    }

    private void RemoveAutoProfileRule(AutoProfileRuleViewModel? rule)
    {
        if (rule is null)
        {
            return;
        }

        AutoProfileRules.Remove(rule);
        if (_autoProfileActiveRule == rule.ProcessName)
        {
            _autoProfileActiveRule = null;
        }

        AddAutoProfileRuleCommand.RaiseCanExecuteChanged();
        QueueSave();
    }

    /// <summary>Programs with a window, which is what people recognise as "running".</summary>
    private async void RefreshRunningPrograms()
    {
        try
        {
            var names = await Task.Run(() =>
            {
                var result = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var process in Process.GetProcesses())
                {
                    using (process)
                    {
                        try
                        {
                            if (process.MainWindowHandle != IntPtr.Zero)
                            {
                                result.Add(AutoProfileRule.NormalizeProcessName(process.ProcessName));
                            }
                        }
                        catch (Exception)
                        {
                            // Some system processes refuse to be inspected.
                        }
                    }
                }

                return result.Where(name => name.Length > 0 && name != "muted").ToArray();
            });
            RunningPrograms.Clear();
            foreach (var name in names)
            {
                RunningPrograms.Add(name);
            }
        }
        catch (Exception exception)
        {
            _log.Write(exception, "List running programs");
        }
    }

    private async void OnAutoProfileTick(object? sender, EventArgs eventArgs) => await ScanProgramsAsync();

    private async Task ScanProgramsAsync()
    {
        if (_autoProfileScanning || !AutoProfileEnabled || AutoProfileRules.Count == 0)
        {
            return;
        }

        _autoProfileScanning = true;
        try
        {
            var running = await Task.Run(() =>
            {
                var processes = Process.GetProcesses();
                try
                {
                    return processes.Select(process => process.ProcessName).ToArray();
                }
                finally
                {
                    foreach (var process in processes)
                    {
                        process.Dispose();
                    }
                }
            });

            var rules = AutoProfileRules.Select(rule => rule.ToRule()).ToArray();
            var runningNames = running.Select(AutoProfileRule.NormalizeProcessName).ToHashSet(StringComparer.Ordinal);
            var match = rules.FirstOrDefault(rule => runningNames.Contains(rule.ProcessName));

            // Busy moments are skipped, not lost: the next scan tries again.
            if (IsBusy || ClipBusy || IsRecording || IsCalibrating || _isApplyingProfile)
            {
                return;
            }

            if (match is not null && match.ProcessName != _autoProfileActiveRule)
            {
                if (_autoProfileActiveRule is null)
                {
                    _profileBeforeAutoSwitch = ActiveProfileId;
                }

                _autoProfileActiveRule = match.ProcessName;
                if (!string.Equals(ActiveProfileId, match.ProfileId, StringComparison.OrdinalIgnoreCase))
                {
                    await ApplyProfileAsync(match.ProfileId);
                }

                AutoProfileStatus = $"{match.ProcessName} is running, so {ActiveProfileName} is on.";
            }
            else if (match is null && _autoProfileActiveRule is not null)
            {
                var closed = _autoProfileActiveRule;
                _autoProfileActiveRule = null;
                var previous = _profileBeforeAutoSwitch;
                _profileBeforeAutoSwitch = null;
                if (previous is not null && Profiles.Any(profile => profile.Id == previous) &&
                    !string.Equals(ActiveProfileId, previous, StringComparison.OrdinalIgnoreCase))
                {
                    await ApplyProfileAsync(previous);
                }

                AutoProfileStatus = $"{closed} closed, so {ActiveProfileName} is back.";
            }
        }
        catch (Exception exception)
        {
            _log.Write(exception, "Automatic profile");
        }
        finally
        {
            _autoProfileScanning = false;
        }
    }

    // ---------------------------------------------------------------- lifetime totals

    public string LifetimeText
    {
        get
        {
            var total = _lifetime;
            if (total.Seconds < 1)
            {
                return "No audio processed yet.";
            }

            var since = total.Since is { } date ? $" since {date.LocalDateTime:d MMM yyyy}" : string.Empty;
            return $"In total{since}: {FormatDuration(total.Seconds)} cleaned over {total.Sessions} session(s), " +
                $"{FormatDuration(total.SpeakingSeconds)} of it speaking, {total.AverageNoiseReductionDb:0.0} dB of noise removed on average.";
        }
    }

    private void FoldLifetime(bool save)
    {
        var session = _engine.Session;
        if (session.Seconds > _lifetimeCounted.Seconds)
        {
            _lifetime = _lifetime.Add(session, _lifetimeCounted, _lifetimeNewSession, DateTimeOffset.Now);
            _lifetimeNewSession = false;
            OnPropertyChanged(nameof(LifetimeText));
            if (save)
            {
                QueueSave();
            }
        }

        _lifetimeCounted = session;
    }

    private void GrowthTick()
    {
        if (++_lifetimeTicks >= LifetimeSaveIntervalTicks)
        {
            _lifetimeTicks = 0;
            FoldLifetime(save: true);
        }

        if (_listening.IsRunning)
        {
            ListeningReductionText = $"{_listening.NoiseReductionDb:0.0} dB removed";
        }
    }

    private void GrowthEngineChanged()
    {
        if (IsRunning)
        {
            _lifetimeCounted = default;
            _lifetimeNewSession = true;
            _lifetimeTicks = 0;
        }
        else
        {
            FoldLifetime(save: true);
        }

        SaveComparisonCommand?.RaiseCanExecuteChanged();
    }

    private void ResetLifetime()
    {
        _lifetime = new LifetimeStatistics();
        _lifetimeCounted = _engine.Session;
        OnPropertyChanged(nameof(LifetimeText));
        QueueSave();
    }

    private static string FormatDuration(double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours} h {time.Minutes} min" : $"{time.Minutes} min";
    }

    // ---------------------------------------------------------------- listening

    public bool ListeningEnabled
    {
        get => _listeningEnabled;
        set
        {
            if (_listeningEnabled == value)
            {
                return;
            }

            SetProperty(ref _listeningEnabled, value);
            _ = ApplyListeningAsync();
            QueueSave();
        }
    }

    public AudioDeviceInfo? ListeningInput
    {
        get => _listeningInput;
        set
        {
            if (SetProperty(ref _listeningInput, value) && !_isRefreshingDevices)
            {
                if (ListeningEnabled)
                {
                    _ = ApplyListeningAsync();
                }

                QueueSave();
            }
        }
    }

    public AudioDeviceInfo? ListeningOutput
    {
        get => _listeningOutput;
        set
        {
            if (SetProperty(ref _listeningOutput, value) && !_isRefreshingDevices)
            {
                if (ListeningEnabled)
                {
                    _ = ApplyListeningAsync();
                }

                QueueSave();
            }
        }
    }

    public double ListeningStrength
    {
        get => _listeningStrength;
        set
        {
            if (SetProperty(ref _listeningStrength, Math.Clamp(value, 0, 1)))
            {
                _listening.UpdateStrength((float)_listeningStrength);
                OnPropertyChanged(nameof(ListeningStrengthText));
                QueueSave();
            }
        }
    }

    public string ListeningStrengthText => $"{ListeningStrength * 100:0}% cleaned";

    public string ListeningStatus
    {
        get => _listeningStatus;
        private set => SetProperty(ref _listeningStatus, value);
    }

    private string _listeningReductionText = string.Empty;

    public string ListeningReductionText
    {
        get => _listeningReductionText;
        private set => SetProperty(ref _listeningReductionText, value);
    }

    private bool _listeningPending;

    /// <summary>Brings the listening pipeline in line with the switches. Changes made while
    /// it is starting are not lost: the loop runs again with the latest choice.</summary>
    private async Task ApplyListeningAsync()
    {
        if (!_initialized)
        {
            return;
        }

        _listeningPending = true;
        if (_listeningBusy)
        {
            return;
        }

        _listeningBusy = true;
        try
        {
            while (_listeningPending && Volatile.Read(ref _disposed) == 0)
            {
                _listeningPending = false;
                await ApplyListeningOnceAsync();
            }
        }
        finally
        {
            _listeningBusy = false;
        }
    }

    private async Task ApplyListeningOnceAsync()
    {
        try
        {
            if (!ListeningEnabled)
            {
                await _listening.StopAsync();
                ListeningStatus = "Off.";
                ListeningReductionText = string.Empty;
                return;
            }

            var input = ListeningInput;
            var output = ListeningOutput;
            if (input is null || output is null)
            {
                ListeningStatus = "Pick where your call app plays into and where you listen.";
                return;
            }

            // Never play other people into your own microphone.
            if (output.Id == SelectedOutput?.Id || WasapiDeviceCatalog.IsLikelyVirtualCable(output.Name))
            {
                ListeningStatus = "Play the cleaned sound on your headphones or speakers, not on a virtual cable.";
                await _listening.StopAsync();
                return;
            }

            await _listening.StartAsync(new ListeningOptions(
                input.Id,
                output.Id,
                (float)ListeningStrength,
                TargetLatencyMilliseconds));
            ListeningStatus = $"Cleaning {input.Name} into {output.Name}.";
        }
        catch (Exception exception)
        {
            _log.Write(exception, "Start listening");
            ListeningStatus = "Could not start: " + FriendlyAudioError(exception);
        }
    }

    private void OnListeningFaulted(object? sender, Exception exception) =>
        _uiContext.Post(_ =>
        {
            _log.Write(exception, "Listening pipeline");
            ListeningStatus = "Stopped: " + FriendlyAudioError(exception);
            ListeningReductionText = string.Empty;
        }, null);

    private void SelectListeningDevices(string? inputId, string? outputId)
    {
        var wasRefreshing = _isRefreshingDevices;
        _isRefreshingDevices = true;
        try
        {
            ListeningInput = InputDevices.FirstOrDefault(device => device.Id == (inputId ?? ListeningInput?.Id))
                ?? InputDevices.FirstOrDefault(device =>
                    device.Name.Contains("cable", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(device.Name, CableRecordingName, StringComparison.OrdinalIgnoreCase));
            ListeningOutput = OutputDevices.FirstOrDefault(device => device.Id == (outputId ?? ListeningOutput?.Id))
                ?? SelectedMonitor;
        }
        finally
        {
            _isRefreshingDevices = wasRefreshing;
        }
    }

    // ---------------------------------------------------------------- other programs

    /// <summary>Runs a command sent by a second launch, like "Muted.exe --toggle-mute".</summary>
    public async Task HandleRemoteAsync(RemoteRequest request)
    {
        switch (request.Command)
        {
            case RemoteCommand.Show:
                ShowWindowRequested?.Invoke(this, EventArgs.Empty);
                break;
            case RemoteCommand.ToggleMute:
                IsMuted = !IsMuted;
                break;
            case RemoteCommand.Mute:
                IsMuted = true;
                break;
            case RemoteCommand.Unmute:
                IsMuted = false;
                break;
            case RemoteCommand.ToggleSuppression:
                SuppressionEnabled = !SuppressionEnabled;
                break;
            case RemoteCommand.ToggleEngine:
                if (ToggleCommand.CanExecute(null))
                {
                    await ToggleAsync();
                }

                break;
            case RemoteCommand.Start:
                if (!IsRunning && ToggleCommand.CanExecute(null))
                {
                    await ToggleAsync();
                }

                break;
            case RemoteCommand.Stop:
                if (IsRunning && ToggleCommand.CanExecute(null))
                {
                    await ToggleAsync();
                }

                break;
            case RemoteCommand.ToggleOverlay:
                OverlayEnabled = !OverlayEnabled;
                break;
            case RemoteCommand.ToggleListening:
                ListeningEnabled = !ListeningEnabled;
                break;
            case RemoteCommand.Profile:
                var profile = Profiles.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, request.Argument, StringComparison.OrdinalIgnoreCase));
                if (profile is null)
                {
                    NotificationRequested?.Invoke(this, $"No profile called \"{request.Argument}\".");
                }
                else
                {
                    await ApplyProfileAsync(profile.Id);
                }

                break;
        }
    }

    // ---------------------------------------------------------------- plumbing

    private void InitializeGrowth()
    {
        RescanDevicesCommand = new RelayCommand(RescanDevices, () => IsStopped);
        FinishSetupCommand = new RelayCommand(() =>
        {
            SetupCompleted = true;
            RaiseSetupChanged();
        });
        ShowSetupGuideCommand = new RelayCommand(() =>
        {
            SetupCompleted = false;
            SelectedPage = AppPage.Settings;
            RaiseSetupChanged();
        });
        ReportProblemCommand = new RelayCommand(ReportProblem);
        ShowDiagnosticsCommand = new RelayCommand(() =>
        {
            SelectedPage = AppPage.Diagnostics;
            if (RunDiagnosticsCommand.CanExecute(null))
            {
                RunDiagnosticsCommand.Execute(null);
            }
        });
        SaveComparisonCommand = new RelayCommand(
            SaveComparison,
            () => _lastClip is { Length: > 0 } && _lastRawClip is { Length: > 0 } && !IsRecording && !ClipBusy);
        AddAutoProfileRuleCommand = new RelayCommand(
            AddAutoProfileRule,
            () => AutoProfileRule.NormalizeProcessName(NewRuleProcessName).Length > 0 &&
                NewRuleProfile is not null &&
                AutoProfileRules.Count < AutoProfileRule.MaximumRules);
        RemoveAutoProfileRuleCommand = new RelayCommand<AutoProfileRuleViewModel>(RemoveAutoProfileRule);
        RefreshRunningProgramsCommand = new RelayCommand(RefreshRunningPrograms);
        ResetLifetimeCommand = new RelayCommand(ResetLifetime);

        _autoProfileTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _autoProfileTimer.Tick += OnAutoProfileTick;
        _listening.Faulted += OnListeningFaulted;
        PropertyChanged += (_, eventArgs) =>
        {
            switch (eventArgs.PropertyName)
            {
                case nameof(SelectedInput):
                case nameof(SelectedOutput):
                case nameof(NeedsVirtualCableSetup):
                case nameof(HasVirtualCable):
                case nameof(IsRoutingReady):
                    RaiseSetupChanged();
                    break;
                case nameof(EngineState):
                    RescanDevicesCommand.RaiseCanExecuteChanged();
                    break;
            }
        };
        Profiles.CollectionChanged += (_, _) =>
        {
            SyncAutoProfileRules();
            if (NewRuleProfile is null || !Profiles.Contains(NewRuleProfile))
            {
                NewRuleProfile = Profiles.FirstOrDefault();
            }
        };
    }

    private void LoadGrowthSettings(AppSettings settings)
    {
        _setupCompleted = settings.SetupCompleted;
        _overlayEnabled = settings.OverlayEnabled;
        _overlayPosition = settings.OverlayPosition;
        _autoProfileEnabled = settings.AutoProfileEnabled;
        _lifetime = settings.Lifetime;
        _listeningEnabled = settings.ListeningEnabled;
        _listeningStrength = settings.ListeningStrength;

        AutoProfileRules.Clear();
        foreach (var rule in settings.AutoProfileRules)
        {
            var profile = settings.Profiles.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, rule.ProfileId, StringComparison.OrdinalIgnoreCase));
            if (profile is not null)
            {
                AutoProfileRules.Add(new AutoProfileRuleViewModel(rule.ProcessName, profile.Id, profile.Name));
            }
        }

        foreach (var name in new[]
                 {
                     nameof(SetupCompleted), nameof(ShowSetupGuide), nameof(OverlayEnabled), nameof(OverlayPosition),
                     nameof(AutoProfileEnabled), nameof(LifetimeText), nameof(ListeningEnabled),
                     nameof(ListeningStrength), nameof(ListeningStrengthText)
                 })
        {
            OnPropertyChanged(name);
        }
    }

    /// <summary>Runs once devices are known: picks listening devices and starts what was on.</summary>
    private void StartGrowth()
    {
        SelectListeningDevices(_settings.ListeningInputDeviceId, _settings.ListeningOutputDeviceId);
        NewRuleProfile = Profiles.FirstOrDefault();
        RaiseSetupChanged();
        if (AutoProfileEnabled)
        {
            AutoProfileStatus = "Watching for the programs below.";
            _autoProfileTimer.Start();
        }

        if (ListeningEnabled)
        {
            _ = ApplyListeningAsync();
        }
    }

    private AppSettings WithGrowthSettings(AppSettings settings) => settings with
    {
        SetupCompleted = SetupCompleted,
        OverlayEnabled = OverlayEnabled,
        OverlayPosition = OverlayPosition,
        AutoProfileEnabled = AutoProfileEnabled,
        AutoProfileRules = AutoProfileRules.Select(rule => rule.ToRule()).ToArray(),
        Lifetime = _lifetime,
        ListeningEnabled = ListeningEnabled,
        ListeningInputDeviceId = ListeningInput?.Id,
        ListeningOutputDeviceId = ListeningOutput?.Id,
        ListeningStrength = (float)ListeningStrength
    };

    /// <summary>Keeps rules pointing at profiles that still exist, with their current names.</summary>
    private void SyncAutoProfileRules()
    {
        for (var index = AutoProfileRules.Count - 1; index >= 0; index--)
        {
            var rule = AutoProfileRules[index];
            var profile = Profiles.FirstOrDefault(candidate => candidate.Id == rule.ProfileId);
            if (profile is null)
            {
                AutoProfileRules.RemoveAt(index);
            }
            else if (profile.Name != rule.ProfileName)
            {
                AutoProfileRules[index] = new AutoProfileRuleViewModel(rule.ProcessName, profile.Id, profile.Name);
            }
        }
    }

    private async Task DisposeGrowthAsync()
    {
        _autoProfileTimer.Stop();
        _autoProfileTimer.Tick -= OnAutoProfileTick;
        FoldLifetime(save: false);
        _listening.Faulted -= OnListeningFaulted;
        await _listening.DisposeAsync();
    }
}

internal sealed record AutoProfileRuleViewModel(string ProcessName, string ProfileId, string ProfileName)
{
    public string Description => $"{ProcessName}  →  {ProfileName}";

    public AutoProfileRule ToRule() => new() { ProcessName = ProcessName, ProfileId = ProfileId };
}

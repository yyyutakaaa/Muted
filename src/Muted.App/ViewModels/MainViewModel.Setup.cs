using System.Collections.ObjectModel;
using System.Reflection;
using Muted.App.Infrastructure;
using Muted.App.Services;
using Muted.Core.Settings;
using Muted.Core.Support;

namespace Muted.App.ViewModels;

/// <summary>The first-start setup guide, the setup check and reporting a problem.</summary>
internal sealed partial class MainViewModel
{
    private bool _isDiagnosticsRunning;
    private string _diagnosticStatus = "Run the checks to verify your setup.";
    private bool _setupCompleted;

    public ObservableCollection<DiagnosticCheck> DiagnosticChecks { get; } = [];

    public AsyncRelayCommand RunDiagnosticsCommand { get; }

    public RelayCommand CopyDiagnosticsCommand { get; }

    public RelayCommand OpenLogFolderCommand { get; }

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

    public RelayCommand RescanDevicesCommand { get; private set; } = null!;

    public RelayCommand FinishSetupCommand { get; private set; } = null!;

    public RelayCommand ShowSetupGuideCommand { get; private set; } = null!;

    public RelayCommand ReportProblemCommand { get; private set; } = null!;

    public RelayCommand ShowDiagnosticsCommand { get; private set; } = null!;

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

    private void InitializeSetupGuide()
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
    }

    private void LoadSetupSettings(AppSettings settings)
    {
        _setupCompleted = settings.SetupCompleted;
        OnPropertyChanged(nameof(SetupCompleted));
        OnPropertyChanged(nameof(ShowSetupGuide));
    }

    private AppSettings WithSetupSettings(AppSettings settings) => settings with
    {
        SetupCompleted = SetupCompleted
    };
}

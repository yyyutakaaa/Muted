using System.Windows.Threading;
using Muted.App.Infrastructure;
using Muted.Core.Audio;

namespace Muted.App.ViewModels;

/// <summary>Starting and stopping the audio engine, its state, and reconnecting after a fault.</summary>
internal sealed partial class MainViewModel
{
    private const int MaximumRecoveryAttempts = 6;
    private readonly DispatcherTimer _recoveryTimer;
    private AudioEngineState _engineState;
    private string _statusText = "Stopped";
    private string? _errorMessage;
    private bool _intendedRunning;
    private bool _isRecovering;
    private int _recoveryAttempt;

    public AsyncRelayCommand ToggleCommand { get; }

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
                UpdateActivityForEngineState();
            }
        }
    }

    public bool IsRunning => EngineState == AudioEngineState.Running;

    public bool IsBusy => EngineState is AudioEngineState.Starting or AudioEngineState.Stopping;

    public bool IsStopped => !IsRunning && !IsBusy;

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

    public bool IsRecovering
    {
        get => _isRecovering;
        private set => SetProperty(ref _isRecovering, value);
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
}

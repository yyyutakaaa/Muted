using Muted.Audio.Windows.Devices;
using Muted.Audio.Windows.Engine;
using Muted.Core.Audio;
using Muted.Core.Settings;

namespace Muted.App.ViewModels;

/// <summary>Clean what you hear: RNNoise on incoming audio through a second virtual cable.</summary>
internal sealed partial class MainViewModel
{
    private readonly ListeningEngine _listening = new();
    private bool _listeningEnabled;
    private AudioDeviceInfo? _listeningInput;
    private AudioDeviceInfo? _listeningOutput;
    private double _listeningStrength = 1;
    private string _listeningStatus = "Off.";
    private bool _listeningBusy;
    private string _listeningReductionText = string.Empty;
    private bool _listeningPending;

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

    public string ListeningReductionText
    {
        get => _listeningReductionText;
        private set => SetProperty(ref _listeningReductionText, value);
    }

    private void ListeningTick()
    {
        if (_listening.IsRunning)
        {
            ListeningReductionText = $"{_listening.NoiseReductionDb:0.0} dB removed";
        }
    }

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

    private void InitializeListening()
    {
        _listening.Faulted += OnListeningFaulted;
    }

    private void LoadListeningSettings(AppSettings settings)
    {
        _listeningEnabled = settings.ListeningEnabled;
        _listeningStrength = settings.ListeningStrength;
        OnPropertyChanged(nameof(ListeningEnabled));
        OnPropertyChanged(nameof(ListeningStrength));
        OnPropertyChanged(nameof(ListeningStrengthText));
    }

    /// <summary>Runs once devices are known: picks the listening devices and starts listening if it was on.</summary>
    private void StartListening()
    {
        SelectListeningDevices(_settings.ListeningInputDeviceId, _settings.ListeningOutputDeviceId);
        if (ListeningEnabled)
        {
            _ = ApplyListeningAsync();
        }
    }

    private AppSettings WithListeningSettings(AppSettings settings) => settings with
    {
        ListeningEnabled = ListeningEnabled,
        ListeningInputDeviceId = ListeningInput?.Id,
        ListeningOutputDeviceId = ListeningOutput?.Id,
        ListeningStrength = (float)ListeningStrength
    };

    private async Task DisposeListeningAsync()
    {
        _listening.Faulted -= OnListeningFaulted;
        await _listening.DisposeAsync();
    }
}

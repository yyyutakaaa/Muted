using System.Collections.ObjectModel;
using Muted.App.Infrastructure;
using Muted.Audio.Windows.Devices;
using Muted.Core.Audio;

namespace Muted.App.ViewModels;

/// <summary>The microphone and cable lists, the selected devices and reacting to devices that come and go.</summary>
internal sealed partial class MainViewModel
{
    private const string NoDevicesError = "Windows did not report any usable audio devices.";
    private const string DeviceReadError = "Audio devices could not be read.";
    public const string VirtualCableUrl = "https://vb-audio.com/Cable/";
    private AudioDeviceInfo? _selectedInput;
    private AudioDeviceInfo? _selectedOutput;
    private bool _isRefreshingDevices;
    private int _refreshPending;
    private int _deferredDeviceRefresh;

    public ObservableCollection<AudioDeviceInfo> InputDevices { get; } = [];

    public ObservableCollection<AudioDeviceInfo> OutputDevices { get; } = [];

    public RelayCommand OpenCableDownloadCommand { get; }

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

    public bool CanSelectInput => IsStopped && !FollowDefaultInput;

    public bool HasDevices => SelectedInput is not null && SelectedOutput is not null;

    public bool IsRoutingReady => WasapiDeviceCatalog.IsLikelyVirtualCable(SelectedOutput?.Name);

    /// <summary>False when Windows reports no virtual cable at all, which drives onboarding.</summary>
    public bool HasVirtualCable =>
        OutputDevices.Any(device => WasapiDeviceCatalog.IsLikelyVirtualCable(device.Name));

    public bool NeedsVirtualCableSetup => !HasVirtualCable;

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

            SelectListeningDevices(null, null);
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
}

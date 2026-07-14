using Muted.Core.Audio;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Muted.Audio.Windows.Devices;

public sealed class WasapiDeviceCatalog : IMMNotificationClient, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private int _disposed;

    public WasapiDeviceCatalog()
    {
        _enumerator.RegisterEndpointNotificationCallback(this);
    }

    public event EventHandler? DevicesChanged;

    public IReadOnlyList<AudioDeviceInfo> GetInputDevices() =>
        Enumerate(DataFlow.Capture, AudioDeviceKind.Input);

    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices() =>
        Enumerate(DataFlow.Render, AudioDeviceKind.Output);

    public AudioDeviceFormatInfo GetMixFormat(string deviceId)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        using var device = _enumerator.GetDevice(deviceId);
        var format = device.AudioClient.MixFormat;
        return new AudioDeviceFormatInfo(format.SampleRate, format.Channels, format.BitsPerSample);
    }

    public static bool IsLikelyVirtualCable(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        string[] markers =
        [
            "cable input",
            "vb-audio",
            "voicemeeter input",
            "virtual audio",
            "virtual cable",
            "sonar - microphone",
            "vac input"
        ];

        return markers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private IReadOnlyList<AudioDeviceInfo> Enumerate(DataFlow flow, AudioDeviceKind kind)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        string? defaultId = null;
        if (_enumerator.HasDefaultAudioEndpoint(flow, Role.Communications))
        {
            using var defaultDevice = _enumerator.GetDefaultAudioEndpoint(flow, Role.Communications);
            defaultId = defaultDevice.ID;
        }
        else if (_enumerator.HasDefaultAudioEndpoint(flow, Role.Console))
        {
            using var defaultDevice = _enumerator.GetDefaultAudioEndpoint(flow, Role.Console);
            defaultId = defaultDevice.ID;
        }

        var devices = new List<AudioDeviceInfo>();
        foreach (var device in _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (device)
            {
                devices.Add(new AudioDeviceInfo(
                    device.ID,
                    device.FriendlyName,
                    kind,
                    string.Equals(device.ID, defaultId, StringComparison.Ordinal)));
            }
        }

        return devices
            .OrderByDescending(device => device.IsDefault)
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState) => RaiseChanged();

    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) => RaiseChanged();

    void IMMNotificationClient.OnDeviceRemoved(string deviceId) => RaiseChanged();

    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => RaiseChanged();

    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) => RaiseChanged();

    private void RaiseChanged()
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _enumerator.UnregisterEndpointNotificationCallback(this);
        _enumerator.Dispose();
    }
}

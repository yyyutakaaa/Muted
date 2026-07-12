namespace Muted.Core.Audio;

public enum AudioDeviceKind
{
    Input,
    Output
}

public sealed record AudioDeviceInfo(
    string Id,
    string Name,
    AudioDeviceKind Kind,
    bool IsDefault = false)
{
    public string DisplayName => IsDefault ? $"{Name}  (standaard)" : Name;
}

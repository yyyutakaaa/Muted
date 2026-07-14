using Muted.Core.Audio;

namespace Muted.Core.Settings;

public sealed record AudioProfile
{
    public const int MaximumNameLength = 40;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Profile";
    public string? InputDeviceId { get; init; }
    public string? OutputDeviceId { get; init; }
    public bool SuppressionEnabled { get; init; } = true;
    public float WetMix { get; init; } = 1f;
    public bool VoiceGateEnabled { get; init; } = true;
    public float VoiceThreshold { get; init; } = 0.55f;
    public int VoiceHoldMilliseconds { get; init; } = 250;

    public AudioProfile Normalize()
    {
        var normalizedName = string.IsNullOrWhiteSpace(Name) ? "Profile" : Name.Trim();
        if (normalizedName.Length > MaximumNameLength)
        {
            normalizedName = normalizedName[..MaximumNameLength];
        }

        return this with
        {
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim(),
            Name = normalizedName,
            WetMix = Math.Clamp(WetMix, 0f, 1f),
            VoiceThreshold = Math.Clamp(VoiceThreshold, 0.05f, 0.99f),
            VoiceHoldMilliseconds = Math.Clamp(VoiceHoldMilliseconds, 0, 2_000)
        };
    }

    public SuppressionOptions ToSuppressionOptions(bool isMuted = false) => new(
        SuppressionEnabled,
        WetMix,
        VoiceGateEnabled,
        VoiceThreshold,
        VoiceHoldMilliseconds,
        IsMuted: isMuted);

    public static IReadOnlyList<AudioProfile> CreateDefaults(
        string? inputDeviceId = null,
        string? outputDeviceId = null) =>
    [
        new AudioProfile
        {
            Id = "balanced",
            Name = "Balanced",
            InputDeviceId = inputDeviceId,
            OutputDeviceId = outputDeviceId,
            VoiceThreshold = 0.55f,
            VoiceHoldMilliseconds = 250
        },
        new AudioProfile
        {
            Id = "meeting",
            Name = "Meeting",
            InputDeviceId = inputDeviceId,
            OutputDeviceId = outputDeviceId,
            VoiceThreshold = 0.42f,
            VoiceHoldMilliseconds = 400
        },
        new AudioProfile
        {
            Id = "gaming",
            Name = "Gaming",
            InputDeviceId = inputDeviceId,
            OutputDeviceId = outputDeviceId,
            VoiceThreshold = 0.68f,
            VoiceHoldMilliseconds = 160
        }
    ];
}

using System.Text.Json;

namespace Muted.Core.Settings;

/// <summary>Portable settings only: device identifiers never leave this computer.</summary>
public sealed record ProfileExchange(int Version, AudioProfile[] Profiles)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static string Export(IEnumerable<AudioProfile> profiles) => JsonSerializer.Serialize(
        new ProfileExchange(1, profiles.Select(Portable).ToArray()), Options);

    public static AudioProfile[] Import(string json)
    {
        if (json.Length > 256_000) throw new InvalidDataException("Profile file is too large.");
        var package = JsonSerializer.Deserialize<ProfileExchange>(json, Options);
        if (package is null || package.Version != 1 || package.Profiles is null ||
            package.Profiles.Length is < 1 or > AppSettings.MaximumProfiles)
            throw new InvalidDataException("Unsupported or empty profile file.");
        return package.Profiles.Select(profile =>
        {
            if (profile is null || !float.IsFinite(profile.InputGain) || !float.IsFinite(profile.OutputGain) ||
                !float.IsFinite(profile.WetMix) || !float.IsFinite(profile.VoiceThreshold) ||
                !float.IsFinite(profile.HighPassFrequency) || !float.IsFinite(profile.AutoGainTargetDb) ||
                !float.IsFinite(profile.EchoStrength))
                throw new InvalidDataException("Profile contains invalid values.");
            return Portable(profile.Normalize()) with { Id = Guid.NewGuid().ToString("N") };
        }).ToArray();
    }

    private static AudioProfile Portable(AudioProfile profile) => profile with
    { InputDeviceId = null, OutputDeviceId = null, EchoReferenceDeviceId = null };
}

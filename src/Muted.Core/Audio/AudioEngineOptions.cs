using Muted.Core.Dsp;

namespace Muted.Core.Audio;

public sealed record AudioEngineOptions(
    string? InputDeviceId,
    string? OutputDeviceId,
    int LatencyMilliseconds,
    SuppressionOptions Suppression,
    MonitorOptions? Monitor = null,
    EchoOptions? Echo = null)
{
    public AudioEngineOptions Normalize() => this with
    {
        LatencyMilliseconds = Math.Clamp(LatencyMilliseconds, 20, 100),
        Suppression = Suppression.Normalize(),
        Monitor = (Monitor ?? MonitorOptions.Disabled).Normalize(),
        Echo = (Echo ?? EchoOptions.Disabled).Normalize()
    };
}

/// <summary>
/// Echo cancellation needs a reference: a loopback of whatever the speakers are
/// playing. Without a device to listen to there is nothing to subtract.
/// </summary>
public sealed record EchoOptions(
    bool Enabled = false,
    string? ReferenceDeviceId = null,
    float Strength = 0.5f)
{
    public static EchoOptions Disabled { get; } = new();

    public bool IsActive => Enabled && !string.IsNullOrWhiteSpace(ReferenceDeviceId);

    public EchoOptions Normalize() => this with
    {
        Strength = Math.Clamp(Strength, 0f, 1f)
    };
}

/// <summary>Optional second render path so you can hear your own processed voice.</summary>
public sealed record MonitorOptions(
    bool Enabled = false,
    string? DeviceId = null,
    float Volume = 0.6f)
{
    public static MonitorOptions Disabled { get; } = new();

    public bool IsActive => Enabled && !string.IsNullOrWhiteSpace(DeviceId);

    public MonitorOptions Normalize() => this with
    {
        Volume = Math.Clamp(Volume, 0f, 1f)
    };
}

public sealed record SuppressionOptions(
    bool Enabled = true,
    float WetMix = 1f,
    bool VoiceGateEnabled = true,
    float VoiceThreshold = 0.55f,
    int VoiceHoldMilliseconds = 250,
    float InputGain = 1f,
    float OutputGain = 1f,
    bool IsMuted = false,
    bool HighPassEnabled = false,
    float HighPassFrequency = 80f,
    bool LimiterEnabled = true,
    bool AutoGainEnabled = false,
    float AutoGainTargetDb = -18f,
    VoiceCharacterOptions? Character = null,
    ShoutGuardOptions? ShoutGuard = null)
{
    public SuppressionOptions Normalize() => this with
    {
        WetMix = Math.Clamp(WetMix, 0f, 1f),
        VoiceThreshold = Math.Clamp(VoiceThreshold, 0.05f, 0.99f),
        VoiceHoldMilliseconds = Math.Clamp(VoiceHoldMilliseconds, 0, 2_000),
        InputGain = Math.Clamp(InputGain, 0.25f, 4f),
        OutputGain = Math.Clamp(OutputGain, 0.25f, 4f),
        HighPassFrequency = Math.Clamp(HighPassFrequency, 40f, 200f),
        AutoGainTargetDb = Math.Clamp(AutoGainTargetDb, -30f, -6f),
        Character = (Character ?? VoiceCharacterOptions.Disabled).Normalize(),
        ShoutGuard = (ShoutGuard ?? ShoutGuardOptions.Disabled).Normalize()
    };
}

/// <summary>
/// The warm, close-up radio voice: low-end warmth, proximity, a softened top with a
/// de-esser, gentle compression and a touch of saturation. Every amount is 0..1.
/// </summary>
public sealed record VoiceCharacterOptions(
    bool Enabled = false,
    float Warmth = 0.6f,
    float Proximity = 0.5f,
    float Smoothness = 0.6f,
    float Compression = 0.5f,
    float Saturation = 0.3f)
{
    public static VoiceCharacterOptions Disabled { get; } = new();

    public VoiceCharacterOptions Normalize() => this with
    {
        Warmth = Amount(Warmth),
        Proximity = Amount(Proximity),
        Smoothness = Amount(Smoothness),
        Compression = Amount(Compression),
        Saturation = Amount(Saturation)
    };

    private static float Amount(float value) => float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;
}

/// <summary>
/// Cuts the output when you shout, so the other side's ears are spared. The threshold
/// is a peak level of the microphone after the boost, in dBFS.
/// </summary>
public sealed record ShoutGuardOptions(
    bool Enabled = false,
    float ThresholdDb = -6f,
    ShoutGuardMode Mode = ShoutGuardMode.Silence)
{
    public static ShoutGuardOptions Disabled { get; } = new();

    public ShoutGuardOptions Normalize() => this with
    {
        ThresholdDb = float.IsFinite(ThresholdDb)
            ? Math.Clamp(ThresholdDb, Dsp.ShoutGuard.MinimumThresholdDb, Dsp.ShoutGuard.MaximumThresholdDb)
            : -6f,
        Mode = Enum.IsDefined(Mode) ? Mode : ShoutGuardMode.Silence
    };
}

namespace Muted.Core.Settings;

public enum HotkeyAction
{
    ToggleMute,
    PushToTalk,
    PushToMute,
    ToggleSuppression,
    ToggleEngine,
    ShowWindow
}

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
    Windows = 8
}

/// <summary>
/// One global shortcut. <see cref="VirtualKey"/> is a Win32 VK code so the binding
/// survives a settings round-trip without depending on any UI type.
/// </summary>
public sealed record HotkeyBinding
{
    public HotkeyAction Action { get; init; }
    public int VirtualKey { get; init; }
    public HotkeyModifiers Modifiers { get; init; }
    public bool Enabled { get; init; }

    public bool IsAssigned => Enabled && VirtualKey > 0;

    /// <summary>True when the action is only active while the key is held down.</summary>
    public bool IsHold => Action is HotkeyAction.PushToTalk or HotkeyAction.PushToMute;

    public HotkeyBinding Normalize() => this with
    {
        VirtualKey = VirtualKey is > 0 and < 256 ? VirtualKey : 0,
        Enabled = Enabled && VirtualKey is > 0 and < 256
    };

    public static IReadOnlyList<HotkeyBinding> CreateDefaults() =>
        Enum.GetValues<HotkeyAction>()
            .Select(action => new HotkeyBinding { Action = action })
            .ToArray();

    /// <summary>Fills in any action the stored settings do not mention yet.</summary>
    public static IReadOnlyList<HotkeyBinding> Complete(IEnumerable<HotkeyBinding>? bindings)
    {
        var known = (bindings ?? [])
            .Where(binding => binding is not null)
            .Select(binding => binding.Normalize())
            .DistinctBy(binding => binding.Action)
            .ToDictionary(binding => binding.Action);

        return Enum.GetValues<HotkeyAction>()
            .Select(action => known.TryGetValue(action, out var binding)
                ? binding
                : new HotkeyBinding { Action = action })
            .ToArray();
    }

    public static string DescribeAction(HotkeyAction action) => action switch
    {
        HotkeyAction.ToggleMute => "Mute / unmute",
        HotkeyAction.PushToTalk => "Push to talk",
        HotkeyAction.PushToMute => "Push to mute",
        HotkeyAction.ToggleSuppression => "Toggle RNNoise",
        HotkeyAction.ToggleEngine => "Start / stop Muted",
        HotkeyAction.ShowWindow => "Show the window",
        _ => action.ToString()
    };

    public static string DescribeHint(HotkeyAction action) => action switch
    {
        HotkeyAction.ToggleMute => "Silences the cable without stopping the pipeline.",
        HotkeyAction.PushToTalk => "Stays muted until you hold this key.",
        HotkeyAction.PushToMute => "Mutes only while you hold this key.",
        HotkeyAction.ToggleSuppression => "Switches noise suppression on or off.",
        HotkeyAction.ToggleEngine => "Starts or stops the whole audio pipeline.",
        HotkeyAction.ShowWindow => "Brings the Muted window back to the front.",
        _ => string.Empty
    };
}

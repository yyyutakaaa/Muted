using System.Text;
using System.Windows.Input;
using Muted.Core.Settings;

namespace Muted.App.Services;

internal static class HotkeyText
{
    /// <summary>Renders a binding the way people write shortcuts: "Ctrl + Shift + M".</summary>
    public static string Describe(HotkeyModifiers modifiers, int virtualKey)
    {
        if (virtualKey <= 0)
        {
            return "Not set";
        }

        var builder = new StringBuilder();
        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            builder.Append("Ctrl + ");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            builder.Append("Alt + ");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            builder.Append("Shift + ");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            builder.Append("Win + ");
        }

        builder.Append(DescribeKey(virtualKey));
        return builder.ToString();
    }

    public static string DescribeKey(int virtualKey)
    {
        var key = KeyInterop.KeyFromVirtualKey(virtualKey);
        return key switch
        {
            Key.None => $"Key {virtualKey}",
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => $"Numpad {key - Key.NumPad0}",
            Key.Oem3 => "`",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.OemOpenBrackets => "[",
            Key.Oem6 => "]",
            Key.Oem5 => "\\",
            Key.Oem1 => ";",
            Key.OemQuotes => "'",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            Key.Space => "Space",
            Key.Escape => "Esc",
            Key.Prior => "Page Up",
            Key.Next => "Page Down",
            Key.Snapshot => "Print Screen",
            _ => key.ToString()
        };
    }

    /// <summary>Modifier keys cannot be a shortcut on their own.</summary>
    public static bool IsModifierKey(Key key) => key
        is Key.LeftCtrl or Key.RightCtrl
        or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt
        or Key.LWin or Key.RWin
        or Key.System;

    public static HotkeyModifiers FromModifierKeys(ModifierKeys modifiers)
    {
        var result = HotkeyModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            result |= HotkeyModifiers.Control;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            result |= HotkeyModifiers.Shift;
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            result |= HotkeyModifiers.Alt;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            result |= HotkeyModifiers.Windows;
        }

        return result;
    }
}

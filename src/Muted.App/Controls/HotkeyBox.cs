using System.Windows;
using System.Windows.Input;
using Muted.App.Services;
using Muted.Core.Settings;

using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Muted.App.Controls;

/// <summary>
/// Click, then press a combination: the button records the next key and writes it back
/// as a Win32 virtual-key code. Escape cancels, Backspace or Delete clears the binding.
/// </summary>
public sealed class HotkeyBox : System.Windows.Controls.Button
{
    public static readonly DependencyProperty VirtualKeyProperty = DependencyProperty.Register(
        nameof(VirtualKey),
        typeof(int),
        typeof(HotkeyBox),
        new FrameworkPropertyMetadata(
            0,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnBindingChanged));

    public static readonly DependencyProperty ModifiersProperty = DependencyProperty.Register(
        nameof(Modifiers),
        typeof(HotkeyModifiers),
        typeof(HotkeyBox),
        new FrameworkPropertyMetadata(
            HotkeyModifiers.None,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnBindingChanged));

    public static readonly DependencyProperty IsRecordingProperty = DependencyProperty.Register(
        nameof(IsRecording),
        typeof(bool),
        typeof(HotkeyBox),
        new PropertyMetadata(false, OnBindingChanged));

    public HotkeyBox()
    {
        Focusable = true;
        UpdateContent();
    }

    public int VirtualKey
    {
        get => (int)GetValue(VirtualKeyProperty);
        set => SetValue(VirtualKeyProperty, value);
    }

    public HotkeyModifiers Modifiers
    {
        get => (HotkeyModifiers)GetValue(ModifiersProperty);
        set => SetValue(ModifiersProperty, value);
    }

    public bool IsRecording
    {
        get => (bool)GetValue(IsRecordingProperty);
        private set => SetValue(IsRecordingProperty, value);
    }

    protected override void OnClick()
    {
        base.OnClick();
        IsRecording = true;
        Focus();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs eventArgs)
    {
        base.OnLostKeyboardFocus(eventArgs);
        IsRecording = false;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs eventArgs)
    {
        if (!IsRecording)
        {
            base.OnPreviewKeyDown(eventArgs);
            return;
        }

        eventArgs.Handled = true;
        var key = eventArgs.Key == Key.System ? eventArgs.SystemKey : eventArgs.Key;

        if (key == Key.Escape)
        {
            IsRecording = false;
            return;
        }

        if (key is Key.Back or Key.Delete)
        {
            VirtualKey = 0;
            Modifiers = HotkeyModifiers.None;
            IsRecording = false;
            return;
        }

        if (HotkeyText.IsModifierKey(key))
        {
            // Wait for the real key; the modifiers are read once it arrives.
            return;
        }

        VirtualKey = KeyInterop.VirtualKeyFromKey(key);
        Modifiers = HotkeyText.FromModifierKeys(Keyboard.Modifiers);
        IsRecording = false;
    }

    private static void OnBindingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((HotkeyBox)sender).UpdateContent();

    private void UpdateContent() =>
        Content = IsRecording ? "Press a key…" : HotkeyText.Describe(Modifiers, VirtualKey);
}

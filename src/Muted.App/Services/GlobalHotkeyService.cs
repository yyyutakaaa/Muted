using System.Diagnostics;
using System.Runtime.InteropServices;
using Muted.Core.Settings;

namespace Muted.App.Services;

internal sealed class HotkeyEventArgs(HotkeyAction action, bool isPressed) : EventArgs
{
    public HotkeyAction Action { get; } = action;

    /// <summary>True on key down, false on key up. Toggle actions only fire on key down.</summary>
    public bool IsPressed { get; } = isPressed;
}

/// <summary>
/// System-wide shortcuts through a low-level keyboard hook. RegisterHotKey cannot see
/// key releases, and push-to-talk needs them, so the hook is the only workable route.
/// Keys are never swallowed: whatever you press still reaches the game or call app.
/// </summary>
internal sealed class GlobalHotkeyService : IDisposable
{
    private const int WhKeyboardLowLevel = 13;
    private const int HcAction = 0;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLeftWindows = 0x5B;
    private const int VkRightWindows = 0x5C;

    private readonly LowLevelKeyboardProc _callback;
    private readonly HashSet<HotkeyAction> _heldActions = [];
    private readonly FileLog _log;
    private IReadOnlyList<HotkeyBinding> _bindings = [];
    private IntPtr _hook;
    private bool _disposed;

    public GlobalHotkeyService(FileLog log)
    {
        _log = log;
        _callback = OnKeyboardEvent;
    }

    public event EventHandler<HotkeyEventArgs>? Triggered;

    /// <summary>True when the hook is installed, which only happens if a key is assigned.</summary>
    public bool IsActive => _hook != IntPtr.Zero;

    public void Update(IReadOnlyList<HotkeyBinding> bindings)
    {
        if (_disposed)
        {
            return;
        }

        _bindings = bindings.Where(binding => binding.IsAssigned).ToArray();
        _heldActions.Clear();

        if (_bindings.Count == 0)
        {
            Uninstall();
            return;
        }

        Install();
    }

    private void Install()
    {
        if (_hook != IntPtr.Zero)
        {
            return;
        }

        try
        {
            using var process = Process.GetCurrentProcess();
            using var module = process.MainModule;
            var moduleHandle = module is null ? IntPtr.Zero : GetModuleHandle(module.ModuleName);
            _hook = SetWindowsHookEx(WhKeyboardLowLevel, _callback, moduleHandle, 0);
            if (_hook == IntPtr.Zero)
            {
                _log.WriteMessage(
                    $"Global shortcuts could not be installed (error {Marshal.GetLastWin32Error()}).");
            }
        }
        catch (Exception exception)
        {
            _log.Write(exception, "Install global shortcuts");
        }
    }

    private void Uninstall()
    {
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr OnKeyboardEvent(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code != HcAction)
        {
            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        try
        {
            var message = (int)wParam;
            var isDown = message is WmKeyDown or WmSysKeyDown;
            var isUp = message is WmKeyUp or WmSysKeyUp;
            if (isDown || isUp)
            {
                var data = Marshal.PtrToStructure<KeyboardHookStruct>(lParam);
                Dispatch((int)data.VirtualKeyCode, isDown);
            }
        }
        catch (Exception exception)
        {
            _log.Write(exception, "Handle global shortcut");
        }

        // Never returning 1 keeps the key working in whatever app has focus.
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private void Dispatch(int virtualKey, bool isDown)
    {
        foreach (var binding in _bindings)
        {
            if (binding.VirtualKey != virtualKey)
            {
                continue;
            }

            if (isDown)
            {
                // Modifiers are only checked on the way down; they are often let go first.
                if (!ModifiersMatch(binding.Modifiers) || !_heldActions.Add(binding.Action))
                {
                    continue;
                }

                Triggered?.Invoke(this, new HotkeyEventArgs(binding.Action, true));
            }
            else if (_heldActions.Remove(binding.Action) && binding.IsHold)
            {
                Triggered?.Invoke(this, new HotkeyEventArgs(binding.Action, false));
            }
        }
    }

    private static bool ModifiersMatch(HotkeyModifiers modifiers)
    {
        // A bare key stays a bare key: people hold Shift to sprint while they talk,
        // and a push-to-talk key that dies under Shift is worse than useless.
        if (modifiers == HotkeyModifiers.None)
        {
            return true;
        }

        return (!modifiers.HasFlag(HotkeyModifiers.Control) || IsDown(VkControl)) &&
            (!modifiers.HasFlag(HotkeyModifiers.Shift) || IsDown(VkShift)) &&
            (!modifiers.HasFlag(HotkeyModifiers.Alt) || IsDown(VkMenu)) &&
            (!modifiers.HasFlag(HotkeyModifiers.Windows) ||
                IsDown(VkLeftWindows) || IsDown(VkRightWindows));
    }

    private static bool IsDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Uninstall();
    }

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookStruct
    {
        public uint VirtualKeyCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookId,
        LowLevelKeyboardProc callback,
        IntPtr moduleHandle,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}

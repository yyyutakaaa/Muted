using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Muted.Core.Settings;

using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Colors = System.Windows.Media.Colors;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace Muted.App.Services;

/// <summary>
/// Swaps the shared brushes in <see cref="Application.Resources"/> for the palette the
/// current theme asks for. The XAML reaches them through DynamicResource, so a swap
/// reaches every control that is already on screen.
/// </summary>
/// <remarks>
/// The brushes have to be replaced rather than repainted in place: WPF freezes a
/// brush the moment a Style that uses it as a setter value is sealed, and a frozen
/// brush cannot change colour again, which is what used to leave the light theme
/// looking like it did nothing at all.
/// <para>
/// Both palettes are one violet family on purpose: the accent, the running dot, the
/// meters and the waveform are all shades of the same hue, and only a genuine warning
/// or failure is allowed to break out of it in amber or rose.
/// </para>
/// </remarks>
internal sealed class ThemeService : IDisposable
{
    private static readonly Palette Dark = new(
        Background: "#0A0713",
        Surface: "#141024",
        SurfaceRaised: "#1B1631",
        SurfaceHover: "#262040",
        Sidebar: "#0D0A19",
        Border: "#2A2342",
        BorderStrong: "#4A3F70",
        Text: "#F4F1FC",
        TextMuted: "#A69EC4",
        Accent: "#A78BFA",
        AccentSoft: "#2C2450",
        Success: "#C6B4FF",
        Warning: "#F5B94D",
        Danger: "#FF6E8F",
        MeterTrack: "#221C39",
        ToggleTrack: "#352D52",
        ToggleThumb: "#E9E4FA",
        DangerSurface: "#2B1425",
        DangerBorder: "#6B3352",
        SuccessSurface: "#221A3D",
        WarningSurface: "#2C2313");

    private static readonly Palette Light = new(
        Background: "#F5F3FC",
        Surface: "#FFFFFF",
        SurfaceRaised: "#F1EDFB",
        SurfaceHover: "#E6DFF8",
        Sidebar: "#EEE9F9",
        Border: "#E2DCF1",
        BorderStrong: "#BCB0DC",
        Text: "#1A1430",
        TextMuted: "#5C5480",
        Accent: "#6D28D9",
        AccentSoft: "#EAE2FC",
        Success: "#7C3AED",
        Warning: "#A2670A",
        Danger: "#C42A55",
        MeterTrack: "#E3DDF2",
        ToggleTrack: "#C6BCE2",
        ToggleThumb: "#FFFFFF",
        DangerSurface: "#FCEBF0",
        DangerBorder: "#F0C6D4",
        SuccessSurface: "#F0E9FD",
        WarningSurface: "#FBF2DE");

    private readonly ResourceDictionary _resources;
    private readonly Dispatcher _dispatcher;
    private AppTheme _theme = AppTheme.System;
    private bool _useSystemAccent;
    private bool _applied;
    private bool _appliedDark;
    private bool _appliedUseSystemAccent;
    private Color _appliedAccent;
    private bool _disposed;

    public ThemeService(ResourceDictionary resources, Dispatcher dispatcher)
    {
        _resources = resources;
        _dispatcher = dispatcher;

        // Windows announces a light/dark switch and an accent change through the same
        // event, so "Follow Windows" keeps following after startup instead of only at it.
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>
    /// Raised on the UI thread once the resource dictionary holds the new palette.
    /// DynamicResource carries the swap into the visual tree by itself; this is for
    /// the few things Windows draws for us, such as the titlebar and the backdrop.
    /// </summary>
    public event EventHandler? Changed;

    public bool IsDark { get; private set; } = true;

    public Color AccentColor { get; private set; } = (Color)ColorConverter.ConvertFromString(Dark.Accent);

    public void Apply(AppTheme theme, bool useSystemAccent)
    {
        _theme = theme;
        _useSystemAccent = useSystemAccent;
        ApplyCore();
    }

    public static bool SystemPrefersLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    private void ApplyCore()
    {
        var dark = _theme switch
        {
            AppTheme.Light => false,
            AppTheme.Dark => true,
            _ => !SystemPrefersLight()
        };

        var palette = dark ? Dark : Light;
        var accent = Parse(palette.Accent);
        if (_useSystemAccent && TryReadSystemAccent(out var systemAccent))
        {
            accent = MakeReadable(systemAccent, dark);
        }

        // Windows repeats its notification several times per change, and every swap
        // costs the controls a redraw, so identical work is skipped.
        var moved = !_applied ||
            _appliedDark != dark ||
            _appliedUseSystemAccent != _useSystemAccent ||
            _appliedAccent != accent;
        if (!moved)
        {
            // Still worth telling: whether the Mica backdrop may be used depends on
            // the Windows theme, which can move while ours stands still.
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        _applied = true;
        _appliedDark = dark;
        _appliedUseSystemAccent = _useSystemAccent;
        _appliedAccent = accent;
        IsDark = dark;
        AccentColor = accent;

        SetBrush("BackgroundBrush", palette.Background);
        SetBrush("SurfaceBrush", palette.Surface);
        SetBrush("SurfaceRaisedBrush", palette.SurfaceRaised);
        SetBrush("SurfaceHoverBrush", palette.SurfaceHover);
        SetBrush("SidebarBrush", palette.Sidebar);
        SetBrush("BorderBrush", palette.Border);
        SetBrush("BorderStrongBrush", palette.BorderStrong);
        SetBrush("TextBrush", palette.Text);
        SetBrush("TextMutedBrush", palette.TextMuted);
        SetBrush("SuccessBrush", palette.Success);
        SetBrush("WarningBrush", palette.Warning);
        SetBrush("DangerBrush", palette.Danger);
        SetBrush("MeterTrackBrush", palette.MeterTrack);
        SetBrush("ToggleTrackBrush", palette.ToggleTrack);
        SetBrush("ToggleThumbBrush", palette.ToggleThumb);
        SetBrush("DangerSurfaceBrush", palette.DangerSurface);
        SetBrush("DangerBorderBrush", palette.DangerBorder);
        SetBrush("SuccessSurfaceBrush", palette.SuccessSurface);
        SetBrush("WarningSurfaceBrush", palette.WarningSurface);
        SetColor("AccentBrush", accent);
        SetColor("AccentSoftBrush", _useSystemAccent
            ? Blend(accent, dark ? Parse(palette.Surface) : Colors.White, dark ? 0.72f : 0.78f)
            : Parse(palette.AccentSoft));

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs eventArgs)
    {
        if (eventArgs.Category is not (UserPreferenceCategory.General
            or UserPreferenceCategory.Color
            or UserPreferenceCategory.VisualStyle))
        {
            return;
        }

        // No shortcut for a fixed Light or Dark: even then the Windows theme decides
        // whether the window may keep its Mica backdrop.
        // Windows raises this on its own thread, and the brushes belong to the UI one.
        _ = _dispatcher.BeginInvoke(() =>
        {
            if (!_disposed)
            {
                ApplyCore();
            }
        });
    }

    private static bool TryReadSystemAccent(out Color color)
    {
        color = default;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is not int stored)
            {
                return false;
            }

            // DWM stores the accent as ABGR.
            var value = unchecked((uint)stored);
            color = Color.FromRgb(
                (byte)(value & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)((value >> 16) & 0xFF));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Keeps a system accent legible against the current surface colour.</summary>
    private static Color MakeReadable(Color color, bool dark)
    {
        var luminance = ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255d;
        if (dark && luminance < 0.32)
        {
            return Blend(color, Colors.White, 0.45f);
        }

        if (!dark && luminance > 0.62)
        {
            return Blend(color, Colors.Black, 0.35f);
        }

        return color;
    }

    private static Color Blend(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromRgb(
            (byte)(from.R + ((to.R - from.R) * amount)),
            (byte)(from.G + ((to.G - from.G) * amount)),
            (byte)(from.B + ((to.B - from.B) * amount)));
    }

    private void SetBrush(string key, string hex) => SetColor(key, Parse(hex));

    private void SetColor(string key, Color color)
    {
        if (_resources[key] is SolidColorBrush brush && brush.Color == color)
        {
            return;
        }

        var replacement = new SolidColorBrush(color);
        replacement.Freeze();
        _resources[key] = replacement;
    }

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private sealed record Palette(
        string Background,
        string Surface,
        string SurfaceRaised,
        string SurfaceHover,
        string Sidebar,
        string Border,
        string BorderStrong,
        string Text,
        string TextMuted,
        string Accent,
        string AccentSoft,
        string Success,
        string Warning,
        string Danger,
        string MeterTrack,
        string ToggleTrack,
        string ToggleThumb,
        string DangerSurface,
        string DangerBorder,
        string SuccessSurface,
        string WarningSurface);
}

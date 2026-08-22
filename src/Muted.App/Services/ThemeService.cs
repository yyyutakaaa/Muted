using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Muted.Core.Settings;

using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Colors = System.Windows.Media.Colors;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace Muted.App.Services;

/// <summary>
/// Repaints the shared brushes in <see cref="Application.Resources"/> instead of
/// swapping dictionaries, so every existing StaticResource reference follows along.
/// </summary>
internal sealed class ThemeService
{
    private static readonly Palette Dark = new(
        Background: "#0B0C10",
        Surface: "#13151B",
        SurfaceRaised: "#191C24",
        SurfaceHover: "#242834",
        Sidebar: "#0E1015",
        Border: "#292D38",
        BorderStrong: "#454A59",
        Text: "#F5F6FA",
        TextMuted: "#9499A8",
        Accent: "#8B7CFF",
        AccentSoft: "#2A254F",
        Success: "#48D6A2",
        Warning: "#F2B84B",
        Danger: "#FF6B7A",
        MeterTrack: "#222630",
        ToggleTrack: "#303440",
        ToggleThumb: "#D9DCE5",
        DangerSurface: "#2A151A",
        DangerBorder: "#66313B",
        SuccessSurface: "#14261F",
        WarningSurface: "#2A2312");

    private static readonly Palette Light = new(
        Background: "#F3F4F8",
        Surface: "#FFFFFF",
        SurfaceRaised: "#F1F2F7",
        SurfaceHover: "#E5E7F0",
        Sidebar: "#EBECF2",
        Border: "#DCDEE8",
        BorderStrong: "#C2C6D4",
        Text: "#14161C",
        TextMuted: "#5A6072",
        Accent: "#6350E0",
        AccentSoft: "#E4E0FA",
        Success: "#12916B",
        Warning: "#A96C0C",
        Danger: "#CC3446",
        MeterTrack: "#DFE1EA",
        ToggleTrack: "#C7CAD6",
        ToggleThumb: "#FFFFFF",
        DangerSurface: "#FCEDEF",
        DangerBorder: "#F0C3C8",
        SuccessSurface: "#E5F5EF",
        WarningSurface: "#FBF1DE");

    private readonly ResourceDictionary _resources;

    public ThemeService(ResourceDictionary resources)
    {
        _resources = resources;
    }

    public event EventHandler? Changed;

    public bool IsDark { get; private set; } = true;

    public Color AccentColor { get; private set; } = (Color)ColorConverter.ConvertFromString(Dark.Accent);

    public void Apply(AppTheme theme, bool useSystemAccent)
    {
        var dark = theme switch
        {
            AppTheme.Light => false,
            AppTheme.Dark => true,
            _ => !SystemPrefersLight()
        };

        var palette = dark ? Dark : Light;
        var accent = Parse(palette.Accent);
        if (useSystemAccent && TryReadSystemAccent(out var systemAccent))
        {
            accent = MakeReadable(systemAccent, dark);
        }

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
        SetColor("AccentSoftBrush", useSystemAccent
            ? Blend(accent, dark ? Parse(palette.Surface) : Colors.White, dark ? 0.72f : 0.78f)
            : Parse(palette.AccentSoft));

        Changed?.Invoke(this, EventArgs.Empty);
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
        if (_resources[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
        }
        else
        {
            _resources[key] = new SolidColorBrush(color);
        }
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

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Muted.App.Services;

namespace Muted.App;

/// <summary>The update prompt and its follow-up notices, drawn in Muted's own style.</summary>
public partial class UpdateDialog : Window
{
    private Func<IProgress<double>, Task<bool>>? _install;
    private string? _releaseUrl;
    private bool _isInstalling;

    private UpdateDialog(Window? owner)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyWindowAppearance();

        if (owner is { IsVisible: true, WindowState: not WindowState.Minimized })
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            // Muted is in the tray: keep the prompt findable without a main window.
            Topmost = true;
            ShowInTaskbar = true;
        }
    }

    internal UpdateDialogResult Result { get; private set; } = UpdateDialogResult.Later;

    /// <summary>
    /// Asks whether to install <paramref name="update"/>. When the user agrees, the
    /// dialog stays open with a progress bar while <paramref name="install"/> runs.
    /// </summary>
    internal static UpdateDialogResult ShowUpdate(
        Window? owner,
        AvailableUpdate update,
        Func<IProgress<double>, Task<bool>> install)
    {
        var dialog = CreateUpdate(owner, update, install);
        dialog.ShowDialog();
        return dialog.Result;
    }

    internal static UpdateDialog CreateUpdate(
        Window? owner,
        AvailableUpdate update,
        Func<IProgress<double>, Task<bool>> install)
    {
        var dialog = new UpdateDialog(owner) { _install = install, _releaseUrl = update.ReleaseUrl };
        var version = update.AvailableName;
        dialog.TitleText.Text = $"Muted {version} is here";
        dialog.SubtitleText.Text = "A new version is ready to install.";
        dialog.CurrentVersionText.Text = update.CurrentName;
        dialog.NewVersionText.Text = version;
        dialog.MessageText.Text =
            "Muted downloads the update, closes for a moment and restarts on its own. " +
            "Your settings stay as they are.";
        dialog.ReleaseNotesLink.Visibility = string.IsNullOrWhiteSpace(update.ReleaseUrl)
            ? Visibility.Collapsed
            : Visibility.Visible;
        return dialog;
    }

    /// <summary>A one-button notice such as "up to date" or "could not check".</summary>
    internal static void ShowNotice(Window? owner, string title, string message, UpdateNoticeKind kind) =>
        CreateNotice(owner, title, message, kind).ShowDialog();

    internal static UpdateDialog CreateNotice(Window? owner, string title, string message, UpdateNoticeKind kind)
    {
        var dialog = new UpdateDialog(owner);
        dialog.TitleText.Text = title;
        dialog.SubtitleText.Visibility = Visibility.Collapsed;
        dialog.VersionCard.Visibility = Visibility.Collapsed;
        dialog.MessageText.Text = message;
        dialog.SkipButton.Visibility = Visibility.Collapsed;
        dialog.LaterButton.Visibility = Visibility.Collapsed;
        dialog.PrimaryGlyph.Visibility = Visibility.Collapsed;
        dialog.PrimaryText.Text = "OK";
        dialog.ApplyKind(kind);
        return dialog;
    }

    private void ApplyKind(UpdateNoticeKind kind)
    {
        var (glyph, brush, surface) = kind switch
        {
            UpdateNoticeKind.UpToDate => ("", "SuccessBrush", "AccentSoftBrush"),
            UpdateNoticeKind.Warning => ("", "WarningBrush", "WarningSurfaceBrush"),
            UpdateNoticeKind.Error => ("", "DangerBrush", "DangerSurfaceBrush"),
            _ => ("", "AccentBrush", "AccentSoftBrush")
        };
        BadgeGlyph.Text = glyph;
        BadgeGlyph.SetResourceReference(ForegroundProperty, brush);
        Badge.SetResourceReference(BackgroundProperty, surface);
    }

    private async void PrimaryButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_install is null || Result == UpdateDialogResult.Failed)
        {
            Close();
            return;
        }

        _isInstalling = true;
        SkipButton.Visibility = Visibility.Collapsed;
        LaterButton.Visibility = Visibility.Collapsed;
        CloseButton.Visibility = Visibility.Hidden;
        PrimaryButton.IsEnabled = false;
        PrimaryGlyph.Visibility = Visibility.Collapsed;
        PrimaryText.Text = "Installing…";
        ProgressPanel.Visibility = Visibility.Visible;
        SetProgress(0);

        var progress = new Progress<double>(SetProgress);
        bool started;
        try
        {
            started = await _install(progress);
        }
        catch (Exception)
        {
            started = false;
        }

        _isInstalling = false;
        if (started)
        {
            Result = UpdateDialogResult.Installing;
            ProgressLabel.Text = "Starting the installer…";
            SetProgress(1);
            Close();
            return;
        }

        Result = UpdateDialogResult.Failed;
        ProgressPanel.Visibility = Visibility.Collapsed;
        VersionCard.Visibility = Visibility.Collapsed;
        ReleaseNotesLink.Visibility = Visibility.Collapsed;
        SubtitleText.Visibility = Visibility.Collapsed;
        TitleText.Text = "Update failed";
        MessageText.Text = "The update could not be downloaded or started. " +
                           "Check your internet connection and try again later.";
        ApplyKind(UpdateNoticeKind.Error);
        CloseButton.Visibility = Visibility.Visible;
        PrimaryText.Text = "Close";
        PrimaryButton.IsEnabled = true;
        PrimaryButton.Focus();
    }

    private void SetProgress(double value)
    {
        var track = (FrameworkElement)ProgressFill.Parent;
        var clamped = Math.Clamp(value, 0, 1);
        ProgressFill.Width = track.ActualWidth * clamped;
        ProgressPercent.Text = $"{clamped:P0}";
        ProgressLabel.Text = clamped >= 1 ? "Verifying…" : "Downloading…";
    }

    private void SkipButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        Result = UpdateDialogResult.Skip;
        Close();
    }

    private void LaterButton_OnClick(object sender, RoutedEventArgs eventArgs) => Close();

    private void CloseButton_OnClick(object sender, RoutedEventArgs eventArgs) => Close();

    private void ReleaseNotes_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (!string.IsNullOrWhiteSpace(_releaseUrl))
        {
            Shell.Open(_releaseUrl);
        }
    }

    protected override void OnClosing(CancelEventArgs eventArgs)
    {
        // Closing mid-download would leave the update half done with nobody watching.
        eventArgs.Cancel = _isInstalling;
        base.OnClosing(eventArgs);
    }

    private void Window_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton != MouseButton.Left)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The mouse may already have been released.
        }
    }

    private void ApplyWindowAppearance()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var dark = System.Windows.Application.Current.Resources["BackgroundBrush"] is SolidColorBrush background &&
                   ((0.2126 * background.Color.R) + (0.7152 * background.Color.G) + (0.0722 * background.Color.B)) < 128
            ? 1
            : 0;
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        var rounded = 2;
        _ = DwmSetWindowAttribute(handle, 33, ref rounded, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);
}

internal enum UpdateDialogResult
{
    Later,
    Skip,
    Installing,
    Failed
}

internal enum UpdateNoticeKind
{
    UpToDate,
    Info,
    Warning,
    Error
}

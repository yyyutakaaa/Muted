using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Muted.App.ViewModels;

namespace Muted.App;

public partial class MainWindow : Window
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmSystemBackdropType = 38;

    private readonly MainViewModel _viewModel;

    internal MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        FooterVersionText.Text = version is null ? "v?" : $"v{version.ToString(3)}";
        _viewModel = viewModel;
        DataContext = viewModel;
        SourceInitialized += (_, _) => ApplyWindowAppearance();
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (eventArgs.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
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

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs eventArgs) =>
        WindowState = WindowState.Minimized;

    private void MaximizeButton_OnClick(object sender, RoutedEventArgs eventArgs) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void CloseButton_OnClick(object sender, RoutedEventArgs eventArgs) => Close();

    internal void ShowDiagnostics()
    {
        _viewModel.SelectedPage = AppPage.Diagnostics;
        if (_viewModel.RunDiagnosticsCommand.CanExecute(null))
        {
            _viewModel.RunDiagnosticsCommand.Execute(null);
        }
    }

    /// <summary>Re-applies the titlebar colour after a theme switch.</summary>
    internal void ApplyWindowAppearance()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var dark = System.Windows.Application.Current.Resources["BackgroundBrush"]
            is System.Windows.Media.SolidColorBrush background && IsDark(background.Color)
            ? 1
            : 0;
        _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int));

        // DWMWCP_ROUND on Windows 11; older Windows versions safely ignore it.
        var rounded = 2;
        _ = DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref rounded, sizeof(int));

        // DWMSBT_MAINWINDOW is Mica, and it only shows through a transparent window
        // background, so the solid brush stays wherever Mica is unavailable.
        var backdrop = 2;
        var micaEnabled = Environment.OSVersion.Version.Build >= 22621 &&
            DwmSetWindowAttribute(handle, DwmSystemBackdropType, ref backdrop, sizeof(int)) == 0;
        Background = micaEnabled
            ? System.Windows.Media.Brushes.Transparent
            : (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["BackgroundBrush"];
    }

    private static bool IsDark(System.Windows.Media.Color color) =>
        ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) < 128;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window,
        int attribute,
        ref int value,
        int valueSize);
}

using System.Diagnostics;
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
    private const string SupportUrl = "https://www.buymeacoffee.com/yyyutakaaa";

    internal MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        VersionText.Text = version is null ? "Version unknown" : $"Version {version.ToString(3)}";
        FooterVersionText.Text = version is null ? "v?" : $"v{version.ToString(3)}";
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

    private void SupportButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            Process.Start(new ProcessStartInfo(SupportUrl) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // A missing browser association is not fatal; the button simply does nothing.
        }
    }

    private void ApplyWindowAppearance()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var enabled = 1;
        _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int));

        // DWMWCP_ROUND on Windows 11; older Windows versions safely ignore it.
        var rounded = 2;
        _ = DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref rounded, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window,
        int attribute,
        ref int value,
        int valueSize);
}

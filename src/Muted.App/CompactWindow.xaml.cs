using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Muted.App.ViewModels;

namespace Muted.App;

public partial class CompactWindow : Window
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;

    internal CompactWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += (_, _) => ApplyWindowAppearance();
        PositionBottomRight();
    }

    /// <summary>Raised when the panel should disappear into the tray.</summary>
    public event EventHandler? HideRequested;

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

    private void HideButton_OnClick(object sender, RoutedEventArgs eventArgs) =>
        HideRequested?.Invoke(this, EventArgs.Empty);

    private void PositionBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 24;
        Top = workArea.Bottom - 210;
    }

    internal void ApplyWindowAppearance()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var dark = System.Windows.Application.Current.Resources["BackgroundBrush"]
            is System.Windows.Media.SolidColorBrush background &&
            ((0.2126 * background.Color.R) + (0.7152 * background.Color.G) + (0.0722 * background.Color.B)) < 128
                ? 1
                : 0;
        _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
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

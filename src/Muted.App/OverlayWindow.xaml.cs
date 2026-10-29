using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Muted.App.ViewModels;
using Muted.Core.Settings;

namespace Muted.App;

/// <summary>
/// A small "Live" or "Muted" pill on top of everything, including borderless full-screen
/// games. Clicks go straight through it and it never takes focus. Games in exclusive
/// full-screen mode draw over every window, so there it cannot show.
/// </summary>
public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExLayered = 0x00080000;
    private const int WsExNoActivate = 0x08000000;
    private const double EdgeMargin = 12;

    private readonly MainViewModel _viewModel;

    internal OverlayWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        SourceInitialized += (_, _) => MakeClickThrough();
        SizeChanged += (_, _) => PlaceOnScreen();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(MainViewModel.OverlayPosition))
        {
            PlaceOnScreen();
        }
    }

    public void PlaceOnScreen()
    {
        var area = SystemParameters.WorkArea;
        var width = ActualWidth > 0 ? ActualWidth : 90;
        var height = ActualHeight > 0 ? ActualHeight : 36;
        (Left, Top) = _viewModel.OverlayPosition switch
        {
            OverlayPosition.TopLeft => (area.Left + EdgeMargin, area.Top + EdgeMargin),
            OverlayPosition.TopRight => (area.Right - width - EdgeMargin, area.Top + EdgeMargin),
            OverlayPosition.BottomLeft => (area.Left + EdgeMargin, area.Bottom - height - EdgeMargin),
            OverlayPosition.BottomRight => (area.Right - width - EdgeMargin, area.Bottom - height - EdgeMargin),
            _ => (area.Left + ((area.Width - width) / 2), area.Top + EdgeMargin)
        };
    }

    private void MakeClickThrough()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        style |= WsExTransparent | WsExToolWindow | WsExLayered | WsExNoActivate;
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style));
        PlaceOnScreen();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}

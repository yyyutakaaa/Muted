using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Muted.App.ViewModels;
using Muted.App.Views;

namespace Muted.App;
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private AppPage? _lastSizedPage;
    internal MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width - 24);
        Height = Math.Min(Height, area.Height - 24);
        MinWidth = Math.Min(MinWidth, area.Width - 24);
        MinHeight = Math.Min(MinHeight, area.Height - 24);
        viewModel.PropertyChanged += OnViewModelChanged;
        Closed += (_, _) => viewModel.PropertyChanged -= OnViewModelChanged;
        SourceInitialized += (_, _) => ApplyWindowAppearance();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.System && e.SystemKey == Key.Space) { ShowWindowMenu(); e.Handled = true; }
            if (e.Key == Key.Escape) { viewModel.SelectedPage = AppPage.Dashboard; e.Handled = true; }
        };
        ShowPage();
    }
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ShowSetupGuide) && _viewModel.ShowSetupGuide && _viewModel.SelectedPage == AppPage.Settings) PageScroll.ScrollToTop();
        if (e.PropertyName == nameof(MainViewModel.SelectedPage)) ShowPage();
    }
    private void ShowPage()
    {
        if (_lastSizedPage != _viewModel.SelectedPage &&
            _viewModel.SelectedPage is AppPage.Dashboard or AppPage.Advanced)
        {
            _lastSizedPage = _viewModel.SelectedPage;
            if (WindowState == WindowState.Normal)
            {
                var area = SystemParameters.WorkArea;
                var advanced = _viewModel.SelectedPage == AppPage.Advanced;
                Width = Math.Min(advanced ? 980 : 690, area.Width - 24);
                Height = Math.Min(advanced ? 980 : 830, area.Height - 24);
            }
        }
        PageHost.Content = _viewModel.SelectedPage switch
        {
            AppPage.Settings => new EverydaySettingsView(),
            AppPage.Advanced => new AdvancedView(),
            AppPage.Audio => new AudioView(),
            AppPage.Shortcuts => new ShortcutsView(),
            AppPage.Presets => new FeaturesView(),
            AppPage.Diagnostics => new DiagnosticsView(),
            AppPage.About => new AboutView(),
            _ => new SimpleView()
        };
        PageScroll.ScrollToTop();
    }
    private void Back_OnClick(object sender, RoutedEventArgs e) => _viewModel.SelectedPage = AppPage.Dashboard;
    private void Minimize_OnClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Advanced_OnClick(object sender, RoutedEventArgs e) => _viewModel.SelectedPage = AppPage.Advanced;
    private void Settings_OnClick(object sender, RoutedEventArgs e) => _viewModel.SelectedPage =
        _viewModel.SelectedPage == AppPage.Settings ? AppPage.Dashboard : AppPage.Settings;
    private void WindowMenu_OnClick(object sender, RoutedEventArgs e) => ShowWindowMenu();
    private void ShowWindowMenu() => SystemCommands.ShowSystemMenu(this, PointToScreen(new System.Windows.Point(28, 58)));
    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2) return;
        try { DragMove(); } catch (InvalidOperationException) { }
    }
    internal void ShowDiagnostics()
    {
        _viewModel.SelectedPage = AppPage.Diagnostics;
        if (_viewModel.RunDiagnosticsCommand.CanExecute(null)) _viewModel.RunDiagnosticsCommand.Execute(null);
    }
    internal void ApplyWindowAppearance()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var dark = Services.ThemeService.SystemPrefersLight() ? 0 : 1;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        var rounded = 2;
        DwmSetWindowAttribute(handle, 33, ref rounded, sizeof(int));
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}

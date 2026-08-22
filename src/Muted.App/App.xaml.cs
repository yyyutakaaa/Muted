using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Muted.App.Services;
using Muted.App.ViewModels;
using Muted.Audio.Windows.Devices;
using Muted.Audio.Windows.Engine;
using Muted.Core.Audio;

namespace Muted.App;

public partial class App : System.Windows.Application
{
    private SingleInstanceService? _singleInstance;
    private WasapiDeviceCatalog? _deviceCatalog;
    private TrayService? _tray;
    private GlobalHotkeyService? _hotkeys;
    private ThemeService? _theme;
    private MainViewModel? _viewModel;
    private MainWindow? _window;
    private CompactWindow? _compactWindow;
    private FileLog? _log;
    private UpdateCoordinator? _updateCoordinator;
    private int _exitRequested;
    private bool _showedTrayHint;

    protected override async void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _log = new FileLog();
        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.IsPrimary)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            if (SynchronizationContext.Current is null)
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher));
            }

            var settingsStore = new JsonSettingsStore();
            var settings = await settingsStore.LoadAsync();

            _theme = new ThemeService(Resources);
            _theme.Apply(settings.Theme, settings.UseSystemAccentColor);

            _updateCoordinator = new UpdateCoordinator(new UpdateService(_log));
            _updateCoordinator.InstallStarted += (_, _) => Dispatcher.BeginInvoke(RequestExit);
            _deviceCatalog = new WasapiDeviceCatalog();
            var engine = new RealtimeAudioEngine();
            _viewModel = new MainViewModel(
                engine,
                _deviceCatalog,
                settingsStore,
                new StartupService(),
                _updateCoordinator,
                _log);
            await _viewModel.InitializeAsync(settings);

            _window = new MainWindow(_viewModel);
            MainWindow = _window;
            _window.Closing += OnWindowClosing;
            _window.StateChanged += OnWindowStateChanged;
            _window.IsVisibleChanged += OnWindowVisibilityChanged;

            _hotkeys = new GlobalHotkeyService(_log);
            _hotkeys.Triggered += OnHotkeyTriggered;
            _viewModel.HotkeysChanged += (_, _) => RefreshHotkeys();
            _viewModel.ShowWindowRequested += (_, _) => Dispatcher.BeginInvoke(ShowMainWindow);
            RefreshHotkeys();

            _tray = new TrayService();
            _tray.OpenRequested += (_, _) => Dispatcher.Invoke(ShowMainWindow);
            _tray.ToggleRequested += (_, _) => Dispatcher.Invoke(() => Execute(_viewModel.ToggleCommand));
            _tray.MuteRequested += (_, _) => Dispatcher.Invoke(() => Execute(_viewModel.ToggleMuteCommand));
            _tray.SuppressionToggleRequested += (_, _) =>
                Dispatcher.Invoke(() => Execute(_viewModel.ToggleSuppressionCommand));
            _tray.MonitorToggleRequested += (_, _) =>
                Dispatcher.Invoke(() => _viewModel.MonitorEnabled = !_viewModel.MonitorEnabled);
            _tray.CompactToggleRequested += (_, _) =>
                Dispatcher.Invoke(() => _viewModel.CompactMode = !_viewModel.CompactMode);
            _tray.ProfileRequested += (_, args) => Dispatcher.Invoke(() =>
                _ = _viewModel.ApplyProfileAsync(args.ProfileId));
            _tray.DiagnosticsRequested += (_, _) => Dispatcher.Invoke(() =>
            {
                ShowMainWindow();
                _window.ShowDiagnostics();
            });
            _tray.ExitRequested += (_, _) => Dispatcher.Invoke(RequestExit);
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            UpdateTrayState();

            var commandLineMinimized = eventArgs.Args.Any(argument =>
                string.Equals(argument, "--minimized", StringComparison.OrdinalIgnoreCase));
            var startMinimized = commandLineMinimized || settings.StartMinimized;
            if (_viewModel.CompactMode)
            {
                ApplyCompactMode(showWindow: !startMinimized);
            }
            else if (!startMinimized)
            {
                _window.Show();
            }
            else if (!settings.MinimizeToTray)
            {
                _window.Show();
                _window.WindowState = WindowState.Minimized;
            }

            _singleInstance.ActivationRequested += (_, _) => Dispatcher.BeginInvoke(ShowMainWindow);

            _ = CheckForUpdateAsync();
        }
        catch (Exception exception)
        {
            _log.Write(exception, "Start application");
            System.Windows.MessageBox.Show(
                $"Muted could not start.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Muted",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            RequestExit();
        }
    }

    private static void Execute(System.Windows.Input.ICommand command)
    {
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private async Task CheckForUpdateAsync()
    {
        if (_updateCoordinator is null || _viewModel is null)
        {
            return;
        }

        var result = await _updateCoordinator.CheckAndPromptAsync(
            showNoUpdateMessage: false,
            _window,
            _viewModel.SkippedUpdateVersion,
            _viewModel.UpdateChannel);
        _viewModel.ReportUpdateStatus(result);
    }

    private void RefreshHotkeys()
    {
        if (_hotkeys is null || _viewModel is null)
        {
            return;
        }

        _hotkeys.Update(_viewModel.GetHotkeyBindings());
        _viewModel.HotkeysActive = _hotkeys.IsActive;
    }

    private void OnHotkeyTriggered(object? sender, HotkeyEventArgs eventArgs)
    {
        // The hook runs on the UI thread, but a dispatch keeps the callback short.
        Dispatcher.BeginInvoke(() => _viewModel?.HandleHotkey(eventArgs.Action, eventArgs.IsPressed));
    }

    private void OnWindowClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (Volatile.Read(ref _exitRequested) != 0 || _window is null || _viewModel is null)
        {
            return;
        }

        eventArgs.Cancel = true;
        if (_viewModel.MinimizeToTray)
        {
            _window.Hide();
            if (!_showedTrayHint)
            {
                _showedTrayHint = true;
                _tray?.ShowBalloon("Muted is still running", "Open Muted from the system tray icon.");
            }
        }
        else
        {
            RequestExit();
        }
    }

    private void OnWindowStateChanged(object? sender, EventArgs eventArgs)
    {
        if (_window?.WindowState == WindowState.Minimized)
        {
            if (_viewModel?.MinimizeToTray == true)
            {
                _window.Hide();
            }
            else
            {
                _viewModel?.SetUiVisible(false);
            }
        }
        else if (_window?.IsVisible == true)
        {
            _viewModel?.SetUiVisible(true);
        }
    }

    private void OnWindowVisibilityChanged(object sender, DependencyPropertyChangedEventArgs eventArgs) =>
        UpdateUiVisibility();

    private void UpdateUiVisibility() =>
        _viewModel?.SetUiVisible(_window?.IsVisible == true || _compactWindow?.IsVisible == true);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        switch (eventArgs.PropertyName)
        {
            case nameof(MainViewModel.EngineState):
            case nameof(MainViewModel.IsMuted):
            case nameof(MainViewModel.IsEffectivelyMuted):
            case nameof(MainViewModel.SuppressionEnabled):
            case nameof(MainViewModel.MonitorEnabled):
            case nameof(MainViewModel.SelectedProfile):
            case nameof(MainViewModel.ActiveProfileId):
            case nameof(MainViewModel.Profiles):
                UpdateTrayState();
                break;
            case nameof(MainViewModel.Theme):
            case nameof(MainViewModel.UseSystemAccentColor):
                ApplyTheme();
                break;
            case nameof(MainViewModel.CompactMode):
                UpdateTrayState();
                ApplyCompactMode(showWindow: true);
                break;
        }
    }

    private void ApplyTheme()
    {
        if (_theme is null || _viewModel is null)
        {
            return;
        }

        _theme.Apply(_viewModel.Theme, _viewModel.UseSystemAccentColor);
        _window?.ApplyWindowAppearance();
        _compactWindow?.ApplyWindowAppearance();
    }

    /// <summary>Swaps between the full window and the small always-on-top panel.</summary>
    private void ApplyCompactMode(bool showWindow)
    {
        if (_viewModel is null || _window is null)
        {
            return;
        }

        if (_viewModel.CompactMode)
        {
            _window.Hide();
            if (_compactWindow is null)
            {
                _compactWindow = new CompactWindow(_viewModel);
                _compactWindow.HideRequested += (_, _) => _compactWindow?.Hide();
                _compactWindow.IsVisibleChanged += (_, _) => UpdateUiVisibility();
                _compactWindow.Closing += OnCompactWindowClosing;
            }

            if (showWindow)
            {
                _compactWindow.Show();
                _compactWindow.Activate();
            }
        }
        else
        {
            _compactWindow?.Hide();
            if (showWindow)
            {
                ShowMainWindow();
            }
        }

        UpdateUiVisibility();
    }

    private void OnCompactWindowClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (Volatile.Read(ref _exitRequested) != 0)
        {
            return;
        }

        eventArgs.Cancel = true;
        _compactWindow?.Hide();
    }

    private void UpdateTrayState()
    {
        if (_tray is null || _viewModel is null)
        {
            return;
        }

        _tray.UpdateState(new TrayState(
            _viewModel.EngineState,
            _viewModel.IsEffectivelyMuted,
            _viewModel.SuppressionEnabled,
            _viewModel.MonitorEnabled,
            _viewModel.CompactMode,
            _viewModel.ActiveProfileName,
            _viewModel.ActiveProfileId,
            _viewModel.Profiles.ToArray()));
    }

    private void ShowMainWindow()
    {
        if (_viewModel?.CompactMode == true && _compactWindow is not null)
        {
            _compactWindow.Show();
            _compactWindow.Activate();
            return;
        }

        if (_window is null)
        {
            return;
        }

        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
        _window.Topmost = true;
        _window.Topmost = false;
        _window.Focus();
    }

    private async void RequestExit()
    {
        if (Interlocked.Exchange(ref _exitRequested, 1) != 0)
        {
            return;
        }

        try
        {
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                await _viewModel.DisposeAsync();
            }
        }
        catch (Exception exception)
        {
            _log?.Write(exception, "Exit application");
        }
        finally
        {
            if (_hotkeys is not null)
            {
                _hotkeys.Triggered -= OnHotkeyTriggered;
                _hotkeys.Dispose();
                _hotkeys = null;
            }

            _compactWindow?.Close();
            _compactWindow = null;
            _tray?.Dispose();
            _tray = null;
            _deviceCatalog?.Dispose();
            _deviceCatalog = null;
            _singleInstance?.Dispose();
            _singleInstance = null;
            Shutdown();
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs eventArgs)
    {
        _log?.Write(eventArgs.Exception, "Unhandled UI error");
        eventArgs.Handled = true;
        System.Windows.MessageBox.Show(
            "Muted encountered an unexpected error and will close.",
            "Muted",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        RequestExit();
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs eventArgs)
    {
        _log?.Write(eventArgs.Exception, "Unhandled background error");
        eventArgs.SetObserved();
    }
}

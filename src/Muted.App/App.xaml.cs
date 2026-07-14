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
    private MainViewModel? _viewModel;
    private MainWindow? _window;
    private FileLog? _log;
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
            _deviceCatalog = new WasapiDeviceCatalog();
            var engine = new RealtimeAudioEngine();
            _viewModel = new MainViewModel(
                engine,
                _deviceCatalog,
                settingsStore,
                new StartupService(),
                _log);
            await _viewModel.InitializeAsync(settings);

            _window = new MainWindow(_viewModel);
            MainWindow = _window;
            _window.Closing += OnWindowClosing;
            _window.StateChanged += OnWindowStateChanged;
            _window.IsVisibleChanged += OnWindowVisibilityChanged;

            _tray = new TrayService();
            _tray.OpenRequested += (_, _) => Dispatcher.Invoke(ShowMainWindow);
            _tray.ToggleRequested += (_, _) => Dispatcher.Invoke(() =>
            {
                if (_viewModel.ToggleCommand.CanExecute(null))
                {
                    _viewModel.ToggleCommand.Execute(null);
                }
            });
            _tray.ExitRequested += (_, _) => Dispatcher.Invoke(RequestExit);
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _tray.UpdateState(_viewModel.EngineState);

            var commandLineMinimized = eventArgs.Args.Any(argument =>
                string.Equals(argument, "--minimized", StringComparison.OrdinalIgnoreCase));
            var startMinimized = commandLineMinimized || settings.StartMinimized;
            if (!startMinimized)
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

    private async Task CheckForUpdateAsync()
    {
        if (_log is null)
        {
            return;
        }

        var updateService = new UpdateService(_log);
        if (await updateService.DownloadAndStartLatestAsync())
        {
            await Dispatcher.InvokeAsync(RequestExit);
        }
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
        _viewModel?.SetUiVisible(_window?.IsVisible == true);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(MainViewModel.EngineState))
        {
            _tray?.UpdateState(_viewModel?.EngineState ?? AudioEngineState.Stopped);
        }
    }

    private void ShowMainWindow()
    {
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

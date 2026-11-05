using Muted.App.Infrastructure;
using Muted.Core.Settings;

namespace Muted.App.ViewModels;

/// <summary>App preferences: startup, tray, theme, window mode and device behaviour.</summary>
internal sealed partial class MainViewModel
{
    private bool _startWithWindows;
    private bool _minimizeToTray = true;
    private bool _startMinimized;
    private bool _autoRecoverDevices = true;
    private bool _startMuted;
    private bool _followDefaultInput;
    private AppTheme _theme = AppTheme.System;
    private bool _useSystemAccentColor;
    private bool _compactMode;
    private bool _advancedMode;

    public RelayCommand ToggleCompactCommand { get; }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (_startWithWindows == value)
            {
                return;
            }

            try
            {
                if (_initialized)
                {
                    _startupService.SetEnabled(value, StartMinimized);
                }
            }
            catch (Exception exception)
            {
                _log.Write(exception, "Update startup setting");
                ErrorMessage = "Startup setting could not be updated.";
                return;
            }

            SetProperty(ref _startWithWindows, value);
            QueueSave();
        }
    }

    public bool StartMinimized
    {
        get => _startMinimized;
        set
        {
            if (_startMinimized == value)
            {
                return;
            }

            var previous = _startMinimized;
            SetProperty(ref _startMinimized, value);
            if (_initialized)
            {
                try
                {
                    if (StartWithWindows)
                    {
                        _startupService.SetEnabled(enabled: true, startMinimized: value);
                    }
                }
                catch (Exception exception)
                {
                    _startMinimized = previous;
                    OnPropertyChanged();
                    _log.Write(exception, "Update startup options");
                    ErrorMessage = "Startup setting could not be updated.";
                    return;
                }

                QueueSave();
            }
        }
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set
        {
            if (SetProperty(ref _minimizeToTray, value) && _initialized)
            {
                QueueSave();
            }
        }
    }

    public bool AutoRecoverDevices
    {
        get => _autoRecoverDevices;
        set
        {
            if (SetProperty(ref _autoRecoverDevices, value) && _initialized)
            {
                if (!value)
                {
                    CancelRecovery();
                }

                QueueSave();
            }
        }
    }

    public bool StartMuted
    {
        get => _startMuted;
        set
        {
            if (SetProperty(ref _startMuted, value) && _initialized)
            {
                QueueSave();
            }
        }
    }

    public bool FollowDefaultInput
    {
        get => _followDefaultInput;
        set
        {
            if (!SetProperty(ref _followDefaultInput, value) || !_initialized)
            {
                return;
            }

            OnPropertyChanged(nameof(CanSelectInput));
            if (value && IsStopped)
            {
                RefreshDevicesCore(SelectedInput?.Id, SelectedOutput?.Id);
            }

            QueueSave();
        }
    }

    public AppTheme Theme
    {
        get => _theme;
        set
        {
            if (SetProperty(ref _theme, value) && _initialized)
            {
                QueueSave();
            }
        }
    }

    public bool UseSystemAccentColor
    {
        get => _useSystemAccentColor;
        set
        {
            if (SetProperty(ref _useSystemAccentColor, value) && _initialized)
            {
                QueueSave();
            }
        }
    }

    public bool CompactMode
    {
        get => _compactMode;
        set
        {
            if (SetProperty(ref _compactMode, value) && _initialized)
            {
                QueueSave();
            }
        }
    }

    /// <summary>Shows the full chain and the live numbers, for people who want them.</summary>
    public bool AdvancedMode
    {
        get => _advancedMode;
        set
        {
            if (SetProperty(ref _advancedMode, value) && _initialized)
            {
                QueueSave();
            }
        }
    }
}

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using Muted.App.Infrastructure;
using Muted.Core.Settings;

namespace Muted.App.ViewModels;

/// <summary>Automatic profiles: switching to a profile while a chosen program runs.</summary>
internal sealed partial class MainViewModel
{
    private DispatcherTimer _autoProfileTimer = null!;
    private bool _autoProfileEnabled;
    private string? _autoProfileActiveRule;
    private string? _profileBeforeAutoSwitch;
    private bool _autoProfileScanning;
    private string _autoProfileStatus = "Add a program and the profile it should switch to.";
    private string _newRuleProcessName = string.Empty;
    private AudioProfile? _newRuleProfile;

    public ObservableCollection<AutoProfileRuleViewModel> AutoProfileRules { get; } = [];

    public ObservableCollection<string> RunningPrograms { get; } = [];

    public RelayCommand AddAutoProfileRuleCommand { get; private set; } = null!;

    public RelayCommand<AutoProfileRuleViewModel> RemoveAutoProfileRuleCommand { get; private set; } = null!;

    public RelayCommand RefreshRunningProgramsCommand { get; private set; } = null!;

    public bool AutoProfileEnabled
    {
        get => _autoProfileEnabled;
        set
        {
            if (!SetProperty(ref _autoProfileEnabled, value))
            {
                return;
            }

            if (value)
            {
                _autoProfileTimer.Start();
                AutoProfileStatus = "Watching for the programs below.";
            }
            else
            {
                _autoProfileTimer.Stop();
                _autoProfileActiveRule = null;
                _profileBeforeAutoSwitch = null;
                AutoProfileStatus = "Off.";
            }

            QueueSave();
        }
    }

    public string AutoProfileStatus
    {
        get => _autoProfileStatus;
        private set => SetProperty(ref _autoProfileStatus, value);
    }

    public string NewRuleProcessName
    {
        get => _newRuleProcessName;
        set
        {
            if (SetProperty(ref _newRuleProcessName, value))
            {
                AddAutoProfileRuleCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AudioProfile? NewRuleProfile
    {
        get => _newRuleProfile;
        set
        {
            if (SetProperty(ref _newRuleProfile, value))
            {
                AddAutoProfileRuleCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private void AddAutoProfileRule()
    {
        var name = AutoProfileRule.NormalizeProcessName(NewRuleProcessName);
        if (name.Length == 0 || NewRuleProfile is null)
        {
            return;
        }

        var existing = AutoProfileRules.FirstOrDefault(rule => rule.ProcessName == name);
        if (existing is not null)
        {
            AutoProfileRules.Remove(existing);
        }

        AutoProfileRules.Add(new AutoProfileRuleViewModel(name, NewRuleProfile.Id, NewRuleProfile.Name));
        NewRuleProcessName = string.Empty;
        AddAutoProfileRuleCommand.RaiseCanExecuteChanged();
        AutoProfileStatus = $"Muted switches to {NewRuleProfile.Name} while {name} runs.";
        QueueSave();
        if (AutoProfileEnabled)
        {
            _ = ScanProgramsAsync();
        }
    }

    private void RemoveAutoProfileRule(AutoProfileRuleViewModel? rule)
    {
        if (rule is null)
        {
            return;
        }

        AutoProfileRules.Remove(rule);
        if (_autoProfileActiveRule == rule.ProcessName)
        {
            _autoProfileActiveRule = null;
        }

        AddAutoProfileRuleCommand.RaiseCanExecuteChanged();
        QueueSave();
    }

    /// <summary>Programs with a window, which is what people recognise as "running".</summary>
    private async void RefreshRunningPrograms()
    {
        try
        {
            var names = await Task.Run(() =>
            {
                var result = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var process in Process.GetProcesses())
                {
                    using (process)
                    {
                        try
                        {
                            if (process.MainWindowHandle != IntPtr.Zero)
                            {
                                result.Add(AutoProfileRule.NormalizeProcessName(process.ProcessName));
                            }
                        }
                        catch (Exception)
                        {
                            // Some system processes refuse to be inspected.
                        }
                    }
                }

                return result.Where(name => name.Length > 0 && name != "muted").ToArray();
            });
            RunningPrograms.Clear();
            foreach (var name in names)
            {
                RunningPrograms.Add(name);
            }
        }
        catch (Exception exception)
        {
            _log.Write(exception, "List running programs");
        }
    }

    private async void OnAutoProfileTick(object? sender, EventArgs eventArgs) => await ScanProgramsAsync();

    private async Task ScanProgramsAsync()
    {
        if (_autoProfileScanning || !AutoProfileEnabled || AutoProfileRules.Count == 0)
        {
            return;
        }

        _autoProfileScanning = true;
        try
        {
            var running = await Task.Run(() =>
            {
                var processes = Process.GetProcesses();
                try
                {
                    return processes.Select(process => process.ProcessName).ToArray();
                }
                finally
                {
                    foreach (var process in processes)
                    {
                        process.Dispose();
                    }
                }
            });

            var rules = AutoProfileRules.Select(rule => rule.ToRule()).ToArray();
            var runningNames = running.Select(AutoProfileRule.NormalizeProcessName).ToHashSet(StringComparer.Ordinal);
            var match = rules.FirstOrDefault(rule => runningNames.Contains(rule.ProcessName));

            // Busy moments are skipped, not lost: the next scan tries again.
            if (IsBusy || ClipBusy || IsRecording || IsCalibrating || _isApplyingProfile)
            {
                return;
            }

            if (match is not null && match.ProcessName != _autoProfileActiveRule)
            {
                if (_autoProfileActiveRule is null)
                {
                    _profileBeforeAutoSwitch = ActiveProfileId;
                }

                _autoProfileActiveRule = match.ProcessName;
                if (!string.Equals(ActiveProfileId, match.ProfileId, StringComparison.OrdinalIgnoreCase))
                {
                    await ApplyProfileAsync(match.ProfileId);
                }

                AutoProfileStatus = $"{match.ProcessName} is running, so {ActiveProfileName} is on.";
            }
            else if (match is null && _autoProfileActiveRule is not null)
            {
                var closed = _autoProfileActiveRule;
                _autoProfileActiveRule = null;
                var previous = _profileBeforeAutoSwitch;
                _profileBeforeAutoSwitch = null;
                if (previous is not null && Profiles.Any(profile => profile.Id == previous) &&
                    !string.Equals(ActiveProfileId, previous, StringComparison.OrdinalIgnoreCase))
                {
                    await ApplyProfileAsync(previous);
                }

                AutoProfileStatus = $"{closed} closed, so {ActiveProfileName} is back.";
            }
        }
        catch (Exception exception)
        {
            _log.Write(exception, "Automatic profile");
        }
        finally
        {
            _autoProfileScanning = false;
        }
    }

    private void InitializeAutoProfiles()
    {
        AddAutoProfileRuleCommand = new RelayCommand(
            AddAutoProfileRule,
            () => AutoProfileRule.NormalizeProcessName(NewRuleProcessName).Length > 0 &&
                NewRuleProfile is not null &&
                AutoProfileRules.Count < AutoProfileRule.MaximumRules);
        RemoveAutoProfileRuleCommand = new RelayCommand<AutoProfileRuleViewModel>(RemoveAutoProfileRule);
        RefreshRunningProgramsCommand = new RelayCommand(RefreshRunningPrograms);

        _autoProfileTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _autoProfileTimer.Tick += OnAutoProfileTick;
        Profiles.CollectionChanged += (_, _) =>
        {
            SyncAutoProfileRules();
            if (NewRuleProfile is null || !Profiles.Contains(NewRuleProfile))
            {
                NewRuleProfile = Profiles.FirstOrDefault();
            }
        };
    }

    private void LoadAutoProfileSettings(AppSettings settings)
    {
        _autoProfileEnabled = settings.AutoProfileEnabled;

        AutoProfileRules.Clear();
        foreach (var rule in settings.AutoProfileRules)
        {
            var profile = settings.Profiles.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, rule.ProfileId, StringComparison.OrdinalIgnoreCase));
            if (profile is not null)
            {
                AutoProfileRules.Add(new AutoProfileRuleViewModel(rule.ProcessName, profile.Id, profile.Name));
            }
        }

        OnPropertyChanged(nameof(AutoProfileEnabled));
    }

    private void StartAutoProfiles()
    {
        NewRuleProfile = Profiles.FirstOrDefault();
        if (AutoProfileEnabled)
        {
            AutoProfileStatus = "Watching for the programs below.";
            _autoProfileTimer.Start();
        }
    }

    private AppSettings WithAutoProfileSettings(AppSettings settings) => settings with
    {
        AutoProfileEnabled = AutoProfileEnabled,
        AutoProfileRules = AutoProfileRules.Select(rule => rule.ToRule()).ToArray()
    };

    /// <summary>Keeps rules pointing at profiles that still exist, with their current names.</summary>
    private void SyncAutoProfileRules()
    {
        for (var index = AutoProfileRules.Count - 1; index >= 0; index--)
        {
            var rule = AutoProfileRules[index];
            var profile = Profiles.FirstOrDefault(candidate => candidate.Id == rule.ProfileId);
            if (profile is null)
            {
                AutoProfileRules.RemoveAt(index);
            }
            else if (profile.Name != rule.ProfileName)
            {
                AutoProfileRules[index] = new AutoProfileRuleViewModel(rule.ProcessName, profile.Id, profile.Name);
            }
        }
    }

    private void StopAutoProfiles()
    {
        _autoProfileTimer.Stop();
        _autoProfileTimer.Tick -= OnAutoProfileTick;
    }
}

using System.Collections.ObjectModel;
using System.IO;
using Muted.App.Infrastructure;
using Muted.Audio.Windows.Devices;
using Muted.Core.Dsp;
using Muted.Core.Settings;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Muted.App.ViewModels;

/// <summary>Saved profiles, voice presets, and importing and exporting profiles.</summary>
internal sealed partial class MainViewModel
{
    private AudioProfile? _selectedProfile;
    private string? _activeProfileId;
    private string _newProfileName = string.Empty;
    private bool _isApplyingProfile;

    public ObservableCollection<AudioProfile> Profiles { get; } = [];

    public AsyncRelayCommand ApplyProfileCommand { get; }

    public RelayCommand SaveProfileCommand { get; }

    public RelayCommand UpdateProfileCommand { get; }

    public RelayCommand DeleteProfileCommand { get; }

    public AudioProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                ApplyProfileCommand.RaiseCanExecuteChanged();
                UpdateProfileCommand.RaiseCanExecuteChanged();
                DeleteProfileCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? ActiveProfileId
    {
        get => _activeProfileId;
        private set
        {
            if (SetProperty(ref _activeProfileId, value))
            {
                OnPropertyChanged(nameof(ActiveProfileName));
                DeleteProfileCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ActiveProfileName => Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, ActiveProfileId, StringComparison.OrdinalIgnoreCase))?.Name
        ?? "No profile";

    public string NewProfileName
    {
        get => _newProfileName;
        set
        {
            if (SetProperty(ref _newProfileName, value))
            {
                SaveProfileCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand NaturalPresetCommand { get; private set; } = null!;

    public AsyncRelayCommand GamingPresetCommand { get; private set; } = null!;

    public AsyncRelayCommand PodcastPresetCommand { get; private set; } = null!;

    public AsyncRelayCommand MeetingPresetCommand { get; private set; } = null!;

    public AsyncRelayCommand WarmRadioPresetCommand { get; private set; } = null!;

    public RelayCommand ExportProfilesCommand { get; private set; } = null!;

    public RelayCommand ExportSelectedProfileCommand { get; private set; } = null!;

    public RelayCommand ImportProfilesCommand { get; private set; } = null!;

    private async Task ApplySelectedProfileAsync()
    {
        if (SelectedProfile is not null)
        {
            await ApplyProfileAsync(SelectedProfile.Id);
        }
    }

    public async Task ApplyProfileAsync(string profileId)
    {
        var profile = Profiles.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, profileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null || IsBusy)
        {
            return;
        }

        var wasRunning = IsRunning;
        if (wasRunning)
        {
            if (!await StopAsync())
            {
                return;
            }
        }

        _isApplyingProfile = true;
        try
        {
            SelectedProfile = profile;
            if (!string.IsNullOrWhiteSpace(profile.InputDeviceId))
            {
                SelectedInput = InputDevices.FirstOrDefault(device => device.Id == profile.InputDeviceId)
                    ?? SelectedInput;
            }

            if (!string.IsNullOrWhiteSpace(profile.OutputDeviceId))
            {
                SelectedOutput = OutputDevices.FirstOrDefault(device => device.Id == profile.OutputDeviceId)
                    ?? SelectedOutput;
            }

            SuppressionEnabled = profile.SuppressionEnabled;
            WetMix = profile.WetMix;
            VoiceGateEnabled = profile.VoiceGateEnabled;
            VoiceSensitivity = profile.VoiceThreshold;
            VoiceHoldMilliseconds = profile.VoiceHoldMilliseconds;
            InputGain = profile.InputGain;
            OutputGain = profile.OutputGain;
            HighPassEnabled = profile.HighPassEnabled;
            HighPassFrequency = profile.HighPassFrequency;
            LimiterEnabled = profile.LimiterEnabled;
            AutoGainEnabled = profile.AutoGainEnabled;
            AutoGainTargetDb = profile.AutoGainTargetDb;
            EchoStrength = profile.EchoStrength;
            SelectedEchoReference = OutputDevices.FirstOrDefault(device => device.Id == profile.EchoReferenceDeviceId)
                ?? SelectedEchoReference;
            EchoCancellationEnabled = profile.EchoCancellationEnabled;
            ApplyVoiceProfile(profile);
            ApplySuppression();
            ActiveProfileId = profile.Id;
        }
        finally
        {
            _isApplyingProfile = false;
        }

        if (wasRunning)
        {
            await StartAsync();
        }

        await TrySaveAsync();
    }

    private AudioProfile BuildProfile(string id, string name) => WithVoiceSettings(new AudioProfile
    {
        Id = id,
        Name = name,
        InputDeviceId = SelectedInput?.Id,
        OutputDeviceId = SelectedOutput?.Id,
        SuppressionEnabled = SuppressionEnabled,
        WetMix = (float)WetMix,
        VoiceGateEnabled = VoiceGateEnabled,
        VoiceThreshold = (float)VoiceSensitivity,
        VoiceHoldMilliseconds = (int)VoiceHoldMilliseconds,
        InputGain = (float)InputGain,
        OutputGain = (float)OutputGain,
        HighPassEnabled = HighPassEnabled,
        HighPassFrequency = (float)HighPassFrequency,
        LimiterEnabled = LimiterEnabled,
        AutoGainEnabled = AutoGainEnabled,
        AutoGainTargetDb = (float)AutoGainTargetDb,
        EchoCancellationEnabled = EchoCancellationEnabled,
        EchoReferenceDeviceId = SelectedEchoReference?.Id,
        EchoStrength = (float)EchoStrength
    }).Normalize();

    private void SaveCurrentProfile()
    {
        var name = NewProfileName.Trim();
        if (name.Length == 0 || Profiles.Count >= AppSettings.MaximumProfiles)
        {
            return;
        }

        if (name.Length > AudioProfile.MaximumNameLength)
        {
            name = name[..AudioProfile.MaximumNameLength];
        }

        var profile = BuildProfile(Guid.NewGuid().ToString("N"), name);
        Profiles.Add(profile);
        SelectedProfile = profile;
        ActiveProfileId = profile.Id;
        NewProfileName = string.Empty;
        OnPropertyChanged(nameof(Profiles));
        DeleteProfileCommand.RaiseCanExecuteChanged();
        SaveProfileCommand.RaiseCanExecuteChanged();
        QueueSave();
    }

    /// <summary>Writes the settings that are live right now back into the selected profile.</summary>
    private void UpdateSelectedProfile()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var index = Profiles.IndexOf(SelectedProfile);
        if (index < 0)
        {
            return;
        }

        var updated = BuildProfile(SelectedProfile.Id, SelectedProfile.Name);
        Profiles[index] = updated;
        SelectedProfile = updated;
        ActiveProfileId = updated.Id;
        OnPropertyChanged(nameof(Profiles));
        OnPropertyChanged(nameof(ActiveProfileName));
        QueueSave();
    }

    private void DeleteSelectedProfile()
    {
        if (SelectedProfile is null || Profiles.Count <= 1 ||
            string.Equals(SelectedProfile.Id, ActiveProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Profiles.Remove(SelectedProfile);
        SelectedProfile = Profiles[0];
        OnPropertyChanged(nameof(Profiles));
        DeleteProfileCommand.RaiseCanExecuteChanged();
        SaveProfileCommand.RaiseCanExecuteChanged();
        QueueSave();
    }

    private void InitializePresetCommands()
    {
        NaturalPresetCommand = new(() => UsePresetAsync("Natural"), () => !IsBusy && !ClipBusy);
        GamingPresetCommand = new(() => UsePresetAsync("Gaming"), () => !IsBusy && !ClipBusy);
        PodcastPresetCommand = new(() => UsePresetAsync("Podcast"), () => !IsBusy && !ClipBusy);
        MeetingPresetCommand = new(() => UsePresetAsync("Meeting"), () => !IsBusy && !ClipBusy);
        WarmRadioPresetCommand = new(() => UsePresetAsync("Warm Radio"), () => !IsBusy && !ClipBusy);
        ExportProfilesCommand = new(() => ExportProfiles(false));
        ExportSelectedProfileCommand = new(() => ExportProfiles(true));
        ImportProfilesCommand = new(ImportProfiles);
    }

    private async Task UsePresetAsync(string name)
    {
        if (IsBusy || ClipBusy) return;
        try
        {
            var id = "preset-" + name.ToLowerInvariant().Replace(' ', '-');
            var warmRadio = name == "Warm Radio";
            var existing = Profiles.FirstOrDefault(p => p.Id == id);
            if (existing is null && Profiles.Count >= AppSettings.MaximumProfiles)
                throw new InvalidOperationException("Delete a profile before adding another preset (maximum 20).");
            var reference = SelectedEchoReference is { } selected && selected.Id != SelectedOutput?.Id &&
                !WasapiDeviceCatalog.IsLikelyVirtualCable(selected.Name) ? selected : OutputDevices.FirstOrDefault(d =>
                !WasapiDeviceCatalog.IsLikelyVirtualCable(d.Name) && d.Id != SelectedOutput?.Id);
            var profile = new AudioProfile
            {
                Id = id, Name = name,
                VoiceGateEnabled = name != "Natural",
                VoiceThreshold = name == "Gaming" ? .68f : .42f,
                VoiceHoldMilliseconds = name == "Gaming" ? 160 : 400,
                HighPassEnabled = true,
                // The warm sound lives in the low end, so only the rumble below it goes.
                HighPassFrequency = warmRadio ? 60f : 80f,
                AutoGainEnabled = name is "Podcast" or "Meeting" or "Warm Radio",
                AutoGainTargetDb = name is "Podcast" or "Warm Radio" ? -16 : -18,
                EchoCancellationEnabled = name == "Meeting" && reference is not null,
                EchoReferenceDeviceId = reference?.Id,
                VoiceCharacterEnabled = warmRadio,
                Warmth = warmRadio ? .75f : .6f,
                Proximity = warmRadio ? .7f : .5f,
                Smoothness = warmRadio ? .7f : .6f,
                Compression = warmRadio ? .6f : .5f,
                Saturation = warmRadio ? .35f : .3f,

                // A preset changes how you sound, never whether your friends' ears are protected.
                ShoutGuardEnabled = ShoutGuardEnabled,
                ShoutThresholdDb = (float)ShoutThresholdDb,
                ShoutGuardMode = ShoutGuardDucks ? ShoutGuardMode.Duck : ShoutGuardMode.Silence
            };
            if (existing is not null) Profiles[Profiles.IndexOf(existing)] = profile;
            else Profiles.Add(profile);
            OnPropertyChanged(nameof(Profiles));
            SaveProfileCommand.RaiseCanExecuteChanged();
            await ApplyProfileAsync(profile.Id);
            FeatureStatus = name == "Meeting" ? "Meeting preset applied. Check the echo reference under Audio matches your speakers." : name + " preset applied.";
        }
        catch (Exception exception) { FeatureStatus = exception.Message; }
    }

    private void ExportProfiles(bool selectedOnly)
    {
        try
        {
            if (selectedOnly && SelectedProfile is null) { FeatureStatus = "Select a profile first."; return; }
            var dialog = new SaveFileDialog { Filter = "Muted profiles (*.json)|*.json", FileName = "Muted-profiles.json" };
            if (dialog.ShowDialog() != true) return;
            File.WriteAllText(dialog.FileName, ProfileExchange.Export(selectedOnly ? new[] { SelectedProfile! } : Profiles));
            FeatureStatus = "Profiles exported without device identifiers.";
        }
        catch (Exception exception) { FeatureStatus = "Export failed: " + exception.Message; }
    }

    private void ImportProfiles()
    {
        try
        {
            var dialog = new OpenFileDialog { Filter = "Muted profiles (*.json)|*.json" };
            if (dialog.ShowDialog() != true) return;
            if (new FileInfo(dialog.FileName).Length > 256_000) throw new InvalidDataException("Profile file is too large.");
            var imported = ProfileExchange.Import(File.ReadAllText(dialog.FileName));
            if (Profiles.Count + imported.Length > AppSettings.MaximumProfiles)
                throw new InvalidDataException("Not enough space. Muted supports 20 profiles; delete some before importing.");
            foreach (var profile in imported)
            {
                var name = profile.Name;
                var suffix = 2;
                while (Profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    name = profile.Name[..Math.Min(profile.Name.Length, 32)] + $" ({suffix++})";
                Profiles.Add(profile with { Name = name });
            }
            SelectedProfile = Profiles.Last();
            OnPropertyChanged(nameof(Profiles));
            SaveProfileCommand.RaiseCanExecuteChanged();
            QueueSave();
            FeatureStatus = $"Imported {imported.Length} profile(s). Select Apply to use one.";
        }
        catch (Exception exception) { FeatureStatus = "Import failed: " + exception.Message; }
    }
}

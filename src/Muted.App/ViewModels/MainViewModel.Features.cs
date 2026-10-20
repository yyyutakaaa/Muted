using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Muted.App.Infrastructure;
using Muted.App.Services;
using Muted.Audio.Windows.Devices;
using Muted.Core.Audio;
using Muted.Core.Dsp;
using Muted.Core.Settings;

namespace Muted.App.ViewModels;

internal sealed partial class MainViewModel
{
    private DispatcherTimer _featureTimer = null!;
    private bool _speakingTrayEnabled = true, _muteReminderEnabled = true, _healthWarningsEnabled = true;
    private bool _pushToTalkSoundsEnabled, _traySpeaking, _previewMuted, _clipBusy, _lastTalkOpen, _isRecording, _isClipPlaying, _suppressTalkCue;
    private string _featureStatus = "Ready. Recordings stay in memory until you explicitly save a diagnosis clip.";
    private string _sessionText = "Start audio to collect session statistics.";
    private string _healthWarning = string.Empty;
    private readonly CancellationTokenSource _featureLifetime = new();
    private float[]? _lastClip;
    private AudioClipBuffer? _recordingBuffer;
    private RecordedClipPlayer? _clipPlayer;
    private long _recordingStarted;
    private double _clipPosition, _clipDuration;
    private bool _diagnosisClip;
    private readonly Stopwatch _feedbackClock = Stopwatch.StartNew();
    private readonly AudioFeedbackTracker _feedback = new();
    private bool _cuePlaying;

    public event EventHandler<string>? NotificationRequested;
    public AsyncRelayCommand NaturalPresetCommand { get; private set; } = null!;
    public AsyncRelayCommand GamingPresetCommand { get; private set; } = null!;
    public AsyncRelayCommand PodcastPresetCommand { get; private set; } = null!;
    public AsyncRelayCommand MeetingPresetCommand { get; private set; } = null!;
    public AsyncRelayCommand WarmRadioPresetCommand { get; private set; } = null!;
    public RelayCommand ExportProfilesCommand { get; private set; } = null!;
    public RelayCommand ExportSelectedProfileCommand { get; private set; } = null!;
    public RelayCommand ImportProfilesCommand { get; private set; } = null!;
    public RelayCommand StartRecordingCommand { get; private set; } = null!;
    public RelayCommand StopRecordingCommand { get; private set; } = null!;
    public RelayCommand PlayPauseClipCommand { get; private set; } = null!;
    public RelayCommand ReplayClipCommand { get; private set; } = null!;
    public RelayCommand RewindClipCommand { get; private set; } = null!;
    public RelayCommand SaveClipCommand { get; private set; } = null!;
    public RelayCommand DeleteClipCommand { get; private set; } = null!;
    public bool SpeakingTrayEnabled { get => _speakingTrayEnabled; set { if (SetProperty(ref _speakingTrayEnabled, value)) QueueSave(); } }
    public bool MuteReminderEnabled { get => _muteReminderEnabled; set { if (SetProperty(ref _muteReminderEnabled, value)) QueueSave(); } }
    public bool PushToTalkSoundsEnabled { get => _pushToTalkSoundsEnabled; set { if (SetProperty(ref _pushToTalkSoundsEnabled, value)) QueueSave(); } }
    public bool HealthWarningsEnabled { get => _healthWarningsEnabled; set { if (SetProperty(ref _healthWarningsEnabled, value)) QueueSave(); } }
    public bool TraySpeaking { get => _traySpeaking; private set => SetProperty(ref _traySpeaking, value); }
    public string FeatureStatus { get => _featureStatus; private set => SetProperty(ref _featureStatus, value); }
    public string SessionText { get => _sessionText; private set => SetProperty(ref _sessionText, value); }
    public string HealthWarning { get => _healthWarning; private set => SetProperty(ref _healthWarning, value); }
    public bool ClipBusy { get => _clipBusy; private set { SetProperty(ref _clipBusy, value); RefreshFeatureCommands(); } }
    public bool IsRecording { get => _isRecording; private set { if (SetProperty(ref _isRecording, value)) { OnPropertyChanged(nameof(RecordingButtonText)); RefreshFeatureCommands(); } } }
    public bool IsClipPlaying { get => _isClipPlaying; private set { if (SetProperty(ref _isClipPlaying, value)) { OnPropertyChanged(nameof(PlayPauseText)); RefreshFeatureCommands(); } } }
    public string RecordingButtonText => IsRecording ? "Recording…" : "Start recording";
    public string PlayPauseText => IsClipPlaying ? "Pause" : "Play";
    public double ClipPosition
    {
        get => _clipPosition;
        set
        {
            var position = Math.Clamp(value, 0, ClipDuration);
            if (SetProperty(ref _clipPosition, position) && _clipPlayer is not null &&
                Math.Abs(_clipPlayer.PositionSeconds - position) > .35)
                _clipPlayer.PositionSeconds = position;
            OnPropertyChanged(nameof(ClipTimeText));
        }
    }
    public double ClipDuration { get => _clipDuration; private set { SetProperty(ref _clipDuration, value); OnPropertyChanged(nameof(ClipTimeText)); } }
    public string ClipTimeText => $"{FormatClipTime(ClipPosition)} / {FormatClipTime(ClipDuration)}";

    private void InitializeFeatures()
    {
        NaturalPresetCommand = new(() => UsePresetAsync("Natural"), () => !IsBusy && !ClipBusy);
        GamingPresetCommand = new(() => UsePresetAsync("Gaming"), () => !IsBusy && !ClipBusy);
        PodcastPresetCommand = new(() => UsePresetAsync("Podcast"), () => !IsBusy && !ClipBusy);
        MeetingPresetCommand = new(() => UsePresetAsync("Meeting"), () => !IsBusy && !ClipBusy);
        WarmRadioPresetCommand = new(() => UsePresetAsync("Warm Radio"), () => !IsBusy && !ClipBusy);
        ExportProfilesCommand = new(() => ExportProfiles(false));
        ExportSelectedProfileCommand = new(() => ExportProfiles(true));
        ImportProfilesCommand = new(ImportProfiles);
        StartRecordingCommand = new(StartRecording, () => IsRunning && !IsRecording && !ClipBusy);
        StopRecordingCommand = new(StopRecording, () => IsRecording);
        PlayPauseClipCommand = new(TogglePlayback, () => _lastClip is not null && !IsRecording);
        ReplayClipCommand = new(ReplayClip, () => _lastClip is not null && !IsRecording);
        RewindClipCommand = new(RewindClip, () => _lastClip is not null && !IsRecording);
        SaveClipCommand = new(SaveClip, () => _lastClip is not null && _diagnosisClip && !ClipBusy);
        DeleteClipCommand = new(DeleteClip, () => _lastClip is not null && !ClipBusy);
        InitializeTuning();
        _featureTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
        _featureTimer.Tick += FeatureTick;
    }

    private void LoadFeatureSettings(AppSettings settings)
    {
        _speakingTrayEnabled = settings.SpeakingTrayEnabled;
        _muteReminderEnabled = settings.MuteReminderEnabled;
        _pushToTalkSoundsEnabled = settings.PushToTalkSoundsEnabled;
        _healthWarningsEnabled = settings.HealthWarningsEnabled;
        foreach (var name in new[] { nameof(SpeakingTrayEnabled), nameof(MuteReminderEnabled), nameof(PushToTalkSoundsEnabled), nameof(HealthWarningsEnabled) })
            OnPropertyChanged(name);
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

    private string PlaybackDeviceId()
    {
        var device = SelectedMonitor ?? OutputDevices.FirstOrDefault(d => d.IsDefault &&
            !WasapiDeviceCatalog.IsLikelyVirtualCable(d.Name) && d.Id != SelectedOutput?.Id);
        if (device is null || device.Id == SelectedOutput?.Id || WasapiDeviceCatalog.IsLikelyVirtualCable(device.Name))
            throw new InvalidOperationException("Select headphones or speakers as the monitor output under Advanced Audio for playback and cues.");
        return device.Id;
    }

    private void StartRecording()
    {
        if (!IsRunning || IsRecording || ClipBusy) return;
        DeleteClip();
        try
        {
            _recordingBuffer = new AudioClipBuffer(120);
            _engine.BeginClip(_recordingBuffer);
            _recordingStarted = Stopwatch.GetTimestamp();
            IsRecording = true;
            FeatureStatus = "Recording… Press Stop when you are finished (maximum 2 minutes).";
        }
        catch (Exception exception) { _recordingBuffer = null; IsRecording = false; FeatureStatus = exception.Message; }
    }

    private void StopRecording()
    {
        if (!IsRecording) return;
        _lastClip = _engine.EndClip();
        _recordingBuffer = null;
        IsRecording = false;
        _diagnosisClip = _lastClip.Length > 0;
        ClipDuration = _lastClip.Length / 48_000d;
        ClipPosition = 0;
        FeatureStatus = _lastClip.Length == 0 ? "No audio was captured." : "Recording ready. You can play, rewind, replay, save, or delete it.";
        RefreshFeatureCommands();
    }

    private void EnsurePlayer()
    {
        if (_clipPlayer is not null || _lastClip is null) return;
        _clipPlayer = new RecordedClipPlayer(_lastClip, PlaybackDeviceId());
        ClipDuration = _clipPlayer.DurationSeconds;
    }

    private void TogglePlayback()
    {
        try
        {
            EnsurePlayer();
            if (_clipPlayer is null) return;
            if (_clipPlayer.IsPlaying) PausePlayback();
            else StartPlayback(restart: false);
        }
        catch (Exception exception) { EndPlaybackMute(); FeatureStatus = "Playback failed: " + exception.Message; }
    }

    private void ReplayClip()
    {
        try { EnsurePlayer(); StartPlayback(restart: true); }
        catch (Exception exception) { EndPlaybackMute(); FeatureStatus = "Playback failed: " + exception.Message; }
    }

    private void RewindClip()
    {
        try
        {
            EnsurePlayer();
            if (_clipPlayer is null) return;
            _clipPlayer.PositionSeconds = Math.Max(0, _clipPlayer.PositionSeconds - 10);
            ClipPosition = _clipPlayer.PositionSeconds;
            FeatureStatus = "Rewound 10 seconds.";
        }
        catch (Exception exception) { FeatureStatus = "Rewind failed: " + exception.Message; }
    }

    private void StartPlayback(bool restart)
    {
        if (_clipPlayer is null) return;
        if (restart) _clipPlayer.Rewind();
        _previewMuted = true;
        RaiseMuteChanged();
        _clipPlayer.Play();
        IsClipPlaying = true;
        FeatureStatus = restart ? "Playing again from the beginning." : "Playing recording. Drag the timeline to seek.";
    }

    private void PausePlayback()
    {
        _clipPlayer?.Pause();
        IsClipPlaying = false;
        EndPlaybackMute();
        FeatureStatus = "Playback paused.";
    }

    private void EndPlaybackMute()
    {
        if (!_previewMuted) return;
        _previewMuted = false;
        _suppressTalkCue = true;
        try { RaiseMuteChanged(); }
        finally { _suppressTalkCue = false; }
    }

    private void SaveClip()
    {
        if (_lastClip is null || !_diagnosisClip) return;
        try
        {
            var dialog = new SaveFileDialog { Filter = "WAV audio (*.wav)|*.wav", FileName = "Muted-diagnosis.wav" };
            if (dialog.ShowDialog() != true) return;
            File.WriteAllBytes(dialog.FileName, LocalAudioPlayer.WaveBytes(_lastClip));
            FeatureStatus = "Diagnosis WAV saved locally. Exported files remain until you delete them.";
        }
        catch (Exception exception) { FeatureStatus = "Save failed: " + exception.Message; }
    }

    private void DeleteClip()
    {
        _clipPlayer?.Dispose();
        _clipPlayer = null;
        EndPlaybackMute();
        if (_lastClip is not null) Array.Clear(_lastClip);
        _lastClip = null;
        _diagnosisClip = false;
        ClipPosition = 0;
        ClipDuration = 0;
        IsClipPlaying = false;
        FeatureStatus = "Temporary clip deleted from memory.";
        RefreshFeatureCommands();
    }

    private void FeatureEngineChanged()
    {
        if (_featureTimer is null || Volatile.Read(ref _disposed) != 0) return;
        if (IsRunning)
        {
            _feedback.Start(_feedbackClock.Elapsed.TotalSeconds);
            _featureTimer.Start();
            _lastTalkOpen = !IsEffectivelyMuted;
        }
        else
        {
            _featureTimer.Stop();
            TraySpeaking = false;
            HealthWarning = string.Empty;
            if (IsRecording) StopRecording();
            PausePlayback();
        }
        TuningEngineChanged();
        UpdateSessionText();
        RefreshFeatureCommands();
    }

    private void RefreshFeatureCommands()
    {
        StartRecordingCommand?.RaiseCanExecuteChanged(); StopRecordingCommand?.RaiseCanExecuteChanged();
        PlayPauseClipCommand?.RaiseCanExecuteChanged(); ReplayClipCommand?.RaiseCanExecuteChanged(); RewindClipCommand?.RaiseCanExecuteChanged();
        SaveClipCommand?.RaiseCanExecuteChanged(); DeleteClipCommand?.RaiseCanExecuteChanged();
        NaturalPresetCommand?.RaiseCanExecuteChanged(); GamingPresetCommand?.RaiseCanExecuteChanged();
        PodcastPresetCommand?.RaiseCanExecuteChanged(); MeetingPresetCommand?.RaiseCanExecuteChanged();
        WarmRadioPresetCommand?.RaiseCanExecuteChanged();
        RefreshTuningCommands();
    }

    private void FeatureTick(object? sender, EventArgs args)
    {
        var metrics = _engine.Metrics;
        TuningTick(metrics);
        if (IsRecording)
        {
            var elapsed = Stopwatch.GetElapsedTime(_recordingStarted).TotalSeconds;
            FeatureStatus = $"Recording… {FormatClipTime(elapsed)} / 02:00. Press Stop when finished." +
                (IsEffectivelyMuted ? " Microphone is muted, so silence is being captured." : string.Empty);
            if (_recordingBuffer?.Complete == true) { StopRecording(); FeatureStatus = "Maximum recording length of 2 minutes reached."; }
        }
        if (_clipPlayer is not null)
        {
            ClipPosition = _clipPlayer.PositionSeconds;
            if (IsClipPlaying && !_clipPlayer.IsPlaying)
            {
                IsClipPlaying = false;
                EndPlaybackMute();
                FeatureStatus = "Playback finished. Press Replay to hear it again.";
            }
        }
        var feedback = _feedback.Update(_feedbackClock.Elapsed.TotalSeconds, metrics,
            IsMuted && !_previewMuted, IsEffectivelyMuted, MuteReminderEnabled, HealthWarningsEnabled);
        TraySpeaking = SpeakingTrayEnabled && feedback.Speaking;
        if (feedback.RemindMute)
        {
            NotificationRequested?.Invoke(this, "You are speaking while Muted's microphone is muted.");
        }
        HealthWarning = feedback.Unhealthy ?
            "Audio is struggling. Close heavy apps or increase the buffer under Advanced Audio. Load is audio processing time, not total CPU usage." : string.Empty;
        if (feedback.NotifyHealth)
        {
            NotificationRequested?.Invoke(this, HealthWarning);
        }
        UpdateSessionText();
    }

    private void UpdateSessionText()
    {
        var stats = _engine.Session;
        SessionText = $"{(IsRunning ? "Current" : "Last")} audio session: {TimeSpan.FromSeconds(stats.Seconds):hh\\:mm\\:ss} · " +
            $"speaking (unmuted): {TimeSpan.FromSeconds(stats.SpeakingSeconds):hh\\:mm\\:ss}\n" +
            $"Limiter events: {stats.LimiterEvents} · average noise reduction: {stats.AverageNoiseReductionDb:0.0} dB · input peak: {FormatDecibels(stats.PeakInput)}";
    }

    private void UpdateTalkCue()
    {
        var open = !IsEffectivelyMuted;
        var changed = open != _lastTalkOpen;
        _lastTalkOpen = open;
        if (changed && IsRunning && IsPushToTalkArmed && PushToTalkSoundsEnabled && !_previewMuted &&
            !_suppressTalkCue && !ClipBusy && !_cuePlaying)
            _ = PlayCueAsync(open);
    }
    private async Task PlayCueAsync(bool open)
    {
        _cuePlaying = true;
        try { await LocalAudioPlayer.PlayAsync(LocalAudioPlayer.Cue(open), PlaybackDeviceId(), _featureLifetime.Token); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { FeatureStatus = "Push-to-talk sound: " + exception.Message; }
        finally { _cuePlaying = false; }
    }
    private void DisposeFeatures()
    {
        _featureTimer.Stop();
        _featureTimer.Tick -= FeatureTick;
        _featureLifetime.Cancel();
        _engine.EndClip();
        DeleteClip();
    }

    private static string FormatClipTime(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"hh\:mm\:ss" : @"mm\:ss");
}

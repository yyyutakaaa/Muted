using System.Diagnostics;
using System.IO;
using Muted.App.Infrastructure;
using Muted.App.Services;
using Muted.Audio.Windows.Devices;
using Muted.Core.Audio;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Muted.App.ViewModels;

/// <summary>Microphone test recordings, playback, diagnosis clips and before/after clips.</summary>
internal sealed partial class MainViewModel
{
    private bool _previewMuted, _clipBusy, _isRecording, _isClipPlaying;
    private string _featureStatus = "Ready. Recordings stay in memory until you explicitly save a diagnosis clip.";
    private float[]? _lastClip;
    private AudioClipBuffer? _recordingBuffer;
    private RecordedClipPlayer? _clipPlayer;
    private long _recordingStarted;
    private double _clipPosition, _clipDuration;
    private bool _diagnosisClip;
    private float[]? _lastRawClip;

    public RelayCommand StartRecordingCommand { get; private set; } = null!;

    public RelayCommand StopRecordingCommand { get; private set; } = null!;

    public RelayCommand PlayPauseClipCommand { get; private set; } = null!;

    public RelayCommand ReplayClipCommand { get; private set; } = null!;

    public RelayCommand RewindClipCommand { get; private set; } = null!;

    public RelayCommand SaveClipCommand { get; private set; } = null!;

    public RelayCommand DeleteClipCommand { get; private set; } = null!;

    public string FeatureStatus { get => _featureStatus; private set => SetProperty(ref _featureStatus, value); }

    public bool ClipBusy { get => _clipBusy; private set { SetProperty(ref _clipBusy, value); RefreshToolCommands(); } }

    public bool IsRecording { get => _isRecording; private set { if (SetProperty(ref _isRecording, value)) { OnPropertyChanged(nameof(RecordingButtonText)); RefreshToolCommands(); } } }

    public bool IsClipPlaying { get => _isClipPlaying; private set { if (SetProperty(ref _isClipPlaying, value)) { OnPropertyChanged(nameof(PlayPauseText)); RefreshToolCommands(); } } }

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

    public RelayCommand SaveComparisonCommand { get; private set; } = null!;

    public string VoiceTestStateText => IsRecording ? "Recording…"
        : IsClipPlaying ? "Playing · " + ClipTimeText
        : ClipDuration > 0 ? "Recorded · " + ClipTimeText
        : IsRunning ? "Ready to record" : "Start processing to record";

    private void InitializeRecordingCommands()
    {
        StartRecordingCommand = new(StartRecording, () => IsRunning && !IsRecording && !ClipBusy);
        StopRecordingCommand = new(StopRecording, () => IsRecording);
        PlayPauseClipCommand = new(TogglePlayback, () => _lastClip is not null && !IsRecording);
        ReplayClipCommand = new(ReplayClip, () => _lastClip is not null && !IsRecording);
        RewindClipCommand = new(RewindClip, () => _lastClip is not null && !IsRecording);
        SaveClipCommand = new(SaveClip, () => _lastClip is not null && _diagnosisClip && !ClipBusy);
        DeleteClipCommand = new(DeleteClip, () => _lastClip is not null && !ClipBusy);
        SaveComparisonCommand = new RelayCommand(
            SaveComparison,
            () => _lastClip is { Length: > 0 } && _lastRawClip is { Length: > 0 } && !IsRecording && !ClipBusy);
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
            _engine.BeginClip(_recordingBuffer, new AudioClipBuffer(120));
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
        _lastRawClip = _engine.EndRawClip();
        _recordingBuffer = null;
        IsRecording = false;
        _diagnosisClip = _lastClip.Length > 0;
        ClipDuration = _lastClip.Length / 48_000d;
        ClipPosition = 0;
        FeatureStatus = _lastClip.Length == 0 ? "No audio was captured." : "Recording ready. Play it, or save a before/after WAV to hear what Muted removed.";
        RefreshToolCommands();
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
        if (_lastRawClip is not null) Array.Clear(_lastRawClip);
        _lastRawClip = null;
        _diagnosisClip = false;
        ClipPosition = 0;
        ClipDuration = 0;
        IsClipPlaying = false;
        FeatureStatus = "Temporary clip deleted from memory.";
        RefreshToolCommands();
    }

    private static string FormatClipTime(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"hh\:mm\:ss" : @"mm\:ss");

    private void SaveComparison()
    {
        if (_lastClip is not { Length: > 0 } after || _lastRawClip is not { Length: > 0 } before)
        {
            return;
        }

        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "WAV audio (*.wav)|*.wav",
                FileName = "Muted-before-after.wav"
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            File.WriteAllBytes(dialog.FileName, LocalAudioPlayer.WaveBytes(ComparisonClip.Build(before, after)));
            FeatureStatus = "Saved: first without Muted, then a short pause, then with Muted. Share it anywhere you like.";
        }
        catch (Exception exception)
        {
            FeatureStatus = "Save failed: " + exception.Message;
        }
    }
}

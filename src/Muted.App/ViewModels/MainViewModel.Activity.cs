using System.Diagnostics;
using System.Windows.Threading;

namespace Muted.App.ViewModels;

/// <summary>The 200 ms timer that drives recording, feedback, tuning, statistics and listening while audio runs.</summary>
internal sealed partial class MainViewModel
{
    private DispatcherTimer _activityTimer = null!;

    private void InitializeActivityTimer()
    {
        _activityTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
        _activityTimer.Tick += OnActivityTick;
    }

    private void UpdateActivityForEngineState()
    {
        if (_activityTimer is null || Volatile.Read(ref _disposed) != 0) return;
        if (IsRunning)
        {
            _feedback.Start(_feedbackClock.Elapsed.TotalSeconds);
            _activityTimer.Start();
            _lastTalkOpen = !IsEffectivelyMuted;
        }
        else
        {
            _activityTimer.Stop();
            TraySpeaking = false;
            OverlaySpeaking = false;
            HealthWarning = string.Empty;
            if (IsRecording) StopRecording();
            PausePlayback();
        }
        TuningEngineChanged();
        StatisticsEngineChanged();
        UpdateSessionText();
        RefreshToolCommands();
    }

    private void RefreshToolCommands()
    {
        StartRecordingCommand?.RaiseCanExecuteChanged(); StopRecordingCommand?.RaiseCanExecuteChanged();
        PlayPauseClipCommand?.RaiseCanExecuteChanged(); ReplayClipCommand?.RaiseCanExecuteChanged(); RewindClipCommand?.RaiseCanExecuteChanged();
        SaveClipCommand?.RaiseCanExecuteChanged(); DeleteClipCommand?.RaiseCanExecuteChanged();
        SaveComparisonCommand?.RaiseCanExecuteChanged();
        NaturalPresetCommand?.RaiseCanExecuteChanged(); GamingPresetCommand?.RaiseCanExecuteChanged();
        PodcastPresetCommand?.RaiseCanExecuteChanged(); MeetingPresetCommand?.RaiseCanExecuteChanged();
        WarmRadioPresetCommand?.RaiseCanExecuteChanged();
        RefreshTuningCommands();
    }

    private void OnActivityTick(object? sender, EventArgs args)
    {
        var metrics = _engine.Metrics;
        TuningTick(metrics);
        StatisticsTick();
        ListeningTick();
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
        OverlaySpeaking = feedback.Speaking;
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

    private void DisposeTools()
    {
        _activityTimer.Stop();
        _activityTimer.Tick -= OnActivityTick;
        _toolsLifetime.Cancel();
        _engine.EndClip();
        _engine.EndRawClip();
        DeleteClip();
    }
}

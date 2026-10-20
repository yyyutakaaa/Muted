using System.Diagnostics;
using Muted.App.Infrastructure;
using Muted.Core.Audio;
using Muted.Core.Dsp;

namespace Muted.App.ViewModels;

/// <summary>
/// "Tune to my voice": a short guided calibration that picks the settings, and an
/// optional automatic mode that keeps the boost and the gate right while you talk.
/// </summary>
internal sealed partial class MainViewModel
{
    private const double QuietSeconds = 4;
    private const double SpeechEndSeconds = 19;
    private const double CalibrationSeconds = 24;
    private const string ReadingText =
        "The quick brown fox jumps over the lazy dog. She sells sea shells by the sea shore, " +
        "and on Sundays we play games together until late at night.";

    private readonly AutoTuner _autoTuner = new();
    private readonly Stopwatch _tuningClock = Stopwatch.StartNew();
    private CalibrationRecorder? _calibrationRecorder;
    private long _calibrationStarted;
    private TuningSnapshot? _calibrationUndo;
    private bool _isCalibrating;
    private bool _hasCalibrationResult;
    private bool _autoTuneEnabled;
    private bool _isAutoTuning;
    private double _calibrationProgress;
    private string _calibrationPrompt = string.Empty;
    private string _calibrationResultText = string.Empty;
    private string _calibrationIntro = "Takes 24 seconds: stay quiet, read a sentence aloud, then say one thing loudly.";
    private string _autoTuneStatusText = "Off";

    public RelayCommand StartCalibrationCommand { get; private set; } = null!;
    public RelayCommand CancelCalibrationCommand { get; private set; } = null!;
    public RelayCommand KeepCalibrationCommand { get; private set; } = null!;
    public RelayCommand UndoCalibrationCommand { get; private set; } = null!;

    public bool IsCalibrating
    {
        get => _isCalibrating;
        private set
        {
            if (SetProperty(ref _isCalibrating, value))
            {
                RefreshTuningCommands();
            }
        }
    }

    public bool HasCalibrationResult
    {
        get => _hasCalibrationResult;
        private set
        {
            if (SetProperty(ref _hasCalibrationResult, value))
            {
                RefreshTuningCommands();
            }
        }
    }

    public double CalibrationProgress
    {
        get => _calibrationProgress;
        private set => SetProperty(ref _calibrationProgress, value);
    }

    public string CalibrationPrompt
    {
        get => _calibrationPrompt;
        private set => SetProperty(ref _calibrationPrompt, value);
    }

    public string CalibrationResultText
    {
        get => _calibrationResultText;
        private set => SetProperty(ref _calibrationResultText, value);
    }

    public string CalibrationIntro
    {
        get => _calibrationIntro;
        private set => SetProperty(ref _calibrationIntro, value);
    }

    /// <summary>Keeps the boost and the gate right while you talk. Never touches the shout guard.</summary>
    public bool AutoTuneEnabled
    {
        get => _autoTuneEnabled;
        set
        {
            if (!SetProperty(ref _autoTuneEnabled, value))
            {
                return;
            }

            if (value)
            {
                ResetAutoTuneBaseline();
            }
            else
            {
                _autoTuner.Stop();
            }

            UpdateAutoTuneStatus();
            QueueSave();
        }
    }

    public string AutoTuneStatusText
    {
        get => _autoTuneStatusText;
        private set => SetProperty(ref _autoTuneStatusText, value);
    }

    private void InitializeTuning()
    {
        StartCalibrationCommand = new(StartCalibration, () => IsRunning && !IsCalibrating && !IsRecording && !ClipBusy);
        CancelCalibrationCommand = new(() => CancelCalibration("Calibration cancelled."), () => IsCalibrating);
        KeepCalibrationCommand = new(KeepCalibration, () => HasCalibrationResult);
        UndoCalibrationCommand = new(UndoCalibration, () => HasCalibrationResult && _calibrationUndo is not null);
    }

    private void RefreshTuningCommands()
    {
        StartCalibrationCommand?.RaiseCanExecuteChanged();
        CancelCalibrationCommand?.RaiseCanExecuteChanged();
        KeepCalibrationCommand?.RaiseCanExecuteChanged();
        UndoCalibrationCommand?.RaiseCanExecuteChanged();
    }

    private void TuningEngineChanged()
    {
        if (!IsRunning && IsCalibrating)
        {
            CancelCalibration("Calibration stopped because audio stopped.");
        }

        if (IsRunning && AutoTuneEnabled)
        {
            ResetAutoTuneBaseline();
        }

        UpdateAutoTuneStatus();
        RefreshTuningCommands();
    }

    private void TuningTick(AudioMetrics metrics)
    {
        if (IsCalibrating)
        {
            AdvanceCalibration();
            return;
        }

        if (!AutoTuneEnabled || !IsRunning)
        {
            return;
        }

        var step = _autoTuner.Update(
            _tuningClock.Elapsed.TotalSeconds,
            metrics.TrackedSpeechDb,
            metrics.TrackedQuietProbability,
            metrics.TrackedSpeechFrames,
            (float)InputGain,
            (float)VoiceSensitivity);
        if (step is not { } change)
        {
            return;
        }

        _isAutoTuning = true;
        try
        {
            InputGain = change.InputGain;
            VoiceSensitivity = change.VoiceThreshold;
        }
        finally
        {
            _isAutoTuning = false;
        }

        AutoTuneStatusText = $"{DateTime.Now:HH:mm} · {change.Reason}";
    }

    private void ResetAutoTuneBaseline()
    {
        if (!AutoTuneEnabled || _isAutoTuning)
        {
            return;
        }

        _autoTuner.Reset(
            (float)InputGain,
            (float)VoiceSensitivity,
            _engine.Metrics.TrackedSpeechFrames,
            _tuningClock.Elapsed.TotalSeconds);
    }

    private void UpdateAutoTuneStatus()
    {
        if (!AutoTuneEnabled)
        {
            AutoTuneStatusText = "Off";
        }
        else if (!IsRunning)
        {
            AutoTuneStatusText = "Waiting for audio";
        }
        else if (!AutoTuneStatusText.Contains('·'))
        {
            AutoTuneStatusText = "Listening. Small changes appear here.";
        }
    }

    private void StartCalibration()
    {
        if (!IsRunning || IsCalibrating)
        {
            return;
        }

        try
        {
            HasCalibrationResult = false;
            CalibrationResultText = string.Empty;
            _calibrationRecorder = new CalibrationRecorder((int)Math.Ceiling(CalibrationSeconds) + 1);
            _engine.BeginCalibration(_calibrationRecorder);
            _calibrationStarted = Stopwatch.GetTimestamp();
            CalibrationProgress = 0;
            IsCalibrating = true;
            AdvanceCalibration();
        }
        catch (Exception exception)
        {
            _log.Write(exception, "Start voice calibration");
            _calibrationRecorder = null;
            CalibrationIntro = "Calibration could not start: " + exception.Message;
        }
    }

    private void AdvanceCalibration()
    {
        var elapsed = Stopwatch.GetElapsedTime(_calibrationStarted).TotalSeconds;
        CalibrationProgress = Math.Clamp(elapsed / CalibrationSeconds, 0, 1);
        if (elapsed < QuietSeconds)
        {
            CalibrationPrompt = $"Stay quiet for a moment… {Math.Ceiling(QuietSeconds - elapsed):0}";
        }
        else if (elapsed < SpeechEndSeconds)
        {
            CalibrationPrompt = $"Read this aloud, the way you normally talk:\n“{ReadingText}”";
        }
        else if (elapsed < CalibrationSeconds)
        {
            CalibrationPrompt = $"Now say something loudly, like you would in a game. “No way, behind you!” {Math.Ceiling(CalibrationSeconds - elapsed):0}";
        }
        else
        {
            FinishCalibration();
        }
    }

    private void CancelCalibration(string message)
    {
        _engine.EndCalibration();
        _calibrationRecorder = null;
        IsCalibrating = false;
        CalibrationProgress = 0;
        CalibrationPrompt = string.Empty;
        CalibrationIntro = message;
    }

    private void FinishCalibration()
    {
        var recording = _engine.EndCalibration();
        _calibrationRecorder = null;
        IsCalibrating = false;
        CalibrationPrompt = string.Empty;
        if (recording is null || recording.Frames == 0)
        {
            CalibrationIntro = "Nothing was recorded. Check that the right microphone is selected.";
            return;
        }

        VoiceMeasurement measurement;
        VoiceRecommendation recommendation;
        try
        {
            measurement = VoiceAnalyzer.Measure(recording, QuietSeconds, SpeechEndSeconds);
            recommendation = VoiceAnalyzer.Recommend(measurement);
        }
        catch (Exception exception)
        {
            _log.Write(exception, "Analyse voice calibration");
            CalibrationIntro = "The recording could not be analysed. Try again.";
            return;
        }

        if (!recommendation.Reliable)
        {
            CalibrationIntro = string.Join(" ", recommendation.Notes);
            return;
        }

        var before = TuningSnapshot.Capture(this);
        ApplyRecommendation(recommendation);
        var after = TuningSnapshot.Capture(this);
        _calibrationUndo = before;
        CalibrationResultText = DescribeCalibration(measurement, recommendation, before, after);
        CalibrationIntro = "Applied, you are hearing it now. Keep it, or undo to go back.";
        HasCalibrationResult = true;
    }

    private void ApplyRecommendation(VoiceRecommendation recommendation)
    {
        _isAutoTuning = true;
        try
        {
            InputGain = recommendation.InputGain;
            VoiceSensitivity = recommendation.VoiceThreshold;
            HighPassEnabled = true;
            HighPassFrequency = recommendation.HighPassFrequency;
            ShoutThresholdDb = Math.Round(recommendation.ShoutThresholdDb);
            Warmth = recommendation.Warmth;
            Proximity = recommendation.Proximity;
            Smoothness = recommendation.Smoothness;
            Compression = recommendation.Compression;
        }
        finally
        {
            _isAutoTuning = false;
        }
    }

    private void KeepCalibration()
    {
        _calibrationUndo = null;
        HasCalibrationResult = false;
        CalibrationIntro = "Saved. Run it again whenever your setup changes.";
        ResetAutoTuneBaseline();
    }

    private void UndoCalibration()
    {
        if (_calibrationUndo is { } previous)
        {
            _isAutoTuning = true;
            try
            {
                previous.Restore(this);
            }
            finally
            {
                _isAutoTuning = false;
            }
        }

        _calibrationUndo = null;
        HasCalibrationResult = false;
        CalibrationIntro = "Back to your previous settings.";
        ResetAutoTuneBaseline();
    }

    private string DescribeCalibration(
        VoiceMeasurement measurement,
        VoiceRecommendation recommendation,
        TuningSnapshot before,
        TuningSnapshot after)
    {
        var lines = new List<string>();
        var pitch = float.IsFinite(measurement.PitchHz) ? $"around {measurement.PitchHz:0} Hz" : "pitch unclear";
        lines.Add($"Your voice: {pitch}, speech {measurement.SpeechLevelDb:0} dB, room {measurement.NoiseFloorDb:0} dB.");
        lines.Add($"Microphone boost: {FormatGain(before.InputGain)} → {FormatGain(after.InputGain)}");
        lines.Add($"Voice gate: {before.VoiceThreshold:0.00} → {after.VoiceThreshold:0.00}");
        lines.Add($"High-pass: {(before.HighPassEnabled ? $"{before.HighPassFrequency:0} Hz" : "off")} → {after.HighPassFrequency:0} Hz");
        lines.Add($"Shout limit: {before.ShoutThresholdDb:0} dB → {after.ShoutThresholdDb:0} dB" +
                  (ShoutGuardEnabled ? string.Empty : " (switch Shout guard on to use it)"));
        lines.Add($"Warm voice: warmth {FormatAmount(after.Warmth)}, proximity {FormatAmount(after.Proximity)}, " +
                  $"smoothness {FormatAmount(after.Smoothness)}, compression {FormatAmount(after.Compression)}" +
                  (VoiceCharacterEnabled ? string.Empty : " (switch Warm radio voice on to hear it)"));
        lines.AddRange(recommendation.Notes);
        return string.Join("\n", lines);
    }

    /// <summary>Every value the calibration may change, so Undo can put it back.</summary>
    private sealed record TuningSnapshot(
        double InputGain,
        double VoiceThreshold,
        bool HighPassEnabled,
        double HighPassFrequency,
        double ShoutThresholdDb,
        double Warmth,
        double Proximity,
        double Smoothness,
        double Compression)
    {
        public static TuningSnapshot Capture(MainViewModel model) => new(
            model.InputGain,
            model.VoiceSensitivity,
            model.HighPassEnabled,
            model.HighPassFrequency,
            model.ShoutThresholdDb,
            model.Warmth,
            model.Proximity,
            model.Smoothness,
            model.Compression);

        public void Restore(MainViewModel model)
        {
            model.InputGain = InputGain;
            model.VoiceSensitivity = VoiceThreshold;
            model.HighPassEnabled = HighPassEnabled;
            model.HighPassFrequency = HighPassFrequency;
            model.ShoutThresholdDb = ShoutThresholdDb;
            model.Warmth = Warmth;
            model.Proximity = Proximity;
            model.Smoothness = Smoothness;
            model.Compression = Compression;
        }
    }
}

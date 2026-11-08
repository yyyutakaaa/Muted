using System.Diagnostics;
using Muted.App.Infrastructure;
using Muted.Audio.Windows.Speech;
using Muted.Core.Audio;
using Muted.Core.Dsp;
using Muted.Core.Speech;

namespace Muted.App.ViewModels;

/// <summary>
/// "Tune to my voice": a short guided calibration that picks the settings and learns
/// your voice, and an optional automatic mode that keeps the boost and the gate right
/// while you talk. Speech recognition follows what you read, so each step ends when you
/// are done instead of after a fixed time.
/// </summary>
internal sealed partial class MainViewModel
{
    // Whisper listens to at most this much of a step at once: the end of the sentence is
    // what matters, and shorter audio keeps every pass under a second or so.
    private const int ListenWindowFrames = 12 * CalibrationFlow.FramesPerSecond;

    // Three seconds of speech per embedding; a voice profile needs at least one.
    private const int EnrollmentChunkSamples = 3 * SpeakerFeatures.SampleRate;

    private readonly AutoTuner _autoTuner = new();
    private readonly Stopwatch _tuningClock = Stopwatch.StartNew();
    private CalibrationRecorder? _calibrationRecorder;
    private CalibrationFlow? _calibrationFlow;
    private CancellationTokenSource? _listenCancellation;
    private TuningSnapshot? _calibrationUndo;
    private VoiceProfile? _voiceUndo;
    private bool _voiceUndoPending;
    private bool _isCalibrating;
    private bool _hasCalibrationResult;
    private bool _autoTuneEnabled;
    private bool _isAutoTuning;
    private double _calibrationProgress;
    private string _calibrationPrompt = string.Empty;
    private string _calibrationResultText = string.Empty;
    private string _calibrationIntro = "Takes about 20 seconds: stay quiet, read a sentence aloud, then say one thing loudly. Muted moves on as soon as you are done, and learns your voice on the way.";
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
        UndoCalibrationCommand = new(UndoCalibration, () => HasCalibrationResult && (_calibrationUndo is not null || _voiceUndoPending));
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

        // Studio Voice only removes noise; it never moves a control for you.
        if (StudioVoice || !AutoTuneEnabled || !IsRunning)
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
            _calibrationRecorder = new CalibrationRecorder(CalibrationFlow.MaxSeconds);
            _calibrationFlow = new CalibrationFlow(SpeechModels.WhisperAvailable);
            _engine.BeginCalibration(_calibrationRecorder);
            CalibrationProgress = 0;
            IsCalibrating = true;
            _listenCancellation = new CancellationTokenSource();
            if (_calibrationFlow.RecognizerAvailable)
            {
                var recorder = _calibrationRecorder;
                var flow = _calibrationFlow;
                var token = _listenCancellation.Token;
                _ = Task.Run(() => ListenWhileCalibrating(recorder, flow, token), token);
            }

            AdvanceCalibration();
        }
        catch (Exception exception)
        {
            _log.Write(exception, "Start voice calibration");
            _calibrationRecorder = null;
            _calibrationFlow = null;
            CalibrationIntro = "Calibration could not start: " + exception.Message;
        }
    }

    /// <summary>
    /// Runs Whisper over the current step again and again while the calibration runs,
    /// and hands each transcript to the UI thread. Loads the model during the quiet step.
    /// </summary>
    private void ListenWhileCalibrating(CalibrationRecorder recorder, CalibrationFlow flow, CancellationToken token)
    {
        WhisperRecognizer? recognizer = null;
        try
        {
            recognizer = new WhisperRecognizer();
            while (!token.IsCancellationRequested)
            {
                // Phase and its start are read from the UI thread's object without a lock;
                // a stale read only means one transcript for a step that just ended,
                // which the flow ignores.
                var phase = flow.Phase;
                if (phase is CalibrationPhase.Quiet)
                {
                    token.WaitHandle.WaitOne(100);
                    continue;
                }

                if (phase is CalibrationPhase.Done)
                {
                    return;
                }

                var audio = recorder.RawSince(flow.PhaseStartFrame, ListenWindowFrames);
                if (audio.Length < CalibrationRecorder.SampleRate / 2)
                {
                    token.WaitHandle.WaitOne(100);
                    continue;
                }

                var text = recognizer.Transcribe(Downsampler.Downsample(audio), token);
                _uiContext.Post(_ =>
                {
                    if (ReferenceEquals(_calibrationFlow, flow))
                    {
                        flow.Hear(phase, text);
                    }
                }, null);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            // Without recognition the calibration still finishes, on pauses alone.
            _log.Write(exception, "Speech recognition during calibration");
            _uiContext.Post(_ => flow.RecognizerAvailable = false, null);
        }
        finally
        {
            recognizer?.Dispose();
        }
    }

    private void AdvanceCalibration()
    {
        var recorder = _calibrationRecorder;
        var flow = _calibrationFlow;
        if (recorder is null || flow is null)
        {
            return;
        }

        flow.Update(recorder.RecordedFrames, recorder.ProbabilitiesSince(flow.PhaseStartFrame));
        CalibrationProgress = flow.Progress;
        switch (flow.Phase)
        {
            case CalibrationPhase.Quiet:
                var left = Math.Ceiling((CalibrationFlow.QuietFrames - recorder.RecordedFrames) / (double)CalibrationFlow.FramesPerSecond);
                CalibrationPrompt = $"Stay quiet for a moment… {Math.Max(1, left):0}";
                break;
            case CalibrationPhase.Reading:
                CalibrationPrompt = $"Read this aloud, the way you normally talk:\n“{CalibrationFlow.ReadingText}”" + HeardLine(flow);
                break;
            case CalibrationPhase.Loud:
                CalibrationPrompt = $"Now say something loudly, like you would in a game:\n“{CalibrationFlow.LoudText}”" + HeardLine(flow);
                break;
            default:
                FinishCalibration();
                break;
        }
    }

    private static string HeardLine(CalibrationFlow flow) =>
        string.IsNullOrWhiteSpace(flow.Heard) ? string.Empty : $"\n\nHeard: {flow.Heard}";

    private void StopListening()
    {
        _listenCancellation?.Cancel();
        _listenCancellation?.Dispose();
        _listenCancellation = null;
    }

    private void CancelCalibration(string message)
    {
        StopListening();
        _engine.EndCalibration();
        _calibrationRecorder = null;
        _calibrationFlow = null;
        IsCalibrating = false;
        CalibrationProgress = 0;
        CalibrationPrompt = string.Empty;
        CalibrationIntro = message;
    }

    private void FinishCalibration()
    {
        StopListening();
        var flow = _calibrationFlow;
        var recording = _engine.EndCalibration();
        _calibrationRecorder = null;
        _calibrationFlow = null;
        IsCalibrating = false;
        CalibrationPrompt = string.Empty;
        if (recording is null || recording.Frames == 0 || flow is null)
        {
            CalibrationIntro = "Nothing was recorded. Check that the right microphone is selected.";
            return;
        }

        VoiceMeasurement measurement;
        VoiceRecommendation recommendation;
        try
        {
            measurement = VoiceAnalyzer.Measure(
                recording,
                flow.ReadingStartFrame / (double)CalibrationFlow.FramesPerSecond,
                flow.LoudStartFrame / (double)CalibrationFlow.FramesPerSecond);
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
        LearnVoice(recording, flow.ReadingStartFrame);
    }

    /// <summary>
    /// Builds a voice profile from the speech in the calibration, off the UI thread, and
    /// applies it when it is ready. Undo puts the previous profile back.
    /// </summary>
    private void LearnVoice(CalibrationRecording recording, int fromFrame)
    {
        if (!SpeechModels.SpeakerAvailable)
        {
            return;
        }

        CalibrationResultText += "\nLearning your voice…";
        _ = Task.Run(() =>
        {
            try
            {
                return BuildVoiceProfile(recording, fromFrame);
            }
            catch (Exception exception)
            {
                _log.Write(exception, "Learn voice from calibration");
                return null;
            }
        }).ContinueWith(task => _uiContext.Post(_ =>
        {
            var learned = task.Result;
            CalibrationResultText = CalibrationResultText.Replace("\nLearning your voice…", string.Empty);
            if (learned is null)
            {
                CalibrationResultText += "\nYour voice could not be learned: there was too little clear speech.";
                return;
            }

            _voiceUndo = _voiceProfile;
            _voiceUndoPending = true;
            LearnedVoice = learned;
            CalibrationResultText += OnlyMyVoice
                ? "\nOnly my voice: learned your voice, other voices are held back now."
                : "\nOnly my voice: learned your voice. Switch it on below to hold other voices back.";
            RefreshTuningCommands();
        }, null), TaskScheduler.Default);
    }

    /// <summary>Your speech from the calibration, cut in 3 s pieces, each turned into an embedding and averaged.</summary>
    private static VoiceProfile? BuildVoiceProfile(CalibrationRecording recording, int fromFrame)
    {
        var frameSize = CalibrationRecorder.FrameSize;
        var speech = new List<float>();
        for (var frame = Math.Max(0, fromFrame); frame < recording.Frames; frame++)
        {
            if (recording.VoiceProbabilities[frame] >= 0.6f)
            {
                speech.AddRange(recording.Cleaned.AsSpan(frame * frameSize, frameSize).ToArray());
            }
        }

        var audio = Downsampler.Downsample(speech.ToArray());
        if (audio.Length < EnrollmentChunkSamples)
        {
            return null;
        }

        using var embedder = new SpeakerEmbedder();
        var embeddings = new List<float[]>();
        for (var start = 0; start + EnrollmentChunkSamples <= audio.Length; start += EnrollmentChunkSamples)
        {
            embeddings.Add(embedder.Embed(audio.AsSpan(start, EnrollmentChunkSamples)));
        }

        return VoiceProfile.Average(embeddings);
    }

    private void ApplyRecommendation(VoiceRecommendation recommendation)
    {
        _isAutoTuning = true;
        try
        {
            InputGain = recommendation.InputGain;

            // Automatic level already evens out the volume after the noise removal, so
            // the make-up stays out of its way.
            OutputGain = AutoGainEnabled ? 1.0 : recommendation.OutputGain;
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
        _voiceUndo = null;
        _voiceUndoPending = false;
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

        if (_voiceUndoPending)
        {
            LearnedVoice = _voiceUndo;
        }

        _calibrationUndo = null;
        _voiceUndo = null;
        _voiceUndoPending = false;
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
        lines.Add(AutoGainEnabled
            ? "Voice level: left to Automatic level."
            : $"Voice level, after the noise removal: {FormatGain(before.OutputGain)} → {FormatGain(after.OutputGain)}");
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
        double OutputGain,
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
            model.OutputGain,
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
            model.OutputGain = OutputGain;
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

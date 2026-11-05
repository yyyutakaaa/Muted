using System.Globalization;
using System.Windows.Threading;
using Muted.Core.Dsp;

namespace Muted.App.ViewModels;

/// <summary>Live meters and read-outs, refreshed on a timer while a window is visible.</summary>
internal sealed partial class MainViewModel
{
    private const int TextTicksPerUpdate = 5;
    private readonly DispatcherTimer _meterTimer;
    private double _inputLevel;
    private double _outputLevel;
    private double _voiceProbability;
    private string _inputLevelDb = "–∞ dB";
    private string _outputLevelDb = "–∞ dB";
    private string _reductionText = "0.0 dB";
    private string _healthText = "Idle";
    private string _dropoutText = "No dropouts";
    private bool _uiVisible = true;
    private int _textTickCounter;
    private string _processingLoadText = "Load –";
    private string _bufferText = "Buffer –";

    /// <summary>Frame peaks the live waveform draws.</summary>
    public WaveformScope Scope => _engine.Scope;

    public double InputLevel
    {
        get => _inputLevel;
        private set => SetProperty(ref _inputLevel, value);
    }

    public double OutputLevel
    {
        get => _outputLevel;
        private set => SetProperty(ref _outputLevel, value);
    }

    public double VoiceProbability
    {
        get => _voiceProbability;
        private set
        {
            if (SetProperty(ref _voiceProbability, value))
            {
                OnPropertyChanged(nameof(IsVoiceDetected));
            }
        }
    }

    public bool IsVoiceDetected => IsRunning && VoiceProbability >= VoiceSensitivity;

    public string InputLevelDb
    {
        get => _inputLevelDb;
        private set => SetProperty(ref _inputLevelDb, value);
    }

    public string OutputLevelDb
    {
        get => _outputLevelDb;
        private set => SetProperty(ref _outputLevelDb, value);
    }

    public string ReductionText
    {
        get => _reductionText;
        private set => SetProperty(ref _reductionText, value);
    }

    public string HealthText
    {
        get => _healthText;
        private set => SetProperty(ref _healthText, value);
    }

    public string DropoutText
    {
        get => _dropoutText;
        private set => SetProperty(ref _dropoutText, value);
    }

    /// <summary>Processing load of the realtime thread, the closest thing Muted measures to CPU use.</summary>
    public string ProcessingLoadText
    {
        get => _processingLoadText;
        private set => SetProperty(ref _processingLoadText, value);
    }

    public string BufferText
    {
        get => _bufferText;
        private set => SetProperty(ref _bufferText, value);
    }

    public void SetUiVisible(bool visible)
    {
        _uiVisible = visible;
        UpdateMeterTimer();
    }

    private void UpdateMeterTimer()
    {
        var shouldRun = IsRunning && _uiVisible && Volatile.Read(ref _disposed) == 0;
        if (shouldRun && !_meterTimer.IsEnabled)
        {
            _meterTimer.Start();
        }
        else if (!shouldRun && _meterTimer.IsEnabled)
        {
            _meterTimer.Stop();
        }

        if (!IsRunning)
        {
            _textTickCounter = TextTicksPerUpdate;
            InputLevel = 0;
            OutputLevel = 0;
            VoiceProbability = 0;
            InputLevelDb = "–∞ dB";
            OutputLevelDb = "–∞ dB";
            ReductionText = "0.0 dB";
            EchoReductionText = "0.0 dB";
            HealthText = "Idle";
            DropoutText = "No dropouts";
            UpdateLayoutMetrics(0, 0, running: false);
        }
    }

    private void OnMeterTick(object? sender, EventArgs eventArgs)
    {
        var metrics = _engine.Metrics;
        InputLevel = Smooth(InputLevel, metrics.InputPeak);
        OutputLevel = Smooth(OutputLevel, metrics.OutputPeak);
        VoiceProbability = Smooth(VoiceProbability, metrics.VoiceProbability, decay: 0.10);

        // Meters move at the timer rate; the numbers next to them are read, not watched,
        // so they refresh five times a second instead of formatting five strings per tick.
        if (++_textTickCounter < TextTicksPerUpdate)
        {
            return;
        }

        _textTickCounter = 0;
        InputLevelDb = FormatDecibels(InputLevel);
        OutputLevelDb = FormatDecibels(OutputLevel);
        ReductionText = $"{metrics.NoiseReductionDb:0.0} dB";
        EchoReductionText = metrics.EchoActive
            ? $"{metrics.EchoReductionDb:0.0} dB"
            : "not running";
        HealthText = $"{metrics.BufferedMilliseconds:0} ms buffer · {metrics.ProcessingLoad * 100:0}% load";
        UpdateLayoutMetrics(metrics.BufferedMilliseconds, metrics.ProcessingLoad, running: true);
        UpdateVoiceReadouts(metrics, IsRunning);

        var dropped = metrics.DroppedInputSamples + metrics.DroppedOutputSamples;
        DropoutText = dropped == 0 && metrics.OutputUnderrunSamples == 0
            ? "No dropouts"
            : $"{metrics.OutputUnderrunSamples} underruns · {dropped} dropped";
    }

    private static string FormatDecibels(double level) =>
        level <= 0.001 ? "–∞ dB" : $"{AudioMath.ToDecibels((float)level):0.0} dB";

    // Peaks rise instantly and fall gradually so the meters read naturally.
    private static double Smooth(double current, double target, double decay = 0.05)
    {
        var clamped = Math.Clamp(target, 0d, 1d);
        return clamped >= current ? clamped : Math.Max(clamped, current - decay);
    }

    private void UpdateLayoutMetrics(double bufferedMilliseconds, float processingLoad, bool running)
    {
        ProcessingLoadText = running
            ? string.Create(CultureInfo.InvariantCulture, $"Load {processingLoad * 100:0}%")
            : "Load –";
        BufferText = running
            ? string.Create(CultureInfo.InvariantCulture, $"Buffer {bufferedMilliseconds:0} ms")
            : "Buffer –";
    }
}

using Muted.App.Infrastructure;
using Muted.Core.Settings;

namespace Muted.App.ViewModels;

/// <summary>Session statistics and the lifetime totals stored in the settings.</summary>
internal sealed partial class MainViewModel
{
    private const int LifetimeSaveIntervalTicks = 150; // 30 seconds of 200 ms feature ticks
    private string _sessionText = "Start audio to collect session statistics.";
    private LifetimeStatistics _lifetime = new();
    private SessionStatistics _lifetimeCounted;
    private bool _lifetimeNewSession;
    private int _lifetimeTicks;

    public string SessionText { get => _sessionText; private set => SetProperty(ref _sessionText, value); }

    public RelayCommand ResetLifetimeCommand { get; private set; } = null!;

    public string LifetimeText
    {
        get
        {
            var total = _lifetime;
            if (total.Seconds < 1)
            {
                return "No audio processed yet.";
            }

            var since = total.Since is { } date ? $" since {date.LocalDateTime:d MMM yyyy}" : string.Empty;
            return $"In total{since}: {FormatDuration(total.Seconds)} cleaned over {total.Sessions} session(s), " +
                $"{FormatDuration(total.SpeakingSeconds)} of it speaking, {total.AverageNoiseReductionDb:0.0} dB of noise removed on average.";
        }
    }

    private void UpdateSessionText()
    {
        var stats = _engine.Session;
        SessionText = $"{(IsRunning ? "Current" : "Last")} audio session: {TimeSpan.FromSeconds(stats.Seconds):hh\\:mm\\:ss} · " +
            $"speaking (unmuted): {TimeSpan.FromSeconds(stats.SpeakingSeconds):hh\\:mm\\:ss}\n" +
            $"Limiter events: {stats.LimiterEvents} · average noise reduction: {stats.AverageNoiseReductionDb:0.0} dB · input peak: {FormatDecibels(stats.PeakInput)}";
    }

    private void FoldLifetime(bool save)
    {
        var session = _engine.Session;
        if (session.Seconds > _lifetimeCounted.Seconds)
        {
            _lifetime = _lifetime.Add(session, _lifetimeCounted, _lifetimeNewSession, DateTimeOffset.Now);
            _lifetimeNewSession = false;
            OnPropertyChanged(nameof(LifetimeText));
            if (save)
            {
                QueueSave();
            }
        }

        _lifetimeCounted = session;
    }

    private void StatisticsTick()
    {
        if (++_lifetimeTicks >= LifetimeSaveIntervalTicks)
        {
            _lifetimeTicks = 0;
            FoldLifetime(save: true);
        }
    }

    private void StatisticsEngineChanged()
    {
        if (IsRunning)
        {
            _lifetimeCounted = default;
            _lifetimeNewSession = true;
            _lifetimeTicks = 0;
        }
        else
        {
            FoldLifetime(save: true);
        }
    }

    private void ResetLifetime()
    {
        _lifetime = new LifetimeStatistics();
        _lifetimeCounted = _engine.Session;
        OnPropertyChanged(nameof(LifetimeText));
        QueueSave();
    }

    private static string FormatDuration(double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours} h {time.Minutes} min" : $"{time.Minutes} min";
    }

    private void InitializeStatistics()
    {
        ResetLifetimeCommand = new RelayCommand(ResetLifetime);
    }

    private void LoadStatisticsSettings(AppSettings settings)
    {
        _lifetime = settings.Lifetime;
        OnPropertyChanged(nameof(LifetimeText));
    }

    private AppSettings WithStatisticsSettings(AppSettings settings) => settings with
    {
        Lifetime = _lifetime
    };
}

using Muted.Core.Audio;

namespace Muted.Core.Settings;

/// <summary>Totals over every audio run, kept in the settings file. Numbers only, never audio.</summary>
public sealed record LifetimeStatistics
{
    public double Seconds { get; init; }
    public double SpeakingSeconds { get; init; }
    public long Sessions { get; init; }

    /// <summary>Sum of seconds times average reduction, so runs of any length average fairly.</summary>
    public double NoiseReductionDbSeconds { get; init; }
    public DateTimeOffset? Since { get; init; }

    public double AverageNoiseReductionDb => Seconds <= 0 ? 0 : NoiseReductionDbSeconds / Seconds;

    /// <summary>Folds in the part of a run that is new since <paramref name="alreadyCounted"/>.</summary>
    public LifetimeStatistics Add(SessionStatistics session, SessionStatistics alreadyCounted, bool newSession, DateTimeOffset now)
    {
        var seconds = Math.Max(0, session.Seconds - alreadyCounted.Seconds);
        var speaking = Math.Max(0, session.SpeakingSeconds - alreadyCounted.SpeakingSeconds);
        var reductionTotal = session.AverageNoiseReductionDb * session.Seconds;
        var countedReduction = alreadyCounted.AverageNoiseReductionDb * alreadyCounted.Seconds;
        return Normalize() with
        {
            Seconds = Seconds + seconds,
            SpeakingSeconds = SpeakingSeconds + speaking,
            Sessions = Sessions + (newSession ? 1 : 0),
            NoiseReductionDbSeconds = NoiseReductionDbSeconds + Math.Max(0, reductionTotal - countedReduction),
            Since = Since ?? now
        };
    }

    public LifetimeStatistics Normalize() => this with
    {
        Seconds = Finite(Seconds),
        SpeakingSeconds = Math.Min(Finite(SpeakingSeconds), Finite(Seconds)),
        Sessions = Math.Max(0, Sessions),
        NoiseReductionDbSeconds = Finite(NoiseReductionDbSeconds)
    };

    private static double Finite(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;
}

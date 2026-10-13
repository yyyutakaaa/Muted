namespace Muted.Core.Audio;

public readonly record struct SessionStatistics(double Seconds, double SpeakingSeconds,
    long LimiterEvents, double AverageNoiseReductionDb, float PeakInput);

/// <summary>Updated by the audio thread, so minimized windows do not lose statistics.</summary>
public sealed class SessionAccumulator
{
    private long _frames, _speaking, _limiterEvents;
    private double _reductionSum;
    private float _peak;
    private bool _limiting;
    public void Add(float voice, bool muted, float reduction, float limiterReduction, float peak)
    {
        if (voice >= 0.55f && !muted) Interlocked.Increment(ref _speaking);
        var limiting = limiterReduction > 0.1f;
        if (limiting && !_limiting) Interlocked.Increment(ref _limiterEvents);
        _limiting = limiting;
        Volatile.Write(ref _reductionSum, _reductionSum + Math.Max(0, reduction));
        Volatile.Write(ref _peak, Math.Max(_peak, peak));
        Interlocked.Increment(ref _frames);
    }
    public SessionStatistics Snapshot()
    {
        var frames = Interlocked.Read(ref _frames);
        return new(frames * .01, Interlocked.Read(ref _speaking) * .01,
            Interlocked.Read(ref _limiterEvents), frames == 0 ? 0 : Volatile.Read(ref _reductionSum) / frames,
            Volatile.Read(ref _peak));
    }
}

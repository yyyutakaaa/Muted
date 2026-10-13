namespace Muted.Core.Audio;

public readonly record struct AudioFeedback(bool Speaking, bool RemindMute, bool Unhealthy, bool NotifyHealth);

/// <summary>Background feedback with hysteresis, startup grace, and independent notification cooldowns.</summary>
public sealed class AudioFeedbackTracker
{
    private double _lastTick, _started, _speechUntil, _dropUntil, _mutedSpeech, _unhealthySeconds;
    private double _nextReminder, _nextHealth;
    private long _drops;

    public void Start(double now)
    {
        _lastTick = _started = now;
        _speechUntil = _dropUntil = _mutedSpeech = _unhealthySeconds = 0;
        _drops = 0;
    }

    public AudioFeedback Update(double now, AudioMetrics metrics, bool manuallyMuted, bool effectivelyMuted,
        bool remindersEnabled, bool healthEnabled)
    {
        var elapsed = Math.Clamp(now - _lastTick, 0, .5);
        _lastTick = now;
        var speech = metrics.VoiceProbability >= .55f && metrics.InputPeak > .005f;
        if (speech) _speechUntil = now + .4;
        _mutedSpeech = remindersEnabled && manuallyMuted && speech ? _mutedSpeech + elapsed : 0;
        var remind = _mutedSpeech >= 2 && now >= _nextReminder;
        if (remind) { _nextReminder = now + 60; _mutedSpeech = 0; }

        var drops = metrics.DroppedInputSamples + metrics.DroppedOutputSamples + metrics.OutputUnderrunSamples;
        if (drops > _drops && now - _started >= 2) _dropUntil = now + 2;
        _drops = drops;
        var unhealthy = now - _started >= 2 &&
            (metrics.ProcessingLoad >= .85f || metrics.BufferedMilliseconds >= 80 || now < _dropUntil);
        _unhealthySeconds = unhealthy ? _unhealthySeconds + elapsed : 0;
        var showHealth = healthEnabled && _unhealthySeconds >= 1;
        var notify = showHealth && now >= _nextHealth;
        if (notify) _nextHealth = now + 60;
        return new(!effectivelyMuted && now < _speechUntil, remind, showHealth, notify);
    }
}

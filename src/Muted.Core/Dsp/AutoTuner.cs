namespace Muted.Core.Dsp;

/// <summary>One small correction proposed by <see cref="AutoTuner"/>.</summary>
public readonly record struct AutoTuneStep(float InputGain, float VoiceThreshold, string Reason);

/// <summary>
/// Keeps the microphone boost and the voice gate right while you talk, for example
/// when you lean back or a fan starts. It only ever moves in small, slow steps and
/// stays within a fixed distance of the starting point, so nothing changes audibly
/// mid-sentence. It never touches the shout guard or the voice character.
/// </summary>
public sealed class AutoTuner
{
    /// <summary>The furthest the boost may wander from where it started.</summary>
    public const float MaximumGainDriftDb = 6f;

    /// <summary>The furthest the gate may wander from where it started.</summary>
    public const float MaximumThresholdDrift = 0.2f;

    private const double EvaluationSeconds = 8.0;
    private const long MinimumNewSpeechFrames = 300;
    private const float GainDeadbandDb = 2f;
    private const float MaximumGainStepDb = 1.5f;
    private const float ThresholdDeadband = 0.04f;
    private const float MaximumThresholdStep = 0.05f;

    private float _baselineGainDb;
    private float _baselineThreshold;
    private long _lastSpeechFrames;
    private double _lastEvaluation;
    private bool _started;

    public bool IsStarted => _started;

    /// <summary>Starts from the given settings; the tuner stays within its bounds around them.</summary>
    public void Reset(float baselineGain, float baselineThreshold, long speechFrames, double nowSeconds)
    {
        _baselineGainDb = GainToDb(baselineGain);
        _baselineThreshold = baselineThreshold;
        _lastSpeechFrames = speechFrames;
        _lastEvaluation = nowSeconds;
        _started = true;
    }

    public void Stop() => _started = false;

    /// <summary>
    /// Call regularly. Returns a step only when there is enough new speech, enough time
    /// has passed, and the current settings are noticeably off.
    /// </summary>
    public AutoTuneStep? Update(
        double nowSeconds,
        float speechLevelDb,
        float quietProbabilityP95,
        long speechFrames,
        float currentGain,
        float currentThreshold)
    {
        if (!_started)
        {
            return null;
        }

        // A restarted engine counts from zero again.
        if (speechFrames < _lastSpeechFrames)
        {
            _lastSpeechFrames = speechFrames;
        }

        if (nowSeconds - _lastEvaluation < EvaluationSeconds)
        {
            return null;
        }

        if (speechFrames - _lastSpeechFrames < MinimumNewSpeechFrames || speechLevelDb <= -90f)
        {
            return null;
        }

        _lastEvaluation = nowSeconds;
        _lastSpeechFrames = speechFrames;

        var gainDb = GainToDb(currentGain);
        var newGainDb = gainDb;
        var reasons = new List<string>(2);

        var error = VoiceAnalyzer.TargetSpeechDb - (speechLevelDb + gainDb);
        if (MathF.Abs(error) > GainDeadbandDb)
        {
            var step = Math.Clamp(error * 0.5f, -MaximumGainStepDb, MaximumGainStepDb);
            newGainDb = Math.Clamp(
                gainDb + step,
                MathF.Max(_baselineGainDb - MaximumGainDriftDb, VoiceAnalyzer.MinimumGainDb),
                MathF.Min(_baselineGainDb + MaximumGainDriftDb, VoiceAnalyzer.MaximumGainDb));
            if (MathF.Abs(newGainDb - gainDb) >= 0.1f)
            {
                reasons.Add(newGainDb > gainDb
                    ? $"boost {newGainDb - gainDb:+0.0} dB, you sound quieter"
                    : $"boost {newGainDb - gainDb:0.0} dB, you sound louder");
            }
            else
            {
                newGainDb = gainDb;
            }
        }

        var newThreshold = currentThreshold;
        var desired = Math.Clamp(quietProbabilityP95 + 0.12f, 0.3f, 0.85f);
        if (MathF.Abs(desired - currentThreshold) > ThresholdDeadband)
        {
            newThreshold = Math.Clamp(
                currentThreshold + Math.Clamp(desired - currentThreshold, -MaximumThresholdStep, MaximumThresholdStep),
                MathF.Max(_baselineThreshold - MaximumThresholdDrift, 0.05f),
                MathF.Min(_baselineThreshold + MaximumThresholdDrift, 0.99f));
            if (MathF.Abs(newThreshold - currentThreshold) >= 0.005f)
            {
                reasons.Add(newThreshold > currentThreshold
                    ? "gate stricter, the room got noisier"
                    : "gate looser, the room got quieter");
            }
            else
            {
                newThreshold = currentThreshold;
            }
        }

        if (reasons.Count == 0)
        {
            return null;
        }

        var reason = string.Join(", ", reasons);
        return new AutoTuneStep(DbToGain(newGainDb), newThreshold, char.ToUpperInvariant(reason[0]) + reason[1..]);
    }

    private static float GainToDb(float gain) => 20f * MathF.Log10(MathF.Max(gain, 1e-4f));

    private static float DbToGain(float db) => MathF.Pow(10f, db / 20f);
}

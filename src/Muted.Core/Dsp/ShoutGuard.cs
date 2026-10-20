namespace Muted.Core.Dsp;

public enum ShoutGuardMode
{
    Silence = 0,
    Duck = 1,
}

/// <summary>
/// Silences (or ducks) the output when the input peaks above a threshold.
/// Analyze sees the input lookAheadSamples before Apply sees the processed version of it,
/// so the gain can close before a loud sample arrives instead of after.
/// </summary>
/// <remarks>
/// Internally the guard tracks a "closure" from 0 (unity gain) to 1 (fully attenuated).
/// Analyze writes, per early sample position, the closure that position needs: 1 for a loud
/// sample and a raised-cosine ramp over the attackSamples before it. Apply reads those values
/// when the same position reaches it, adds hold and release, and turns closure into gain.
/// </remarks>
public sealed class ShoutGuard
{
    public const float MinimumThresholdDb = -30f;
    public const float MaximumThresholdDb = 0f;
    public const float DuckDepthDb = -20f;

    public const int MaximumFrameLength = 4096;
    public const int MaximumLookAheadSamples = 4096;

    private const float DisableRampMilliseconds = 5f;
    private const float ModeSmoothingMilliseconds = 5f;

    private static readonly float DuckFloor = MathF.Pow(10f, DuckDepthDb / 20f);

    private readonly int _lookAheadSamples;
    private readonly int _attackSamples;
    private readonly int _holdSamples;
    private readonly int _releaseSamples;
    private readonly float _maxAttackStep;
    private readonly float _disableStep;
    private readonly float _modeCoefficient;

    // Required closure per early sample position, indexed by position % length.
    private readonly float[] _required;
    // Raised-cosine attack shape: _attackRamp[k] is the closure k samples into the attack.
    private readonly float[] _attackRamp;

    private long _written;        // early positions written by Analyze so far
    private long _applyPosition;  // early position of the next sample Apply processes
    private long _lastLoud;       // early position of the last loud sample

    private float _closure;
    private int _holdRemaining;
    private bool _releasing;
    private float _releaseFrom;
    private int _releaseIndex;
    private float _floor;
    private bool _floorInitialised;
    private bool _engaged;
    private long _triggerCount;

    public ShoutGuard(
        int sampleRate,
        int lookAheadSamples,
        int holdMilliseconds = 400,
        float attackMilliseconds = 1.5f,
        float releaseMilliseconds = 150f)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 8_000);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sampleRate, 768_000);
        ArgumentOutOfRangeException.ThrowIfNegative(lookAheadSamples);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(lookAheadSamples, MaximumLookAheadSamples);
        ArgumentOutOfRangeException.ThrowIfNegative(holdMilliseconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(holdMilliseconds, 10_000);
        if (!float.IsFinite(attackMilliseconds) || attackMilliseconds <= 0f || attackMilliseconds > 50f)
        {
            throw new ArgumentOutOfRangeException(nameof(attackMilliseconds), attackMilliseconds, "Must be in (0, 50] ms.");
        }

        if (!float.IsFinite(releaseMilliseconds) || releaseMilliseconds <= 0f || releaseMilliseconds > 10_000f)
        {
            throw new ArgumentOutOfRangeException(nameof(releaseMilliseconds), releaseMilliseconds, "Must be in (0, 10000] ms.");
        }

        _lookAheadSamples = lookAheadSamples;
        _attackSamples = Math.Max(1, (int)MathF.Round(attackMilliseconds * sampleRate / 1000f));
        _holdSamples = (int)Math.Round(holdMilliseconds * (double)sampleRate / 1000.0);
        _releaseSamples = Math.Max(1, (int)MathF.Round(releaseMilliseconds * sampleRate / 1000f));

        // Largest per-sample step of the raised-cosine ramp, plus a hair for rounding.
        _maxAttackStep = MathF.PI / (2f * _attackSamples) * 1.001f;
        _disableStep = 1f / Math.Max(1f, DisableRampMilliseconds * sampleRate / 1000f);
        _modeCoefficient = 1f - MathF.Exp(-1f / (ModeSmoothingMilliseconds * sampleRate / 1000f));

        _attackRamp = new float[_attackSamples + 1];
        for (var k = 0; k <= _attackSamples; k++)
        {
            _attackRamp[k] = 0.5f - 0.5f * MathF.Cos(MathF.PI * k / _attackSamples);
        }

        _attackRamp[_attackSamples] = 1f;

        _required = new float[lookAheadSamples + MaximumFrameLength + _attackSamples + 1];
        Reset();
    }

    /// <summary>True while gain is below unity because of a shout.</summary>
    public bool IsEngaged => _closure > 0f;

    /// <summary>Distinct shout events since construction or Reset.</summary>
    public long TriggerCount => _triggerCount;

    public void Analyze(ReadOnlySpan<float> earlyFrame, float thresholdDb, bool enabled)
    {
        if (earlyFrame.Length > MaximumFrameLength)
        {
            throw new ArgumentException($"Frames longer than {MaximumFrameLength} samples are not supported.", nameof(earlyFrame));
        }

        if (float.IsNaN(thresholdDb))
        {
            thresholdDb = MaximumThresholdDb;
        }

        thresholdDb = Math.Clamp(thresholdDb, MinimumThresholdDb, MaximumThresholdDb);
        var threshold = MathF.Pow(10f, thresholdDb / 20f);
        var length = _required.Length;

        for (var index = 0; index < earlyFrame.Length; index++)
        {
            var position = _written + index;
            var loud = enabled && MathF.Abs(earlyFrame[index]) > threshold;
            _required[(int)(position % length)] = loud ? 1f : 0f;
            if (!loud)
            {
                continue;
            }

            // Ramp in over the attackSamples before this sample. Positions Apply has already
            // passed are gone (only happens when lookahead < attack), and positions after the
            // previous loud sample are already covered by its hold.
            var rampStart = Math.Max(
                Math.Max(position - _attackSamples, 0),
                Math.Max(_lastLoud + 1, _applyPosition));
            for (var p = rampStart; p < position; p++)
            {
                ref var slot = ref _required[(int)(p % length)];
                var ramp = _attackRamp[(int)(_attackSamples - (position - p))];
                if (ramp > slot)
                {
                    slot = ramp;
                }
            }

            _lastLoud = position;
        }

        _written += earlyFrame.Length;
    }

    public void Apply(Span<float> samples, ShoutGuardMode mode, bool enabled)
    {
        var targetFloor = mode == ShoutGuardMode.Duck ? DuckFloor : 0f;
        if (!_floorInitialised || _closure <= 0f)
        {
            _floor = targetFloor;
            _floorInitialised = true;
        }

        for (var index = 0; index < samples.Length; index++)
        {
            var required = ReadRequired(_applyPosition + index);

            if (!enabled)
            {
                _holdRemaining = 0;
                _releasing = false;
                if (_closure <= 0f)
                {
                    continue;
                }

                _closure = Math.Max(0f, _closure - _disableStep);
            }
            else
            {
                StepClosure(required);
            }

            if (_closure <= 0f)
            {
                _engaged = false;
                continue;
            }

            if (!_engaged)
            {
                _engaged = true;
                _triggerCount++;
            }

            _floor += (targetFloor - _floor) * _modeCoefficient;
            samples[index] *= 1f - _closure * (1f - _floor);
        }

        if (_closure <= 0f)
        {
            _engaged = false;
        }

        _applyPosition += samples.Length;
    }

    public void Reset()
    {
        Array.Clear(_required);
        _written = 0;
        _applyPosition = -_lookAheadSamples;
        _lastLoud = long.MinValue / 2;
        _closure = 0f;
        _holdRemaining = 0;
        _releasing = false;
        _releaseFrom = 0f;
        _releaseIndex = 0;
        _floor = 0f;
        _floorInitialised = false;
        _engaged = false;
        _triggerCount = 0;
    }

    private void StepClosure(float required)
    {
        float target;
        if (required >= 1f)
        {
            _holdRemaining = _holdSamples;
            _releasing = false;
            target = 1f;
        }
        else if (_holdRemaining > 0)
        {
            _holdRemaining--;
            _releasing = false;
            target = 1f;
        }
        else if (_closure > 0f || required > 0f)
        {
            if (!_releasing)
            {
                _releasing = true;
                _releaseFrom = _closure;
                _releaseIndex = 0;
            }

            _releaseIndex++;
            var released = _releaseIndex >= _releaseSamples
                ? 0f
                : _releaseFrom * (0.5f + 0.5f * MathF.Cos(MathF.PI * _releaseIndex / _releaseSamples));
            target = Math.Max(released, required);
        }
        else
        {
            _releasing = false;
            target = 0f;
        }

        // Closing is slew-limited to the attack ramp's own slope, so it follows the ramp
        // exactly when there is enough lookahead and never clicks when there isn't.
        _closure = target > _closure ? Math.Min(target, _closure + _maxAttackStep) : target;
    }

    private float ReadRequired(long position)
    {
        if (position < 0 || position >= _written || position < _written - _required.Length)
        {
            return 0f;
        }

        return _required[(int)(position % _required.Length)];
    }
}

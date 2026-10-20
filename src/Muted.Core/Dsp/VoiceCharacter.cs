namespace Muted.Core.Dsp;

/// <summary>
/// "Warm intimate radio voice": low-shelf warmth, a proximity-effect bump, a smooth
/// top with a split-band de-esser, gentle RMS compression and a touch of 2x
/// oversampled tube-like saturation. Every stage is unity when its amount is 0.
/// </summary>
/// <remarks>
/// While enabled the chain has a fixed latency of <c>15</c> samples (the linear-phase
/// oversampling filters). Bypass is a bit-exact passthrough with no latency.
/// </remarks>
public sealed class VoiceCharacter
{
    private const int MaxChunk = 4096;

    // Halfband FIR used for both up- and downsampling around the waveshaper.
    private const int FirTaps = 31;
    private const int FirHistory = FirTaps - 1;
    private const int SaturationLatency = FirHistory / 2;

    private const float WarmthFrequency = 150f;
    private const float WarmthMaxDb = 6f;
    private const float ProximityFrequency = 220f;
    private const float ProximityQ = 0.9f;
    private const float ProximityMaxDb = 4f;
    private const float PresenceDipFrequency = 3500f;
    private const float PresenceDipQ = 1.0f;
    private const float PresenceDipMaxDb = -2f;
    private const float SmoothShelfFrequency = 7000f;
    private const float SmoothShelfMaxDb = -6f;

    private const float DeEssSplitFrequency = 4500f;
    private const float DeEssSidechainCenter = 6700f; // geometric centre of 5–9 kHz
    private const float DeEssSidechainQ = 1.68f;      // ~4 kHz bandwidth
    private const float DeEssThresholdDb = -9f;       // sibilant band vs. full band
    private const float DeEssSlope = 1.5f;
    private const float DeEssMaxReductionDb = 10f;
    private const float DeEssFloorPower = 1e-6f;      // -60 dBFS: ignore near-silence

    private const float CompKneeDb = 6f;
    private const float CompMakeupGateLowDb = -55f;
    private const float CompMakeupGateHighDb = -45f;

    private const float SaturationMaxMix = 0.35f;
    private const float SaturationMaxDrive = 3f;      // extra linear drive at amount 1
    private const float SaturationMaxBias = 0.3f;     // asymmetry at amount 1

    private static readonly float[] HalfbandKernel = BuildHalfband();

    private readonly int _sampleRate;
    private readonly float _paramSmoothingSeconds = 0.02f;

    private readonly Biquad _warmth = new();
    private readonly Biquad _proximity = new();
    private readonly Biquad _presenceDip = new();
    private readonly Biquad _smoothShelf = new();
    private readonly Biquad _deEssSplit = new();
    private readonly Biquad _deEssSidechain = new();

    private readonly float[] _dry = new float[MaxChunk];
    private readonly float[] _upBuffer = new float[FirHistory + (2 * MaxChunk)];
    private readonly float[] _shapeBuffer = new float[FirHistory + (2 * MaxChunk)];
    private readonly float[] _delayBuffer = new float[SaturationLatency + MaxChunk];

    // Envelope coefficients.
    private readonly float _deEssDetectCoef;
    private readonly float _deEssAttackCoef;
    private readonly float _deEssReleaseCoef;
    private readonly float _compDetectCoef;
    private readonly float _compAttackCoef;
    private readonly float _compReleaseCoef;
    private readonly float _gateAttackCoef;
    private readonly float _gateReleaseCoef;
    private readonly float _dcCoef;

    // Smoothed amounts (what the previous frame ended on).
    private float _warmthAmount;
    private float _proximityAmount;
    private float _smoothnessAmount;
    private float _compressionAmount;
    private float _saturationAmount;

    // Dynamic state.
    private float _sibPower;
    private float _fullPower;
    private float _deEssReductionDb;
    private float _compPower;
    private float _compGainDb;
    private float _makeupGate;
    private float _dcIn;
    private float _dcOut;

    private bool _active;

    public VoiceCharacter(int sampleRate)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        _sampleRate = sampleRate;
        _deEssDetectCoef = TimeCoef(0.0015f);
        _deEssAttackCoef = TimeCoef(0.001f);
        _deEssReleaseCoef = TimeCoef(0.060f);
        _compDetectCoef = TimeCoef(0.010f);
        _compAttackCoef = TimeCoef(0.008f);
        _compReleaseCoef = TimeCoef(0.150f);
        _gateAttackCoef = TimeCoef(0.030f);
        _gateReleaseCoef = TimeCoef(0.200f);
        _dcCoef = MathF.Exp(-2f * MathF.PI * 10f / sampleRate);

        Reset();
    }

    /// <summary>Strongest compressor gain reduction (dB, before make-up) of the last frame.</summary>
    public float CompressorReductionDb { get; private set; }

    /// <summary>Strongest de-esser high-band reduction (dB) of the last frame.</summary>
    public float DeEsserReductionDb { get; private set; }

    public void Process(
        Span<float> samples,
        float warmth,
        float proximity,
        float smoothness,
        float compression,
        float saturation,
        bool enabled)
    {
        if (!enabled && !_active)
        {
            CompressorReductionDb = 0f;
            DeEsserReductionDb = 0f;
            return;
        }

        warmth = Sanitize(warmth);
        proximity = Sanitize(proximity);
        smoothness = Sanitize(smoothness);
        compression = Sanitize(compression);
        saturation = Sanitize(saturation);

        var startingUp = enabled && !_active;
        if (startingUp)
        {
            // Coming out of bypass: start clean on the requested settings and fade in.
            ClearState();
            _warmthAmount = warmth;
            _proximityAmount = proximity;
            _smoothnessAmount = smoothness;
            _compressionAmount = compression;
            _saturationAmount = saturation;
            UpdateFilters(immediate: true, rampLength: 1);
        }

        CompressorReductionDb = 0f;
        DeEsserReductionDb = 0f;

        // Fade direction for an enable/disable transition: wet weight goes from..to.
        var fadeFrom = startingUp ? 0f : 1f;
        var fadeTo = enabled ? 1f : 0f;
        var total = samples.Length;

        for (var offset = 0; offset < total; offset += MaxChunk)
        {
            var count = Math.Min(MaxChunk, total - offset);
            var chunk = samples.Slice(offset, count);
            var chunkFrom = fadeFrom + ((fadeTo - fadeFrom) * offset / total);
            var chunkTo = fadeFrom + ((fadeTo - fadeFrom) * (offset + count) / total);
            ProcessChunk(chunk, warmth, proximity, smoothness, compression, saturation, chunkFrom, chunkTo);
        }

        if (!enabled)
        {
            // Faded out to dry: from now on we are a plain passthrough.
            _active = false;
            ClearState();
        }
        else
        {
            _active = true;
        }
    }

    public void Reset()
    {
        _active = false;
        ClearState();
        _warmthAmount = 0f;
        _proximityAmount = 0f;
        _smoothnessAmount = 0f;
        _compressionAmount = 0f;
        _saturationAmount = 0f;
        UpdateFilters(immediate: true, rampLength: 1);
        CompressorReductionDb = 0f;
        DeEsserReductionDb = 0f;
    }

    private void ProcessChunk(
        Span<float> samples,
        float warmth,
        float proximity,
        float smoothness,
        float compression,
        float saturation,
        float wetFrom,
        float wetTo)
    {
        var n = samples.Length;
        var needsFade = wetFrom != 1f || wetTo != 1f;
        if (needsFade)
        {
            samples.CopyTo(_dry);
        }

        // Per-frame parameter smoothing, then per-sample interpolation inside the frame.
        var k = 1f - MathF.Exp(-n / (_paramSmoothingSeconds * _sampleRate));
        var prevSmooth = _smoothnessAmount;
        var prevComp = _compressionAmount;
        var prevSat = _saturationAmount;
        _warmthAmount = SmoothToward(_warmthAmount, warmth, k);
        _proximityAmount = SmoothToward(_proximityAmount, proximity, k);
        _smoothnessAmount = SmoothToward(_smoothnessAmount, smoothness, k);
        _compressionAmount = SmoothToward(_compressionAmount, compression, k);
        _saturationAmount = SmoothToward(_saturationAmount, saturation, k);
        UpdateFilters(immediate: false, rampLength: n);

        var invN = 1f / n;
        var maxComp = 0f;
        var maxDeEss = 0f;

        // Stages 1–4, sample by sample.
        for (var i = 0; i < n; i++)
        {
            var t = (i + 1) * invN;
            var x = samples[i];
            if (!float.IsFinite(x))
            {
                x = 0f;
            }

            // (1) warmth, (2) proximity bump + presence dip, (3a) smooth top shelf.
            x = _warmth.Tick(x);
            x = _proximity.Tick(x);
            x = _presenceDip.Tick(x);
            x = _smoothShelf.Tick(x);

            // (3b) split-band de-esser.
            var high = _deEssSplit.Tick(x);
            var side = _deEssSidechain.Tick(x);
            _sibPower = (_deEssDetectCoef * _sibPower) + ((1f - _deEssDetectCoef) * side * side);
            _fullPower = (_deEssDetectCoef * _fullPower) + ((1f - _deEssDetectCoef) * x * x);

            var smoothAmount = prevSmooth + ((_smoothnessAmount - prevSmooth) * t);
            var maxReduction = DeEssMaxReductionDb * smoothAmount;
            var targetReduction = 0f;
            if (maxReduction > 0f && _fullPower > DeEssFloorPower)
            {
                var ratioDb = 10f * MathF.Log10((_sibPower + 1e-12f) / (_fullPower + 1e-12f));
                targetReduction = Math.Clamp((ratioDb - DeEssThresholdDb) * DeEssSlope, 0f, maxReduction);
            }

            var deEssCoef = targetReduction > _deEssReductionDb ? _deEssAttackCoef : _deEssReleaseCoef;
            _deEssReductionDb = targetReduction + (deEssCoef * (_deEssReductionDb - targetReduction));
            if (_deEssReductionDb > 1e-4f)
            {
                var highGain = DbToGain(-_deEssReductionDb);
                x -= (1f - highGain) * high;
                maxDeEss = MathF.Max(maxDeEss, _deEssReductionDb);
            }

            // (4) feed-forward RMS compressor with soft knee and gated make-up.
            _compPower = (_compDetectCoef * _compPower) + ((1f - _compDetectCoef) * x * x);
            var compAmount = prevComp + ((_compressionAmount - prevComp) * t);
            if (compAmount > 0f || _compGainDb < -1e-4f)
            {
                var levelDb = 10f * MathF.Log10(_compPower + 1e-12f);
                var threshold = -18f - (12f * compAmount);
                var ratio = 1.5f + (2f * compAmount);
                var staticDb = StaticGainDb(levelDb, threshold, ratio);
                var compCoef = staticDb < _compGainDb ? _compAttackCoef : _compReleaseCoef;
                _compGainDb = staticDb + (compCoef * (_compGainDb - staticDb));

                var gateTarget = Math.Clamp(
                    (levelDb - CompMakeupGateLowDb) / (CompMakeupGateHighDb - CompMakeupGateLowDb), 0f, 1f);
                var gateCoef = gateTarget > _makeupGate ? _gateAttackCoef : _gateReleaseCoef;
                _makeupGate = gateTarget + (gateCoef * (_makeupGate - gateTarget));

                var makeupDb = -0.5f * StaticGainDb(-18f, threshold, ratio) * _makeupGate;

                // Fade the whole stage in over the first 10% so amount 0 is exactly unity.
                var presence = MathF.Min(1f, compAmount * 10f);
                var gainDb = (_compGainDb + makeupDb) * presence;
                x *= DbToGain(gainDb);
                maxComp = MathF.Max(maxComp, -_compGainDb * presence);
            }

            samples[i] = x;
        }

        // (5) 2x oversampled asymmetric saturation, latency-matched dry path.
        Saturate(samples, prevSat, _saturationAmount);

        for (var i = 0; i < n; i++)
        {
            var y = samples[i];
            if (needsFade)
            {
                var wet = wetFrom + ((wetTo - wetFrom) * (i + 1) * invN);
                y = (y * wet) + (_dry[i] * (1f - wet));
            }

            samples[i] = float.IsFinite(y) ? Math.Clamp(y, -1f, 1f) : 0f;
        }

        CompressorReductionDb = MathF.Max(CompressorReductionDb, maxComp);
        DeEsserReductionDb = MathF.Max(DeEsserReductionDb, maxDeEss);
        FlushState();
    }

    private void Saturate(Span<float> samples, float fromAmount, float toAmount)
    {
        var n = samples.Length;
        var up = _upBuffer;
        var shaped = _shapeBuffer;
        var delay = _delayBuffer;
        var h = HalfbandKernel;

        // Zero-stuff into the upsampling buffer (gain 2 restores level) and fill the dry delay.
        for (var i = 0; i < n; i++)
        {
            up[FirHistory + (2 * i)] = 2f * samples[i];
            up[FirHistory + (2 * i) + 1] = 0f;
            delay[SaturationLatency + i] = samples[i];
        }

        var upCount = 2 * n;
        var invUp = 1f / upCount;
        for (var i = 0; i < upCount; i++)
        {
            var v = 0f;
            for (var tap = 0; tap < FirTaps; tap++)
            {
                v += h[tap] * up[i + tap];
            }

            var amount = fromAmount + ((toAmount - fromAmount) * (i + 1) * invUp);
            var drive = 1f + (SaturationMaxDrive * amount);
            var bias = SaturationMaxBias * amount;
            var tanhBias = MathF.Tanh(bias);
            var slope = drive * (1f - (tanhBias * tanhBias));
            shaped[FirHistory + i] = (MathF.Tanh((drive * v) + bias) - tanhBias) / slope;
        }

        var invN = 1f / n;
        for (var j = 0; j < n; j++)
        {
            var w = 0f;
            var baseIndex = 2 * j;
            for (var tap = 0; tap < FirTaps; tap++)
            {
                w += h[tap] * shaped[baseIndex + tap];
            }

            var dryDelayed = delay[j];

            // Only the distortion residue is DC-blocked, so the clean voice keeps its lows.
            var residue = w - dryDelayed;
            var blocked = residue - _dcIn + (_dcCoef * _dcOut);
            _dcIn = residue;
            _dcOut = blocked;

            var amount = fromAmount + ((toAmount - fromAmount) * (j + 1) * invN);
            samples[j] = dryDelayed + (SaturationMaxMix * amount * blocked);
        }

        // Keep the filter and delay tails for the next frame.
        Array.Copy(up, upCount, up, 0, FirHistory);
        Array.Copy(shaped, upCount, shaped, 0, FirHistory);
        Array.Copy(delay, n, delay, 0, SaturationLatency);
    }

    private void UpdateFilters(bool immediate, int rampLength)
    {
        var fs = _sampleRate;
        _warmth.SetLowShelf(fs, WarmthFrequency, WarmthMaxDb * _warmthAmount, rampLength, immediate);
        _proximity.SetPeaking(fs, ProximityFrequency, ProximityQ, ProximityMaxDb * _proximityAmount, rampLength, immediate);
        _presenceDip.SetPeaking(fs, PresenceDipFrequency, PresenceDipQ, PresenceDipMaxDb * _proximityAmount, rampLength, immediate);
        _smoothShelf.SetHighShelf(fs, SmoothShelfFrequency, SmoothShelfMaxDb * _smoothnessAmount, rampLength, immediate);
        _deEssSplit.SetHighPass(fs, DeEssSplitFrequency, rampLength, immediate);
        _deEssSidechain.SetBandPass(fs, DeEssSidechainCenter, DeEssSidechainQ, rampLength, immediate);
    }

    private static float StaticGainDb(float levelDb, float threshold, float ratio)
    {
        var over = levelDb - threshold;
        var slope = (1f / ratio) - 1f;
        if (2f * over < -CompKneeDb)
        {
            return 0f;
        }

        if (2f * over > CompKneeDb)
        {
            return slope * over;
        }

        var x = over + (CompKneeDb / 2f);
        return slope * x * x / (2f * CompKneeDb);
    }

    private void ClearState()
    {
        _warmth.ClearState();
        _proximity.ClearState();
        _presenceDip.ClearState();
        _smoothShelf.ClearState();
        _deEssSplit.ClearState();
        _deEssSidechain.ClearState();
        Array.Clear(_upBuffer);
        Array.Clear(_shapeBuffer);
        Array.Clear(_delayBuffer);
        _sibPower = 0f;
        _fullPower = 0f;
        _deEssReductionDb = 0f;
        _compPower = 0f;
        _compGainDb = 0f;
        _makeupGate = 0f;
        _dcIn = 0f;
        _dcOut = 0f;
    }

    private void FlushState()
    {
        _warmth.Flush();
        _proximity.Flush();
        _presenceDip.Flush();
        _smoothShelf.Flush();
        _deEssSplit.Flush();
        _deEssSidechain.Flush();
        _sibPower = Flush(_sibPower);
        _fullPower = Flush(_fullPower);
        _deEssReductionDb = Flush(_deEssReductionDb);
        _compPower = Flush(_compPower);
        _compGainDb = Flush(_compGainDb);
        _makeupGate = Flush(_makeupGate);
        _dcIn = Flush(_dcIn);
        _dcOut = Flush(_dcOut);

        for (var i = 0; i < FirHistory; i++)
        {
            _upBuffer[i] = Flush(_upBuffer[i]);
            _shapeBuffer[i] = Flush(_shapeBuffer[i]);
        }

        for (var i = 0; i < SaturationLatency; i++)
        {
            _delayBuffer[i] = Flush(_delayBuffer[i]);
        }
    }

    private float TimeCoef(float seconds) => MathF.Exp(-1f / (seconds * _sampleRate));

    private static float SmoothToward(float current, float target, float k)
    {
        var next = current + ((target - current) * k);
        return MathF.Abs(target - next) < 1e-4f ? target : next;
    }

    private static float Sanitize(float amount) =>
        float.IsFinite(amount) ? Math.Clamp(amount, 0f, 1f) : 0f;

    private static float DbToGain(float db) => MathF.Exp(db * 0.11512925f); // ln(10)/20

    private static float Flush(float value) =>
        float.IsSubnormal(value) || !float.IsFinite(value) ? 0f : value;

    private static float[] BuildHalfband()
    {
        // Blackman-windowed sinc at a quarter of the oversampled rate (= original Nyquist).
        var kernel = new float[FirTaps];
        var center = (FirTaps - 1) / 2.0;
        var sum = 0.0;
        var values = new double[FirTaps];
        for (var i = 0; i < FirTaps; i++)
        {
            var m = i - center;
            var sinc = m == 0 ? 0.5 : Math.Sin(Math.PI * 0.5 * m) / (Math.PI * m);
            var window = 0.42 - (0.5 * Math.Cos(2.0 * Math.PI * i / (FirTaps - 1)))
                + (0.08 * Math.Cos(4.0 * Math.PI * i / (FirTaps - 1)));
            values[i] = sinc * window;
            sum += values[i];
        }

        for (var i = 0; i < FirTaps; i++)
        {
            kernel[i] = (float)(values[i] / sum);
        }

        return kernel;
    }

    /// <summary>
    /// RBJ cookbook biquad (direct form I, double precision) whose coefficients ramp
    /// linearly to a new target across one frame. Linear interpolation of (a1, a2) stays
    /// inside the stability triangle, so the ramp is always stable.
    /// </summary>
    private sealed class Biquad
    {
        private double _b0 = 1.0;
        private double _b1;
        private double _b2;
        private double _a1;
        private double _a2;
        private double _tb0 = 1.0;
        private double _tb1;
        private double _tb2;
        private double _ta1;
        private double _ta2;
        private double _db0;
        private double _db1;
        private double _db2;
        private double _da1;
        private double _da2;
        private int _rampRemaining;
        private double _x1;
        private double _x2;
        private double _y1;
        private double _y2;

        public float Tick(float input)
        {
            double x0 = input;
            var y0 = (_b0 * x0) + (_b1 * _x1) + (_b2 * _x2) - (_a1 * _y1) - (_a2 * _y2);
            _x2 = _x1;
            _x1 = x0;
            _y2 = _y1;
            _y1 = y0;

            if (_rampRemaining > 0)
            {
                if (--_rampRemaining == 0)
                {
                    _b0 = _tb0;
                    _b1 = _tb1;
                    _b2 = _tb2;
                    _a1 = _ta1;
                    _a2 = _ta2;
                }
                else
                {
                    _b0 += _db0;
                    _b1 += _db1;
                    _b2 += _db2;
                    _a1 += _da1;
                    _a2 += _da2;
                }
            }

            return (float)y0;
        }

        public void SetLowShelf(int fs, float frequency, float gainDb, int ramp, bool immediate)
        {
            var (cos, sin) = Omega(fs, frequency);
            var a = Math.Pow(10.0, gainDb / 40.0);
            var alpha = sin / 2.0 * Math.Sqrt(2.0); // shelf slope S = 1
            var twoSqrtAAlpha = 2.0 * Math.Sqrt(a) * alpha;
            var a0 = (a + 1) + ((a - 1) * cos) + twoSqrtAAlpha;
            SetTarget(
                a * ((a + 1) - ((a - 1) * cos) + twoSqrtAAlpha) / a0,
                2 * a * ((a - 1) - ((a + 1) * cos)) / a0,
                a * ((a + 1) - ((a - 1) * cos) - twoSqrtAAlpha) / a0,
                -2 * ((a - 1) + ((a + 1) * cos)) / a0,
                ((a + 1) + ((a - 1) * cos) - twoSqrtAAlpha) / a0,
                ramp,
                immediate);
        }

        public void SetHighShelf(int fs, float frequency, float gainDb, int ramp, bool immediate)
        {
            var (cos, sin) = Omega(fs, frequency);
            var a = Math.Pow(10.0, gainDb / 40.0);
            var alpha = sin / 2.0 * Math.Sqrt(2.0);
            var twoSqrtAAlpha = 2.0 * Math.Sqrt(a) * alpha;
            var a0 = (a + 1) - ((a - 1) * cos) + twoSqrtAAlpha;
            SetTarget(
                a * ((a + 1) + ((a - 1) * cos) + twoSqrtAAlpha) / a0,
                -2 * a * ((a - 1) + ((a + 1) * cos)) / a0,
                a * ((a + 1) + ((a - 1) * cos) - twoSqrtAAlpha) / a0,
                2 * ((a - 1) - ((a + 1) * cos)) / a0,
                ((a + 1) - ((a - 1) * cos) - twoSqrtAAlpha) / a0,
                ramp,
                immediate);
        }

        public void SetPeaking(int fs, float frequency, float q, float gainDb, int ramp, bool immediate)
        {
            var (cos, sin) = Omega(fs, frequency);
            var a = Math.Pow(10.0, gainDb / 40.0);
            var alpha = sin / (2.0 * q);
            var a0 = 1 + (alpha / a);
            SetTarget(
                (1 + (alpha * a)) / a0,
                -2 * cos / a0,
                (1 - (alpha * a)) / a0,
                -2 * cos / a0,
                (1 - (alpha / a)) / a0,
                ramp,
                immediate);
        }

        public void SetHighPass(int fs, float frequency, int ramp, bool immediate)
        {
            var (cos, sin) = Omega(fs, frequency);
            var alpha = sin / (2.0 * 0.70710678);
            var a0 = 1 + alpha;
            SetTarget(
                (1 + cos) / 2 / a0,
                -(1 + cos) / a0,
                (1 + cos) / 2 / a0,
                -2 * cos / a0,
                (1 - alpha) / a0,
                ramp,
                immediate);
        }

        public void SetBandPass(int fs, float frequency, float q, int ramp, bool immediate)
        {
            var (cos, sin) = Omega(fs, frequency);
            var alpha = sin / (2.0 * q);
            var a0 = 1 + alpha;
            SetTarget(alpha / a0, 0, -alpha / a0, -2 * cos / a0, (1 - alpha) / a0, ramp, immediate);
        }

        public void ClearState()
        {
            _x1 = 0;
            _x2 = 0;
            _y1 = 0;
            _y2 = 0;
        }

        public void Flush()
        {
            _x1 = FlushD(_x1);
            _x2 = FlushD(_x2);
            _y1 = FlushD(_y1);
            _y2 = FlushD(_y2);
        }

        private void SetTarget(double b0, double b1, double b2, double a1, double a2, int ramp, bool immediate)
        {
            if (b0 == _tb0 && b1 == _tb1 && b2 == _tb2 && a1 == _ta1 && a2 == _ta2 && !immediate)
            {
                return;
            }

            _tb0 = b0;
            _tb1 = b1;
            _tb2 = b2;
            _ta1 = a1;
            _ta2 = a2;

            if (immediate || ramp <= 1)
            {
                _b0 = b0;
                _b1 = b1;
                _b2 = b2;
                _a1 = a1;
                _a2 = a2;
                _rampRemaining = 0;
                return;
            }

            _db0 = (b0 - _b0) / ramp;
            _db1 = (b1 - _b1) / ramp;
            _db2 = (b2 - _b2) / ramp;
            _da1 = (a1 - _a1) / ramp;
            _da2 = (a2 - _a2) / ramp;
            _rampRemaining = ramp;
        }

        private static (double Cos, double Sin) Omega(int fs, float frequency)
        {
            // Keep every corner safely below Nyquist on low sample rates.
            var f = Math.Min(frequency, 0.45 * fs);
            var omega = 2.0 * Math.PI * f / fs;
            return (Math.Cos(omega), Math.Sin(omega));
        }

        private static double FlushD(double value) =>
            !double.IsFinite(value) || Math.Abs(value) < 1e-30 ? 0.0 : value;
    }
}

namespace Muted.Core.Dsp;

/// <summary>
/// Cancels the sound of your own speakers out of the microphone, so Muted is usable
/// without a headset. RNNoise removes noise; it has no idea what your speakers are
/// playing, and this does.
/// </summary>
/// <remarks>
/// A partitioned frequency-domain adaptive filter (the multidelay structure): the
/// loopback of what the speakers play is the reference, and the filter learns the
/// path from that reference to what the microphone hears. Twenty partitions of 10 ms
/// cover a 200 ms tail, which is enough for the render buffer, the speakers and the
/// trip through the room.
///
/// Two things keep it stable. A Geigel detector freezes adaptation while you talk
/// over the far end, because a filter that adapts to your own voice destroys it. And
/// a broadband residual suppressor ducks whatever the linear filter could not model,
/// which is mostly speaker distortion, while leaving your own speech alone.
/// </remarks>
public sealed class EchoCanceller
{
    public const int FrameSize = 480;
    public const int TailMilliseconds = PartitionCount * 10;

    private const int FftSize = 1024;
    private const int Overlap = FftSize - FrameSize;
    private const int PartitionCount = 20;
    private const float StepSize = 0.8f;
    private const float PowerSmoothing = 0.85f;
    private const float ReferenceFloor = 1e-5f;
    private const int DoubleTalkHangoverFrames = 30;
    private const float DoubleTalkThreshold = 3f;
    private const float TrustedErleDb = 6f;
    private const float ModelDecay = 0.999f;
    private const float Epsilon = 1e-9f;

    private readonly Fft _fft = new(FftSize);
    private readonly float[][] _filterReal = Create(PartitionCount, FftSize);
    private readonly float[][] _filterImaginary = Create(PartitionCount, FftSize);
    private readonly float[][] _blockReal = Create(PartitionCount, FftSize);
    private readonly float[][] _blockImaginary = Create(PartitionCount, FftSize);
    private readonly float[][] _blockPower = Create(PartitionCount, FftSize);
    private readonly float[] _summedPower = new float[FftSize];
    private readonly float[] _window = new float[FftSize];
    private readonly float[] _scratchReal = new float[FftSize];
    private readonly float[] _scratchImaginary = new float[FftSize];
    private readonly float[] _echoReal = new float[FftSize];
    private readonly float[] _echoImaginary = new float[FftSize];
    private readonly float[] _errorReal = new float[FftSize];
    private readonly float[] _errorImaginary = new float[FftSize];
    private readonly float[] _error = new float[FrameSize];
    private int _head = -1;
    private int _constrainIndex;
    private int _doubleTalkHold;
    private float _referencePeak;
    private float _gain = 1f;
    private float _erleDb;
    private float _modelQuality;
    private bool _running;

    /// <summary>How much echo is currently being removed, in dB.</summary>
    public float ErleDb => _erleDb;

    /// <summary>True while the far end is loud enough for the filter to learn from.</summary>
    public bool FarEndActive { get; private set; }

    /// <summary>True while near-end speech is holding adaptation still.</summary>
    public bool DoubleTalk => _doubleTalkHold > 0;

    /// <summary>
    /// Replaces <paramref name="microphone"/> with the echo-free signal.
    /// </summary>
    /// <param name="microphone">One frame of microphone audio, modified in place.</param>
    /// <param name="reference">The same frame's worth of what the speakers played.</param>
    /// <param name="enabled">False passes the microphone through untouched.</param>
    /// <param name="strength">0 leaves the residual alone, 1 ducks it hard.</param>
    public void Process(
        Span<float> microphone,
        ReadOnlySpan<float> reference,
        bool enabled,
        float strength)
    {
        if (microphone.Length != FrameSize || reference.Length != FrameSize)
        {
            throw new ArgumentException($"The echo canceller works on exactly {FrameSize} samples.");
        }

        if (!enabled)
        {
            if (_running)
            {
                Reset();
            }

            return;
        }

        _running = true;
        strength = Math.Clamp(strength, 0f, 1f);

        PushReference(reference);
        EstimateEcho();

        var microphoneEnergy = 0f;
        var echoEnergy = 0f;
        var errorEnergy = 0f;
        var microphonePeak = 0f;
        for (var index = 0; index < FrameSize; index++)
        {
            var input = microphone[index];
            var echo = _echoReal[Overlap + index];
            var error = input - echo;
            _error[index] = error;

            microphoneEnergy += input * input;
            echoEnergy += echo * echo;
            errorEnergy += error * error;
            microphonePeak = MathF.Max(microphonePeak, MathF.Abs(input));
        }

        UpdateDoubleTalk(microphonePeak, microphoneEnergy, echoEnergy);
        Adapt();
        Suppress(microphone, strength, echoEnergy, errorEnergy);
        UpdateErle(microphoneEnergy, errorEnergy);
    }

    public void Reset()
    {
        foreach (var partition in _filterReal)
        {
            Array.Clear(partition);
        }

        foreach (var partition in _filterImaginary)
        {
            Array.Clear(partition);
        }

        foreach (var partition in _blockReal)
        {
            Array.Clear(partition);
        }

        foreach (var partition in _blockImaginary)
        {
            Array.Clear(partition);
        }

        foreach (var partition in _blockPower)
        {
            Array.Clear(partition);
        }

        Array.Clear(_summedPower);
        Array.Clear(_window);
        _head = -1;
        _constrainIndex = 0;
        _doubleTalkHold = 0;
        _referencePeak = 0f;
        _gain = 1f;
        _erleDb = 0f;
        _modelQuality = 0f;
        FarEndActive = false;
        _running = false;
    }

    /// <summary>Slides the reference window and stores its spectrum in the delay line.</summary>
    private void PushReference(ReadOnlySpan<float> reference)
    {
        Array.Copy(_window, FrameSize, _window, 0, Overlap);
        reference.CopyTo(_window.AsSpan(Overlap));

        var peak = 0f;
        foreach (var sample in reference)
        {
            peak = MathF.Max(peak, MathF.Abs(sample));
        }

        // A decaying peak covers the whole tail, so the detector still knows the far
        // end is loud during a short gap between words.
        _referencePeak = MathF.Max(peak, _referencePeak * 0.95f);
        FarEndActive = _referencePeak > ReferenceFloor;

        _head = (_head + 1) % PartitionCount;
        _window.CopyTo(_scratchReal.AsSpan());
        Array.Clear(_scratchImaginary);
        _fft.Forward(_scratchReal, _scratchImaginary);

        var real = _blockReal[_head];
        var imaginary = _blockImaginary[_head];
        var power = _blockPower[_head];
        for (var bin = 0; bin < FftSize; bin++)
        {
            // Swap this slot's contribution to the running power sum for the new one.
            var newPower = (_scratchReal[bin] * _scratchReal[bin]) +
                (_scratchImaginary[bin] * _scratchImaginary[bin]);
            _summedPower[bin] = MathF.Max(0f, _summedPower[bin] - power[bin]) + newPower;
            power[bin] = newPower;
            real[bin] = _scratchReal[bin];
            imaginary[bin] = _scratchImaginary[bin];
        }
    }

    /// <summary>Runs the filter over the delay line and leaves the estimate in _echoReal.</summary>
    private void EstimateEcho()
    {
        Array.Clear(_echoReal);
        Array.Clear(_echoImaginary);

        for (var partition = 0; partition < PartitionCount; partition++)
        {
            var slot = Slot(partition);
            var xr = _blockReal[slot];
            var xi = _blockImaginary[slot];
            var wr = _filterReal[partition];
            var wi = _filterImaginary[partition];

            for (var bin = 0; bin < FftSize; bin++)
            {
                _echoReal[bin] += (wr[bin] * xr[bin]) - (wi[bin] * xi[bin]);
                _echoImaginary[bin] += (wr[bin] * xi[bin]) + (wi[bin] * xr[bin]);
            }
        }

        _fft.Inverse(_echoReal, _echoImaginary);
    }

    /// <summary>
    /// Decides whether you are talking over the far end, which is when adaptation has
    /// to stop: a filter that adapts to your own voice will subtract it.
    /// </summary>
    /// <remarks>
    /// Once the filter is worth trusting, the test is simply whether the microphone
    /// holds more than the filter can explain. Before that there is no model yet, so
    /// it falls back to Geigel: an echo path always loses energy, so a microphone
    /// louder than the far-end peak itself cannot be echo alone. A fixed Geigel
    /// threshold would be wrong for loud speakers close to the mic, which is exactly
    /// the case this feature exists for.
    /// </remarks>
    private void UpdateDoubleTalk(float microphonePeak, float microphoneEnergy, float echoEnergy)
    {
        var detected = _modelQuality > TrustedErleDb
            ? microphoneEnergy > (DoubleTalkThreshold * echoEnergy) + 1e-6f
            : FarEndActive && microphonePeak > 2f * _referencePeak;

        if (detected)
        {
            _doubleTalkHold = DoubleTalkHangoverFrames;
        }
        else if (_doubleTalkHold > 0)
        {
            _doubleTalkHold--;
        }
    }

    private void Adapt()
    {
        Array.Clear(_errorReal, 0, Overlap);
        _error.CopyTo(_errorReal.AsSpan(Overlap));
        Array.Clear(_errorImaginary);
        _fft.Forward(_errorReal, _errorImaginary);

        if (!FarEndActive || _doubleTalkHold > 0)
        {
            return;
        }

        for (var partition = 0; partition < PartitionCount; partition++)
        {
            var slot = Slot(partition);
            var xr = _blockReal[slot];
            var xi = _blockImaginary[slot];
            var wr = _filterReal[partition];
            var wi = _filterImaginary[partition];

            for (var bin = 0; bin < FftSize; bin++)
            {
                var step = StepSize / (_summedPower[bin] + Epsilon);
                var gradientReal = (xr[bin] * _errorReal[bin]) + (xi[bin] * _errorImaginary[bin]);
                var gradientImaginary = (xr[bin] * _errorImaginary[bin]) - (xi[bin] * _errorReal[bin]);
                wr[bin] += step * gradientReal;
                wi[bin] += step * gradientImaginary;
            }
        }

        ConstrainNextPartition();
    }

    /// <summary>
    /// Each partition may only hold <see cref="FrameSize"/> taps. Without this the
    /// update wraps around and the filter models an echo that arrives before its
    /// cause. One partition per frame keeps the cost at two extra transforms.
    /// </summary>
    private void ConstrainNextPartition()
    {
        var partition = _constrainIndex;
        _constrainIndex = (_constrainIndex + 1) % PartitionCount;

        var real = _filterReal[partition];
        var imaginary = _filterImaginary[partition];
        real.CopyTo(_scratchReal.AsSpan());
        imaginary.CopyTo(_scratchImaginary.AsSpan());
        _fft.Inverse(_scratchReal, _scratchImaginary);

        Array.Clear(_scratchReal, FrameSize, FftSize - FrameSize);
        Array.Clear(_scratchImaginary);
        _fft.Forward(_scratchReal, _scratchImaginary);

        _scratchReal.CopyTo(real.AsSpan());
        _scratchImaginary.CopyTo(imaginary.AsSpan());
    }

    /// <summary>
    /// Ducks what the linear filter could not model. During double talk the error is
    /// dominated by your own voice, the ratio stays near one, and nothing is ducked.
    /// </summary>
    private void Suppress(Span<float> output, float strength, float echoEnergy, float errorEnergy)
    {
        var leak = 0.05f + (0.45f * strength);
        var floor = 1f - (0.95f * strength);
        var target = Math.Clamp(
            (errorEnergy - (leak * echoEnergy)) / (errorEnergy + Epsilon),
            floor,
            1f);

        var start = _gain;
        var step = (target - start) / FrameSize;
        var gain = start;
        for (var index = 0; index < FrameSize; index++)
        {
            gain += step;
            output[index] = Math.Clamp(_error[index] * gain, -1f, 1f);
        }

        _gain = target;
    }

    private void UpdateErle(float microphoneEnergy, float errorEnergy)
    {
        if (microphoneEnergy <= 1e-7f)
        {
            _erleDb *= 0.98f;
            return;
        }

        var reduction = 10f * MathF.Log10((microphoneEnergy + Epsilon) / (errorEnergy + Epsilon));
        _erleDb = (_erleDb * 0.9f) + (Math.Clamp(reduction, 0f, 60f) * 0.1f);

        // Held separately from the reported figure, and only slowly given up. The
        // reported ERLE collapses the moment you start talking, and a detector that
        // believed it would decide the filter is worthless right when it matters and
        // start training on your own voice.
        _modelQuality = MathF.Max(_erleDb, _modelQuality * ModelDecay);
    }

    private int Slot(int partition) => ((_head - partition) % PartitionCount + PartitionCount) % PartitionCount;

    private static float[][] Create(int count, int size)
    {
        var arrays = new float[count][];
        for (var index = 0; index < count; index++)
        {
            arrays[index] = new float[size];
        }

        return arrays;
    }
}

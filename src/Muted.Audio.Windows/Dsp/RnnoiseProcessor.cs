namespace Muted.Audio.Windows.Dsp;

public sealed class RnnoiseProcessor : IDisposable
{
    public const int ExpectedFrameSize = 480;
    public const int SampleRate = 48_000;
    public const int AlgorithmicDelaySamples = ExpectedFrameSize * 2;
    private const float PcmScale = 32_768f;

    private readonly SafeRnnoiseHandle _state;
    private readonly float[] _scaledInput;
    private readonly float[] _scaledOutput;
    private int _disposed;

    public RnnoiseProcessor()
    {
        FrameSize = RnnoiseNative.rnnoise_get_frame_size();
        if (FrameSize != ExpectedFrameSize)
        {
            throw new NotSupportedException(
                $"Deze RNNoise-build gebruikt {FrameSize} samples per frame; Muted verwacht {ExpectedFrameSize}.");
        }

        _state = RnnoiseNative.rnnoise_create(IntPtr.Zero);
        if (_state.IsInvalid)
        {
            _state.Dispose();
            throw new InvalidOperationException("RNNoise could not initialize its denoise state.");
        }

        _scaledInput = new float[FrameSize];
        _scaledOutput = new float[FrameSize];
    }

    public int FrameSize { get; }

    public unsafe float Process(ReadOnlySpan<float> input, Span<float> output)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (input.Length != FrameSize || output.Length < FrameSize)
        {
            throw new ArgumentException($"RNNoise vereist exact {FrameSize} input- en outputsamples.");
        }

        for (var index = 0; index < FrameSize; index++)
        {
            _scaledInput[index] = Math.Clamp(input[index], -1f, 1f) * PcmScale;
        }

        float voiceProbability;
        fixed (float* inputPointer = _scaledInput)
        fixed (float* outputPointer = _scaledOutput)
        {
            voiceProbability = RnnoiseNative.rnnoise_process_frame(_state, outputPointer, inputPointer);
        }

        for (var index = 0; index < FrameSize; index++)
        {
            output[index] = Math.Clamp(_scaledOutput[index] / PcmScale, -1f, 1f);
        }

        return Math.Clamp(voiceProbability, 0f, 1f);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _state.Dispose();
        }
    }
}

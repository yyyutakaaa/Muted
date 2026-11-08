using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Muted.Core.Dsp;

namespace Muted.Audio.Windows.Dsp;

/// <summary>
/// DPDFNet (CEVA): DeepFilterNet2 with dual-path RNN blocks in the encoder. Two
/// variants run here, both at 48 kHz fullband, as a streaming ONNX graph that carries
/// its own state from frame to frame.
/// </summary>
/// <remarks>
/// The model works on 960-sample windows with a 480-sample hop, using a Vorbis window
/// and a causal (centred on the past) STFT. One extra hop of output staging brings its
/// delay up to the 1440 samples every Studio Voice model shares, so switching models
/// never shifts the timeline.
/// </remarks>
public sealed class DpdfNetProcessor : IDenoiser
{
    private const int WindowLength = 960;
    private const int HopSize = 480;
    private const int FrequencyBins = (WindowLength / 2) + 1;

    private static readonly string[] InputNames = ["spec", "state_in"];
    private static readonly string[] OutputNames = ["spec_e", "state_out"];

    private readonly InferenceSession _session;
    private readonly RunOptions _runOptions = new();

    // STFT and overlap-add.
    private readonly MixedRadixFft _fft = new(WindowLength);
    private readonly float[] _window = VorbisWindow(WindowLength);
    private readonly float[] _inputBuffer = new float[WindowLength];
    private int _inputCount;
    private readonly float[] _overlap = new float[WindowLength];
    private readonly float[] _spectrum = new float[WindowLength];
    private readonly float[] _frame = new float[WindowLength];
    private readonly float[] _stepRe = new float[FrequencyBins];
    private readonly float[] _stepIm = new float[FrequencyBins];

    // The ONNX graph's view of the same memory.
    private readonly float[] _specTensor = new float[FrequencyBins * 2];
    private readonly float[] _specOutTensor = new float[FrequencyBins * 2];
    private readonly float[][] _states;
    private readonly float[] _initialState;
    private readonly OrtValue[] _stateValues;
    private readonly OrtValue _specValue;
    private readonly OrtValue _specOutValue;
    private int _current;
    private int _disposed;

    // The output of a call was made from the input five calls earlier (the 2400-sample
    // delay), so six input frames are kept to compare each output with its own input.
    private readonly float[][] _history = CreateHistory(IDenoiser.LatencySamples / HopSize + 1);
    private int _historyPosition;

    public DpdfNetProcessor(string modelPath, string id, int threads = DenoiserSession.DefaultThreads)
    {
        Id = id;
        _session = DenoiserSession.Open(modelPath, threads);

        var metadata = _session.ModelMetadata?.CustomMetadataMap
            ?? throw new InvalidOperationException("The Studio Voice model is missing its metadata.");
        var stateSize = ReadInt(metadata, "state_size");
        var erbSize = ReadInt(metadata, "erb_norm_state_size");
        var specSize = ReadInt(metadata, "spec_norm_state_size");

        // The learned normalisation state is part of the trained model: starting from
        // zeros makes the first seconds sound wrong, so it comes from the metadata.
        _initialState = new float[stateSize];
        ReadVegetable(metadata, "erb_norm_init").CopyTo(_initialState, 0);
        ReadVegetable(metadata, "spec_norm_init").CopyTo(_initialState, erbSize);
        if (erbSize + specSize > stateSize)
        {
            throw new InvalidOperationException("The Studio Voice model states do not fit its state tensor.");
        }

        _states = [new float[stateSize], new float[stateSize]];
        _stateValues =
        [
            OrtValue.CreateTensorValueFromMemory(_states[0], [stateSize]),
            OrtValue.CreateTensorValueFromMemory(_states[1], [stateSize])
        ];
        _specValue = OrtValue.CreateTensorValueFromMemory(_specTensor, [1, 1, FrequencyBins, 2]);
        _specOutValue = OrtValue.CreateTensorValueFromMemory(_specOutTensor, [1, 1, FrequencyBins, 2]);

        Reset();
    }

    public string Id { get; }

    public float SpeechConfidence { get; private set; }

    /// <summary>The Vorbis window: w[n]^2 + w[n + hop]^2 == 1, so overlap-add needs no scaling.</summary>
    private static float[] VorbisWindow(int length)
    {
        var window = new float[length];
        var half = length / 2.0;
        for (var index = 0; index < length; index++)
        {
            var sine = Math.Sin(0.5 * Math.PI * (index + 0.5) / half);
            window[index] = (float)Math.Sin(0.5 * Math.PI * sine * sine);
        }

        return window;
    }

    private static float[][] CreateHistory(int frames)
    {
        var history = new float[frames][];
        for (var index = 0; index < frames; index++)
        {
            history[index] = new float[HopSize];
        }

        return history;
    }

    private static int ReadInt(IDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && int.TryParse(value, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"The Studio Voice model is missing metadata '{key}'.");

    private static float[] ReadVegetable(IDictionary<string, string> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value))
        {
            throw new InvalidOperationException($"The Studio Voice model is missing metadata '{key}'.");
        }

        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new float[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            result[index] = float.Parse(parts[index], System.Globalization.CultureInfo.InvariantCulture);
        }

        return result;
    }

    public void Process(ReadOnlySpan<float> input, Span<float> output)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (input.Length != HopSize || output.Length < HopSize)
        {
            throw new ArgumentException($"DPDFNet requires exactly {HopSize} input and output samples.");
        }

        // Remember this frame so the emitted one can be compared with what produced it.
        input.CopyTo(_history[_historyPosition]);
        output[..HopSize].Clear();

        for (var index = 0; index < HopSize; index++)
        {
            _inputBuffer[_inputCount + index] = Math.Clamp(input[index], -1f, 1f);
        }

        _inputCount += HopSize;
        if (_inputCount < WindowLength)
        {
            _historyPosition = (_historyPosition + 1) % _history.Length;
            SpeechConfidence = 0f;
            return;
        }

        // Analysis: the Vorbis window, then a real FFT of the whole 960 samples.
        for (var index = 0; index < WindowLength; index++)
        {
            _spectrum[index] = _inputBuffer[index] * _window[index];
        }

        _fft.ForwardReal(_spectrum, _stepRe, _stepIm);
        for (var bin = 0; bin < FrequencyBins; bin++)
        {
            _specTensor[bin * 2] = _stepRe[bin];
            _specTensor[(bin * 2) + 1] = _stepIm[bin];
        }

        var next = 1 - _current;
        _session.Run(
            _runOptions,
            InputNames,
            [_specValue, _stateValues[_current]],
            OutputNames,
            [_specOutValue, _stateValues[next]]);
        _current = next;

        for (var bin = 0; bin < FrequencyBins; bin++)
        {
            _stepRe[bin] = _specOutTensor[bin * 2];
            _stepIm[bin] = _specOutTensor[(bin * 2) + 1];
        }

        _fft.InverseReal(_stepRe, _stepIm, _frame);

        // Synthesis: window again and overlap-add. The window's squares sum to one, so
        // the first hop is final and the rest waits for the next frame.
        for (var index = 0; index < WindowLength; index++)
        {
            _overlap[index] += _frame[index] * _window[index];
        }

        for (var index = 0; index < HopSize; index++)
        {
            output[index] = Math.Clamp(_overlap[index], -1f, 1f);
        }

        for (var index = 0; index < HopSize; index++)
        {
            _overlap[index] = _overlap[index + HopSize];
            _overlap[index + HopSize] = 0f;
        }

        // Slide the input window by one hop.
        for (var index = 0; index < HopSize; index++)
        {
            _inputBuffer[index] = _inputBuffer[index + HopSize];
        }

        _inputCount = HopSize;
        UpdateSpeechConfidence(output);
        _historyPosition = (_historyPosition + 1) % _history.Length;
    }

    /// <summary>
    /// The model does not report a speech probability, but how much it removed says the
    /// same thing: next to nothing for a voice, a lot for room noise. The emitted frame
    /// is compared with the input it was made from, five calls ago, so the confidence
    /// lines up with the audio the voice gate is about to let through.
    /// </summary>
    private void UpdateSpeechConfidence(ReadOnlySpan<float> wet)
    {
        // The slot after the newest one holds the oldest frame: five calls back.
        var dry = _history[(_historyPosition + 1) % _history.Length];
        var dryRms = AudioMath.Rms(dry);
        var wetRms = AudioMath.Rms(wet);
        if (!float.IsFinite(dryRms) || dryRms < 1e-4f)
        {
            SpeechConfidence = 0f;
            return;
        }

        var reductionDb = 20f * MathF.Log10(dryRms / MathF.Max(wetRms, 1e-9f));
        SpeechConfidence = Math.Clamp((20f - reductionDb) / 18f, 0f, 1f);
    }

    public void Reset()
    {
        Array.Clear(_inputBuffer);
        Array.Clear(_overlap);
        foreach (var frame in _history)
        {
            Array.Clear(frame);
        }

        _inputCount = 0;
        _historyPosition = 0;
        _current = 0;

        // Both state buffers start from the model's trained normalisation values.
        _initialState.CopyTo(_states[0], 0);
        _initialState.CopyTo(_states[1], 0);
        SpeechConfidence = 0f;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stateValues[0].Dispose();
        _stateValues[1].Dispose();
        _specValue.Dispose();
        _specOutValue.Dispose();
        _runOptions.Dispose();
        _session.Dispose();
    }
}

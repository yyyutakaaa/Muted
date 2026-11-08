using Microsoft.ML.OnnxRuntime;

namespace Muted.Audio.Windows.Dsp;

/// <summary>
/// Studio Voice: DeepFilterNet3 as a streaming ONNX model. It takes the same 480-sample
/// frames at 48 kHz as RNNoise, removes noise and room echo, and carries its own state
/// from one frame to the next.
/// </summary>
/// <remarks>
/// Every buffer is allocated once and the model writes straight into it, so the audio
/// thread does not allocate while it runs.
/// </remarks>
public sealed class DeepFilterProcessor : IDenoiser
{
    public const int FrameSize = 480;
    public const int SampleRate = 48_000;

    /// <summary>The model's own output trails its input by three frames (30 ms), as the tests measure.</summary>
    public const int AlgorithmicDelaySamples = FrameSize * 3;

    public const string ModelFileName = "deepfilternet3.onnx";

    private const int StateSize = 45_304;

    private static readonly string[] InputNames = ["input_frame", "states", "atten_lim_db"];
    private static readonly string[] OutputNames = ["enhanced_audio_frame", "new_states", "lsnr"];

    private readonly InferenceSession _session;
    private readonly RunOptions _runOptions = new();
    private readonly float[] _input = new float[FrameSize];
    private readonly float[] _output = new float[FrameSize];
    private readonly float[][] _states = [new float[StateSize], new float[StateSize]];
    private readonly float[] _attenuationLimit = [0f];
    private readonly float[] _localSnr = new float[1];

    // DeepFilterNet3 trails its input by three frames. Delivering two calls behind adds
    // two more, matching DPDFNet so switching models never shifts the timeline.
    private readonly float[][] _stages = [new float[FrameSize], new float[FrameSize]];
    private int _stage;
    private readonly OrtValue _inputValue;
    private readonly OrtValue _outputValue;
    private readonly OrtValue[] _stateValues;
    private readonly OrtValue _limitValue;
    private readonly OrtValue _snrValue;
    private int _current;
    private int _disposed;

    public DeepFilterProcessor(string modelPath, int threads = DenoiserSession.DefaultThreads)
    {
        _session = DenoiserSession.Open(modelPath, threads);

        _inputValue = OrtValue.CreateTensorValueFromMemory(_input, [FrameSize]);
        _outputValue = OrtValue.CreateTensorValueFromMemory(_output, [FrameSize]);
        _stateValues =
        [
            OrtValue.CreateTensorValueFromMemory(_states[0], [StateSize]),
            OrtValue.CreateTensorValueFromMemory(_states[1], [StateSize])
        ];
        _limitValue = OrtValue.CreateTensorValueFromMemory(_attenuationLimit, []);
        _snrValue = OrtValue.CreateTensorValueFromMemory(_localSnr, [1]);
    }

    /// <summary>Where the installer and the build put the model, next to Muted.exe.</summary>
    public static string DefaultModelPath => Path.Combine(AppContext.BaseDirectory, "models", ModelFileName);

    public string Id => "deepfilternet3";

    /// <summary>The last frame's speech confidence, from the model's own signal-to-noise estimate.</summary>
    public float SpeechConfidence { get; private set; }

    /// <summary>
    /// Cleans one frame and delivers one, staged so every Studio Voice model reaches the
    /// same total delay.
    /// </summary>
    public void Process(ReadOnlySpan<float> input, Span<float> output)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (input.Length != FrameSize || output.Length < FrameSize)
        {
            throw new ArgumentException($"Studio Voice requires exactly {FrameSize} input and output samples.");
        }

        for (var index = 0; index < FrameSize; index++)
        {
            _input[index] = Math.Clamp(input[index], -1f, 1f);
        }

        // The state goes in from one buffer and comes out in the other; the next frame
        // reads where this one wrote.
        var next = 1 - _current;
        _session.Run(
            _runOptions,
            InputNames,
            [_inputValue, _stateValues[_current], _limitValue],
            OutputNames,
            [_outputValue, _stateValues[next], _snrValue]);
        _current = next;

        // Deliver the frame from two calls ago, then keep this one for later.
        _stages[_stage].AsSpan().CopyTo(output);
        for (var index = 0; index < FrameSize; index++)
        {
            var sample = _output[index];
            _stages[_stage][index] = float.IsFinite(sample) ? Math.Clamp(sample, -1f, 1f) : 0f;
        }

        _stage = (_stage + 1) % _stages.Length;

        // RNNoise's probability drives the gate for this model; keep the public
        // confidence in the same 0..1 shape the other models report.
        SpeechConfidence = float.IsFinite(_localSnr[0])
            ? Math.Clamp((_localSnr[0] + 5f) / 15f, 0f, 1f)
            : 0f;
    }

    /// <summary>The model's local signal-to-noise estimate for the last frame, in dB.</summary>
    public float LocalSnrDb => _localSnr[0];

    /// <summary>Forgets everything the model learned about the room so far.</summary>
    public void Reset()
    {
        Array.Clear(_states[0]);
        Array.Clear(_states[1]);
        foreach (var stage in _stages)
        {
            Array.Clear(stage);
        }

        _stage = 0;
        _current = 0;
        SpeechConfidence = 0f;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _inputValue.Dispose();
        _outputValue.Dispose();
        _stateValues[0].Dispose();
        _stateValues[1].Dispose();
        _limitValue.Dispose();
        _snrValue.Dispose();
        _runOptions.Dispose();
        _session.Dispose();
    }
}

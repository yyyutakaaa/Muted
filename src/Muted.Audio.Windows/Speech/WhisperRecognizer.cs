using System.Globalization;
using System.Text;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Muted.Core.Speech;

namespace Muted.Audio.Windows.Speech;

/// <summary>
/// Local speech recognition with OpenAI's Whisper tiny.en, in the sherpa-onnx export:
/// an encoder for 30 seconds of log-mel features and a decoder that is fed one token at
/// a time with its own key/value cache. Greedy decoding, no timestamps. Runs on the CPU
/// in the background; nothing leaves the PC. Not thread-safe: one caller at a time.
/// </summary>
public sealed class WhisperRecognizer : IDisposable
{
    private const int MaxTokens = 96;

    private readonly InferenceSession _encoder;
    private readonly InferenceSession _decoder;
    private readonly WhisperFeatures _features = new();
    private readonly byte[][] _tokens;
    private readonly long[] _prompt;
    private readonly int _endOfText;
    private readonly int _layers;
    private readonly int _textContext;
    private readonly int _state;

    public WhisperRecognizer()
    {
        _encoder = SpeechModels.Open(SpeechModels.WhisperEncoderFileName, threads: 2);
        try
        {
            _decoder = SpeechModels.Open(SpeechModels.WhisperDecoderFileName, threads: 2);
        }
        catch
        {
            _encoder.Dispose();
            throw;
        }

        var metadata = _encoder.ModelMetadata.CustomMetadataMap;
        int Read(string key) => int.Parse(metadata[key], CultureInfo.InvariantCulture);
        _endOfText = Read("eot");
        _layers = Read("n_text_layer");
        _textContext = Read("n_text_ctx");
        _state = Read("n_text_state");
        _prompt = [Read("sot"), Read("no_timestamps")];
        _tokens = LoadTokens(SpeechModels.PathOf(SpeechModels.WhisperTokensFileName));
    }

    /// <summary>What was said in <paramref name="audio16k"/>, up to its last 30 seconds.</summary>
    public string Transcribe(ReadOnlySpan<float> audio16k, CancellationToken cancellationToken = default)
    {
        var mel = new DenseTensor<float>(_features.Compute(audio16k), [1, WhisperFeatures.MelBands, WhisperFeatures.Frames]);
        DenseTensor<float> crossKeys;
        DenseTensor<float> crossValues;
        using (var encoded = _encoder.Run([NamedOnnxValue.CreateFromTensor("mel", mel)]))
        {
            crossKeys = encoded.First(value => value.Name == "n_layer_cross_k").AsTensor<float>().ToDenseTensor();
            crossValues = encoded.First(value => value.Name == "n_layer_cross_v").AsTensor<float>().ToDenseTensor();
        }

        var cacheShape = new[] { _layers, 1, _textContext, _state };
        var selfKeys = new DenseTensor<float>(cacheShape);
        var selfValues = new DenseTensor<float>(cacheShape);
        var output = new List<int>();
        long[] step = _prompt;
        long offset = 0;
        while (output.Count < MaxTokens && offset + step.Length < _textContext)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("tokens", new DenseTensor<long>(step, [1, step.Length])),
                NamedOnnxValue.CreateFromTensor("in_n_layer_self_k_cache", selfKeys),
                NamedOnnxValue.CreateFromTensor("in_n_layer_self_v_cache", selfValues),
                NamedOnnxValue.CreateFromTensor("n_layer_cross_k", crossKeys),
                NamedOnnxValue.CreateFromTensor("n_layer_cross_v", crossValues),
                NamedOnnxValue.CreateFromTensor("offset", new DenseTensor<long>(new[] { offset }, [1]))
            };

            int next;
            using (var decoded = _decoder.Run(inputs))
            {
                var logits = decoded.First(value => value.Name == "logits").AsTensor<float>();
                next = BestToken(logits, step.Length - 1);
                selfKeys = decoded.First(value => value.Name == "out_n_layer_self_k_cache").AsTensor<float>().ToDenseTensor();
                selfValues = decoded.First(value => value.Name == "out_n_layer_self_v_cache").AsTensor<float>().ToDenseTensor();
            }

            offset += step.Length;
            if (next == _endOfText)
            {
                break;
            }

            output.Add(next);
            step = [next];
        }

        return Decode(output).Trim();
    }

    /// <summary>The most likely next text token; special tokens other than the end are never picked.</summary>
    private int BestToken(Tensor<float> logits, int position)
    {
        var best = _endOfText;
        var bestScore = float.NegativeInfinity;
        for (var token = 0; token <= _endOfText; token++)
        {
            var score = logits[0, position, token];
            if (score > bestScore)
            {
                bestScore = score;
                best = token;
            }
        }

        return best;
    }

    private string Decode(IEnumerable<int> tokens)
    {
        var bytes = new List<byte>();
        foreach (var token in tokens)
        {
            if (token >= 0 && token < _tokens.Length)
            {
                bytes.AddRange(_tokens[token]);
            }
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    /// <summary>sherpa-onnx's token table: one "base64-bytes id" pair per line.</summary>
    private static byte[][] LoadTokens(string path)
    {
        var table = new Dictionary<int, byte[]>();
        foreach (var line in File.ReadLines(path))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                table[id] = Convert.FromBase64String(parts[0]);
            }
        }

        var tokens = new byte[table.Count == 0 ? 0 : table.Keys.Max() + 1][];
        for (var index = 0; index < tokens.Length; index++)
        {
            tokens[index] = table.TryGetValue(index, out var bytes) ? bytes : [];
        }

        return tokens;
    }

    public void Dispose()
    {
        _encoder.Dispose();
        _decoder.Dispose();
    }
}

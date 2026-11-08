using Microsoft.ML.OnnxRuntime;
using Muted.Audio.Windows.Dsp;

namespace Muted.Audio.Windows.Speech;

/// <summary>Where the speech models live and how ONNX Runtime opens them.</summary>
public static class SpeechModels
{
    public const string WhisperEncoderFileName = "whisper-tiny.en-encoder.onnx";
    public const string WhisperDecoderFileName = "whisper-tiny.en-decoder.onnx";
    public const string WhisperTokensFileName = "whisper-tiny.en-tokens.txt";
    public const string SpeakerFileName = "wespeaker-campplus.onnx";

    public static string PathOf(string fileName) => Path.Combine(DenoiserFactory.ModelsDirectory, fileName);

    public static bool WhisperAvailable =>
        File.Exists(PathOf(WhisperEncoderFileName)) &&
        File.Exists(PathOf(WhisperDecoderFileName)) &&
        File.Exists(PathOf(WhisperTokensFileName));

    public static bool SpeakerAvailable => File.Exists(PathOf(SpeakerFileName));

    /// <summary>
    /// These models run in the background, never on the audio thread, so they get a
    /// thread or two and a lower priority than the realtime work.
    /// </summary>
    internal static InferenceSession Open(string fileName, int threads)
    {
        var path = PathOf(fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("A speech model is missing.", path);
        }

        threads = Math.Clamp(threads, 1, Math.Max(1, Environment.ProcessorCount / 2));
        using var options = new SessionOptions
        {
            IntraOpNumThreads = threads,
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };
        options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
        return new InferenceSession(path, options);
    }
}

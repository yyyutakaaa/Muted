using Microsoft.ML.OnnxRuntime;

namespace Muted.Audio.Windows.Dsp;

/// <summary>How ONNX Runtime runs a Studio Voice model.</summary>
public static class DenoiserSession
{
    /// <summary>
    /// One thread, no spinning. These models process one small 10 ms frame at a time;
    /// splitting that over several cores costs more in hand-offs than it saves, and
    /// spinning workers burn whole cores between frames. Measured in the benchmark test.
    /// </summary>
    public const int DefaultThreads = 1;

    public static InferenceSession Open(string modelPath, int threads = DefaultThreads)
    {
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("The Studio Voice model is missing.", modelPath);
        }

        threads = Math.Clamp(threads, 1, Environment.ProcessorCount);
        using var options = new SessionOptions
        {
            IntraOpNumThreads = threads,
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };
        options.AddSessionConfigEntry("session.intra_op.allow_spinning", threads > 1 ? "1" : "0");
        return new InferenceSession(modelPath, options);
    }
}

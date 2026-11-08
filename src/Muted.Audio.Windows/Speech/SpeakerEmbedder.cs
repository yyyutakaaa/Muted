using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Muted.Core.Speech;

namespace Muted.Audio.Windows.Speech;

/// <summary>
/// Turns a stretch of 16 kHz speech into a 512-number voice embedding with WeSpeaker's
/// CAM++ model. Two stretches of the same voice point in nearly the same direction,
/// other voices clearly less so. Not thread-safe: one caller at a time.
/// </summary>
public sealed class SpeakerEmbedder : IDisposable
{
    /// <summary>Shorter than this and the embedding says more about the words than the voice.</summary>
    public const int MinimumSamples = SpeakerFeatures.SampleRate * 3 / 4;

    private readonly InferenceSession _session;
    private readonly SpeakerFeatures _features = new();
    private readonly string _inputName;

    public SpeakerEmbedder()
    {
        _session = SpeechModels.Open(SpeechModels.SpeakerFileName, threads: 1);
        _inputName = _session.InputMetadata.Keys.First();
    }

    /// <summary>The embedding of <paramref name="audio16k"/>, scaled to length 1.</summary>
    public float[] Embed(ReadOnlySpan<float> audio16k)
    {
        if (audio16k.Length < MinimumSamples)
        {
            throw new ArgumentException("Too little audio for a voice embedding.", nameof(audio16k));
        }

        var features = _features.Compute(audio16k);
        var frames = features.Length / SpeakerFeatures.Bands;
        var tensor = new DenseTensor<float>(features, [1, frames, SpeakerFeatures.Bands]);
        using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, tensor)]);
        var embedding = results.First().AsEnumerable<float>().ToArray();
        return new VoiceProfile(embedding).Embedding.ToArray();
    }

    public void Dispose() => _session.Dispose();
}

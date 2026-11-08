namespace Muted.Audio.Windows.Dsp;

/// <summary>
/// One Studio Voice model. Every model takes exactly 480 samples (10 ms at 48 kHz) in
/// and gives 480 out, with the same total delay, so Muted can swap models without the
/// sound jumping or the timing shifting.
/// </summary>
public interface IDenoiser : IDisposable
{
    /// <summary>
    /// The total delay every Studio Voice model delivers, in samples: five 10 ms frames.
    /// The models differ by nature (DeepFilterNet3 needs three frames, DPDFNet needs
    /// four), and staging makes up the rest, so switching never shifts the timeline.
    /// </summary>
    const int LatencySamples = 2_400;

    int FrameSize => 480;

    /// <summary>The model as Muted identifies it in settings.</summary>
    string Id { get; }

    /// <summary>
    /// Cleans one frame. <paramref name="input"/> is the microphone and
    /// <paramref name="output"/> receives the cleaned frame, delayed by
    /// <see cref="LatencySamples"/> like every other model.
    /// </summary>
    void Process(ReadOnlySpan<float> input, Span<float> output);

    /// <summary>How sure the model is that the last frame held a voice, 0 to 1.</summary>
    float SpeechConfidence { get; }

    /// <summary>Forgets the room. Switching models or recovering from a stuck filter uses this.</summary>
    void Reset();
}

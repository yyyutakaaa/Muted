namespace Muted.Core.Speech;

/// <summary>
/// What Muted remembers of your voice: one speaker embedding, scaled to length 1, so
/// comparing it with another embedding is a single dot product (cosine similarity).
/// Stored in the settings as Base64; it describes how a voice sounds, not what was said.
/// </summary>
public sealed class VoiceProfile
{
    private readonly float[] _embedding;

    public VoiceProfile(ReadOnlySpan<float> embedding)
    {
        if (embedding.Length == 0)
        {
            throw new ArgumentException("A voice profile needs an embedding.", nameof(embedding));
        }

        _embedding = Normalize(embedding);
    }

    public int Dimensions => _embedding.Length;

    public ReadOnlySpan<float> Embedding => _embedding;

    /// <summary>Cosine similarity, -1..1; NaN when the sizes differ.</summary>
    public float Similarity(ReadOnlySpan<float> embedding)
    {
        if (embedding.Length != _embedding.Length)
        {
            return float.NaN;
        }

        var dot = 0.0;
        var norm = 0.0;
        for (var index = 0; index < embedding.Length; index++)
        {
            dot += _embedding[index] * embedding[index];
            norm += embedding[index] * (double)embedding[index];
        }

        return norm <= 0 ? float.NaN : (float)(dot / Math.Sqrt(norm));
    }

    /// <summary>The average direction of several embeddings of the same voice.</summary>
    public static VoiceProfile Average(IReadOnlyList<float[]> embeddings)
    {
        if (embeddings.Count == 0)
        {
            throw new ArgumentException("At least one embedding is needed.", nameof(embeddings));
        }

        var sum = new float[embeddings[0].Length];
        foreach (var embedding in embeddings)
        {
            var normalized = Normalize(embedding);
            for (var index = 0; index < sum.Length; index++)
            {
                sum[index] += normalized[index];
            }
        }

        return new VoiceProfile(sum);
    }

    public string ToBase64()
    {
        var bytes = new byte[_embedding.Length * sizeof(float)];
        Buffer.BlockCopy(_embedding, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>Reads a stored profile; null when there is none or it is damaged.</summary>
    public static VoiceProfile? FromBase64(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(text);
            if (bytes.Length == 0 || bytes.Length % sizeof(float) != 0)
            {
                return null;
            }

            var embedding = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, embedding, 0, bytes.Length);
            foreach (var value in embedding)
            {
                if (!float.IsFinite(value))
                {
                    return null;
                }
            }

            return new VoiceProfile(embedding);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static float[] Normalize(ReadOnlySpan<float> embedding)
    {
        var norm = 0.0;
        foreach (var value in embedding)
        {
            norm += value * (double)value;
        }

        if (norm <= 0 || !double.IsFinite(norm))
        {
            throw new ArgumentException("The embedding has no direction.", nameof(embedding));
        }

        var scale = (float)(1.0 / Math.Sqrt(norm));
        var result = new float[embedding.Length];
        for (var index = 0; index < embedding.Length; index++)
        {
            result[index] = embedding[index] * scale;
        }

        return result;
    }
}

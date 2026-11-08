using System.IO;

namespace Muted.Audio.Windows.Tests;

/// <summary>
/// A clean 48 kHz mono speech sample, so model tests use something the models treat as
/// a voice. Synthetic buzzes are removed by both models, which makes them useless for
/// measuring what a model keeps.
/// </summary>
internal static class SpeechFixture
{
    private static float[]? _cached;

    public static string Path => System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures", "speech-48k-mono.wav");

    public static float[] Load()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        using var stream = File.OpenRead(Path);
        using var reader = new BinaryReader(stream);
        stream.Seek(12, SeekOrigin.Begin);
        while (stream.Position < stream.Length - 8)
        {
            var id = new string(reader.ReadChars(4));
            var size = reader.ReadInt32();
            if (id != "data")
            {
                stream.Seek(size, SeekOrigin.Current);
                continue;
            }

            var samples = new float[size / 2];
            for (var index = 0; index < samples.Length; index++)
            {
                samples[index] = reader.ReadInt16() / 32768f;
            }

            _cached = samples;
            return samples;
        }

        throw new InvalidDataException("The speech fixture has no data chunk.");
    }
}

namespace Muted.Core.Audio;

public sealed record AudioDeviceFormatInfo(int SampleRate, int Channels, int BitsPerSample)
{
    public string DisplayName => $"{SampleRate / 1000d:0.#} kHz, {Channels} ch, {BitsPerSample}-bit";
}

namespace Muted.Core.Audio;

/// <summary>Single producer, bounded recording. Copy only after sealing; never writes to disk.</summary>
public sealed class AudioClipBuffer
{
    private readonly float[] _samples;
    private int _count;
    private int _sealed;
    private int _writing;
    public AudioClipBuffer(int seconds)
    {
        if (seconds is < 1 or > 120) throw new ArgumentOutOfRangeException(nameof(seconds));
        _samples = new float[seconds * 48_000];
    }
    public bool Complete => Volatile.Read(ref _count) == _samples.Length;
    public void Write(ReadOnlySpan<float> frame)
    {
        Interlocked.Exchange(ref _writing, 1);
        try
        {
            if (Volatile.Read(ref _sealed) != 0) return;
            var count = _count;
            var length = Math.Min(frame.Length, _samples.Length - count);
            frame[..length].CopyTo(_samples.AsSpan(count));
            Volatile.Write(ref _count, count + length);
        }
        finally { Volatile.Write(ref _writing, 0); }
    }
    public float[] Finish()
    {
        Volatile.Write(ref _sealed, 1);
        var spinner = new SpinWait();
        while (Volatile.Read(ref _writing) != 0) spinner.SpinOnce();
        return _samples.AsSpan(0, Volatile.Read(ref _count)).ToArray();
    }
}

using System.Diagnostics;
using Muted.Core.Dsp;
using Muted.Core.Speech;

namespace Muted.Audio.Windows.Speech;

public enum SpeakerCheckStatus
{
    Off,
    Loading,
    Listening,
    Unavailable
}

/// <summary>
/// Checks, in the background, whether the voice in the microphone is yours. The audio
/// thread hands over every cleaned 10 ms frame with its speech probability, which costs
/// a copy and nothing else. A worker thread keeps the last second or so of speech,
/// turns it into a voice embedding twice a second, and publishes how similar it is to
/// your voice profile. The <see cref="SpeakerGate"/> on the audio thread reads that.
/// </summary>
public sealed class SpeakerVerifier : IDisposable
{
    private const int InputRate = 48_000;
    private const int FrameSize = 480;
    private const int FrameSize16k = FrameSize / Downsampler.Factor;
    private const float VoicedProbability = 0.5f;

    // About one second of speech gives a dependable embedding; checking every 0.4 s keeps
    // the decision current without costing more than a tenth of one core.
    private const int SpeechFrames = 110;
    private const int MinimumSpeechFrames = 80;
    private const int CheckEveryVoicedFrames = 40;

    // After two seconds of silence the next voice could be anyone: forget the old score
    // and the old speech, and let the sound through until the new voice is checked.
    private const int ForgetAfterSilentFrames = 200;

    private readonly FloatRingBuffer _samples = new(InputRate * 2);
    private readonly FloatRingBuffer _probabilities = new(256);
    private readonly Func<SpeakerEmbedder> _factory;
    private readonly Thread _worker;
    private readonly CancellationTokenSource _stop = new();
    private VoiceProfile? _profile;
    private int _enabled;
    private float _similarity = float.NaN;
    private volatile SpeakerCheckStatus _status = SpeakerCheckStatus.Off;
    private volatile string? _error;
    private long _checks;

    public SpeakerVerifier(Func<SpeakerEmbedder>? factory = null)
    {
        _factory = factory ?? (() => new SpeakerEmbedder());
        _worker = new Thread(Run)
        {
            IsBackground = true,
            Name = "Muted speaker check",
            Priority = ThreadPriority.BelowNormal
        };
        _worker.Start();
    }

    /// <summary>Similarity of the latest speech with your voice, -1..1, or NaN when there is no fresh answer.</summary>
    public float Similarity => Volatile.Read(ref _similarity);

    public SpeakerCheckStatus Status => _status;

    public string? Error => _error;

    /// <summary>How many embeddings were compared, for tests and diagnostics.</summary>
    public long Checks => Interlocked.Read(ref _checks);

    public bool IsEnabled => Volatile.Read(ref _enabled) != 0;

    /// <summary>Turns the check on with a profile, or off. Safe from any thread.</summary>
    public void Configure(bool enabled, VoiceProfile? profile)
    {
        Volatile.Write(ref _profile, profile);
        var on = enabled && profile is not null;
        Volatile.Write(ref _enabled, on ? 1 : 0);
        if (!on)
        {
            Volatile.Write(ref _similarity, float.NaN);
            if (_status != SpeakerCheckStatus.Unavailable)
            {
                _status = SpeakerCheckStatus.Off;
            }
        }
    }

    /// <summary>Called from the audio thread with one cleaned 48 kHz frame. Never blocks or allocates.</summary>
    public void Push(ReadOnlySpan<float> frame, float voiceProbability)
    {
        if (Volatile.Read(ref _enabled) == 0 || frame.Length != FrameSize)
        {
            return;
        }

        // Both buffers have room for the same stretch of time; when the worker falls
        // behind, a frame is dropped from both, so samples and probabilities stay paired.
        if (_samples.Capacity - _samples.Count < FrameSize || _probabilities.Capacity - _probabilities.Count < 1)
        {
            return;
        }

        _samples.Write(frame);
        Span<float> probability = stackalloc float[1];
        probability[0] = voiceProbability;
        _probabilities.Write(probability);
    }

    private void Run()
    {
        var token = _stop.Token;
        SpeakerEmbedder? embedder = null;
        var downsampler = new Downsampler();
        var frame = new float[FrameSize];
        var frame16k = new float[FrameSize16k + 1];
        var probability = new float[1];
        var speech = new float[SpeechFrames * FrameSize16k];
        var speechFrames = 0;
        var voicedSinceCheck = 0;
        var silentFrames = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (Volatile.Read(ref _enabled) == 0)
                {
                    // Discard is the reading side's own operation, so this stays safe
                    // while the audio thread might still be writing.
                    _samples.Discard(_samples.Count);
                    _probabilities.Discard(_probabilities.Count);
                    downsampler.Reset();
                    speechFrames = 0;
                    voicedSinceCheck = 0;
                    token.WaitHandle.WaitOne(100);
                    continue;
                }

                if (embedder is null)
                {
                    if (_status == SpeakerCheckStatus.Unavailable)
                    {
                        token.WaitHandle.WaitOne(250);
                        continue;
                    }

                    _status = SpeakerCheckStatus.Loading;
                    try
                    {
                        embedder = _factory();
                        _status = SpeakerCheckStatus.Listening;
                    }
                    catch (Exception exception)
                    {
                        _error = exception.Message;
                        _status = SpeakerCheckStatus.Unavailable;
                        continue;
                    }
                }

                if (_status != SpeakerCheckStatus.Listening)
                {
                    _status = SpeakerCheckStatus.Listening;
                }

                if (_probabilities.Count < 1 || _samples.Count < FrameSize)
                {
                    token.WaitHandle.WaitOne(20);
                    continue;
                }

                _probabilities.Read(probability);
                _samples.Read(frame);
                var written = downsampler.Process(frame, frame16k);
                if (probability[0] < VoicedProbability)
                {
                    if (++silentFrames >= ForgetAfterSilentFrames)
                    {
                        speechFrames = 0;
                        voicedSinceCheck = 0;
                        Volatile.Write(ref _similarity, float.NaN);
                    }

                    continue;
                }

                silentFrames = 0;
                AppendSpeech(speech, ref speechFrames, frame16k.AsSpan(0, Math.Min(written, FrameSize16k)));
                if (++voicedSinceCheck < CheckEveryVoicedFrames || speechFrames < MinimumSpeechFrames)
                {
                    continue;
                }

                voicedSinceCheck = 0;
                var profile = Volatile.Read(ref _profile);
                if (profile is null)
                {
                    continue;
                }

                var embedding = embedder.Embed(speech.AsSpan(0, speechFrames * FrameSize16k));
                Volatile.Write(ref _similarity, profile.Similarity(embedding));
                Interlocked.Increment(ref _checks);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The check is an extra: when it fails, the gate fails open and the
            // microphone keeps working.
            _error = exception.Message;
            _status = SpeakerCheckStatus.Unavailable;
            Volatile.Write(ref _similarity, float.NaN);
            Trace.TraceError("Speaker check stopped: {0}", exception);
        }
        finally
        {
            embedder?.Dispose();
        }
    }

    /// <summary>Keeps the most recent <see cref="SpeechFrames"/> frames of speech, oldest first.</summary>
    private static void AppendSpeech(float[] speech, ref int frames, ReadOnlySpan<float> frame16k)
    {
        if (frames == SpeechFrames)
        {
            Array.Copy(speech, FrameSize16k, speech, 0, speech.Length - FrameSize16k);
            frames--;
        }

        frame16k.CopyTo(speech.AsSpan(frames * FrameSize16k, FrameSize16k));
        frames++;
    }

    public void Dispose()
    {
        _stop.Cancel();
        _worker.Join(TimeSpan.FromSeconds(2));
        _stop.Dispose();
    }
}

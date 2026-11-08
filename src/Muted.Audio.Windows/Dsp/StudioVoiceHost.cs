using Muted.Core.Audio;

namespace Muted.Audio.Windows.Dsp;

/// <summary>
/// Owns the Studio Voice models: loads the one asked for in the background, lets it
/// learn the room next to the model that is still cleaning, then crossfades over to it.
/// The microphone never goes silent during a switch.
/// </summary>
/// <remarks>
/// Any thread may call <see cref="Request"/>. Only the audio thread calls
/// <see cref="Process"/> and <see cref="Reset"/>; it alone touches the model that
/// is cleaning, so models are never swapped out from under a running frame. Replaced
/// models are disposed on the thread pool, because closing an ONNX session can take
/// longer than a frame.
/// </remarks>
public sealed class StudioVoiceHost : IDisposable
{
    /// <summary>How long a new model runs unheard, so its first audible frame is settled.</summary>
    public const int WarmupFrames = 30;

    /// <summary>How long the old and the new model are crossfaded.</summary>
    public const int FadeFrames = 10;

    private const int Frame = 480;
    private const int NothingRequested = -1;

    private readonly Func<StudioModel, IDenoiser> _factory;
    private readonly float[] _incomingOutput = new float[Frame];

    // Shared between the loader and the audio thread. A loaded model and its name
    // travel together, so the audio thread never pairs one with the other's label.
    private Staged? _staged;
    private volatile int _requested = NothingRequested;
    private volatile int _lastLoaded = NothingRequested;
    private volatile int _activeModel = NothingRequested;
    private volatile string? _error;
    private int _loading;
    private long _loads;
    private int _disposed;

    // Audio thread only.
    private IDenoiser? _active;
    private IDenoiser? _warming;
    private volatile int _warmingModel = NothingRequested;
    private int _warmingFrames;

    public StudioVoiceHost(Func<StudioModel, IDenoiser>? factory = null)
    {
        _factory = factory ?? (model => DenoiserFactory.Create(model));
    }

    /// <summary>The model that is cleaning right now, or null while none has started.</summary>
    public StudioModel? ActiveModel => _activeModel == NothingRequested ? null : (StudioModel)_activeModel;

    /// <summary>Why the last model could not load or run, if it could not.</summary>
    public string? Error => _error;

    /// <summary>How many models were loaded in total; a switch is one load.</summary>
    public long Loads => Interlocked.Read(ref _loads);

    /// <summary>True while a requested model is loading or warming up next to the active one.</summary>
    public bool IsSwitching =>
        Volatile.Read(ref _loading) != 0 ||
        Volatile.Read(ref _staged) is not null ||
        _warmingModel != NothingRequested ||
        (_requested != NothingRequested && _requested != _activeModel && _error is null);

    /// <summary>Speech confidence of the frame just delivered, 0 to 1.</summary>
    public float SpeechConfidence { get; private set; }

    /// <summary>Asks for a model. Loading happens in the background; repeated calls are free.</summary>
    public void Request(StudioModel model)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var wanted = (int)model;
        if (_requested != wanted)
        {
            _requested = wanted;

            // A different model gets a fresh chance even if the last one failed.
            _error = null;
        }

        StartLoading();
    }

    private void StartLoading()
    {
        // The newest loaded model is the one that ends up cleaning, so it is the only
        // thing to compare with. A failed load counts too, so a broken model is not
        // retried in a loop.
        var wanted = _requested;
        if (wanted == NothingRequested || wanted == _lastLoaded)
        {
            return;
        }

        if (Interlocked.Exchange(ref _loading, 1) != 0)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                var model = (StudioModel)wanted;
                IDenoiser? processor = null;
                try
                {
                    processor = _factory(model);
                }
                catch (Exception exception)
                {
                    _error = exception.Message;
                }

                _lastLoaded = wanted;
                if (processor is null)
                {
                    return;
                }

                Interlocked.Increment(ref _loads);
                if (Volatile.Read(ref _disposed) != 0)
                {
                    processor.Dispose();
                    return;
                }

                // A model still waiting for the audio thread is replaced, not queued.
                DisposeLater(Interlocked.Exchange(ref _staged, new Staged(processor, wanted))?.Denoiser);
            }
            finally
            {
                Volatile.Write(ref _loading, 0);
            }

            // Something else may have been asked for while this one loaded.
            StartLoading();
        });
    }

    /// <summary>
    /// Cleans one frame on the audio thread. Returns false while no model can clean yet,
    /// so the caller keeps its own fallback running.
    /// </summary>
    public bool Process(ReadOnlySpan<float> input, Span<float> output)
    {
        TakeStagedModel();
        var active = _active;
        if (active is null)
        {
            SpeechConfidence = 0f;
            return false;
        }

        try
        {
            active.Process(input, output);
            SpeechConfidence = active.SpeechConfidence;

            var warming = _warming;
            if (warming is null)
            {
                return true;
            }

            // Both models see the same input and share one delay, so their outputs line
            // up sample for sample and a plain linear crossfade is seamless.
            warming.Process(input, _incomingOutput);
            _warmingFrames++;
            var fade = _warmingFrames - WarmupFrames;
            if (fade <= 0)
            {
                return true;
            }

            var start = (fade - 1) / (float)FadeFrames;
            var end = Math.Min(1f, fade / (float)FadeFrames);
            for (var index = 0; index < Frame; index++)
            {
                var weight = start + ((end - start) * (index + 1) / Frame);
                output[index] = (output[index] * (1f - weight)) + (_incomingOutput[index] * weight);
            }

            SpeechConfidence = (SpeechConfidence * (1f - end)) + (warming.SpeechConfidence * end);
            if (fade >= FadeFrames)
            {
                // The new model owns the sound from the next frame on.
                DisposeLater(_active);
                _active = warming;
                _activeModel = _warmingModel;
                _warming = null;
                _warmingModel = NothingRequested;
                _warmingFrames = 0;
            }

            return true;
        }
        catch (Exception exception)
        {
            // A model that fails mid-call stops; the caller falls back to RNNoise.
            _error = exception.Message;
            DisposeLater(_active);
            DisposeLater(_warming);
            _active = null;
            _warming = null;
            _activeModel = NothingRequested;
            _warmingModel = NothingRequested;
            SpeechConfidence = 0f;
            return false;
        }
    }

    /// <summary>Starts the active model over, as switching it off and on does. Audio thread only.</summary>
    public void Reset()
    {
        _active?.Reset();
        if (_warming is not null)
        {
            _warming.Reset();
            _warmingFrames = 0;
        }

        SpeechConfidence = 0f;
    }

    private void TakeStagedModel()
    {
        if (Volatile.Read(ref _staged) is null)
        {
            return;
        }

        var staged = Interlocked.Exchange(ref _staged, null);
        if (staged is null)
        {
            return;
        }

        var (incoming, model) = (staged.Denoiser, staged.Model);
        incoming.Reset();
        if (_active is null)
        {
            // Nothing is cleaning yet, so there is nothing to fade from.
            _active = incoming;
            _activeModel = model;
            return;
        }

        if (model == _activeModel)
        {
            // Asked for what is already cleaning, for example after switching back.
            DisposeLater(incoming);
            DisposeLater(_warming);
            _warming = null;
            _warmingModel = NothingRequested;
            _warmingFrames = 0;
            return;
        }

        DisposeLater(_warming);
        _warming = incoming;
        _warmingModel = model;
        _warmingFrames = 0;
    }

    private static void DisposeLater(IDenoiser? denoiser)
    {
        if (denoiser is not null)
        {
            _ = Task.Run(denoiser.Dispose);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _staged, null)?.Denoiser.Dispose();
        _warming?.Dispose();
        _active?.Dispose();
        _warming = null;
        _active = null;
        _activeModel = NothingRequested;
    }

    private sealed record Staged(IDenoiser Denoiser, int Model);
}

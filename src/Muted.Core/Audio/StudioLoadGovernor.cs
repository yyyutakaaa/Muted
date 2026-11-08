namespace Muted.Core.Audio;

/// <summary>
/// Steps Studio Voice down from Maximum to Balanced while the PC is too busy to run
/// Maximum on time, for example during a heavy game, and back up once there is room.
/// Maximum needs about 7 ms of every 10 ms on one core; when a game takes the cores,
/// frames arrive late and the voice crackles. Balanced needs less than half of that.
/// Called by the audio thread once per frame; allocates nothing.
/// </summary>
public sealed class StudioLoadGovernor
{
    public const int FramesPerSecond = 100;

    // Late frames and buffer underruns add up and fade with a half-life of two seconds.
    // A few stray late frames are normal; eight in a short while is a crackle.
    private const float Decay = 0.99654f;
    private const float StepDownScore = 8f;
    private const float UnderrunWeight = 4f;

    // Balanced runs at well under half of Maximum's cost, so a load this low on
    // Balanced means Maximum would fit again.
    private const float RoomToStepUp = 0.35f;

    private const int FirstWaitFrames = 60 * FramesPerSecond;
    private const int LongestWaitFrames = 10 * 60 * FramesPerSecond;
    private const int TrialFrames = 30 * FramesPerSecond;

    private float _score;
    private float _load;
    private bool _steppedDown;
    private int _waitFrames = FirstWaitFrames;
    private int _calmFrames;
    private int _trialFramesLeft;

    /// <summary>True while Balanced stands in for Maximum.</summary>
    public bool SteppedDown => _steppedDown;

    /// <summary>How many times it stepped down, for diagnostics and tests.</summary>
    public int StepDowns { get; private set; }

    public void Reset()
    {
        _score = 0f;
        _load = 0f;
        _steppedDown = false;
        _waitFrames = FirstWaitFrames;
        _calmFrames = 0;
        _trialFramesLeft = 0;
    }

    /// <param name="requested">The model you chose.</param>
    /// <param name="active">The model that cleaned this frame, or null while none did.</param>
    /// <param name="switching">True while a model is loading or crossfading; that frame says nothing.</param>
    /// <param name="frameLoad">How long this frame took, as a share of its 10 ms.</param>
    /// <param name="underrun">True when the output ran dry since the last frame.</param>
    /// <param name="enabled">False always runs the model you chose.</param>
    /// <returns>The model that should be cleaning.</returns>
    public StudioModel Observe(StudioModel requested, StudioModel? active, bool switching, float frameLoad, bool underrun, bool enabled)
    {
        if (!enabled || requested != StudioModel.DpdfNet8)
        {
            if (_steppedDown || _score > 0f)
            {
                Reset();
            }

            return requested;
        }

        _score *= Decay;
        _load = (_load * 0.99f) + (Math.Clamp(frameLoad, 0f, 4f) * 0.01f);
        if (switching || active is null)
        {
            return Effective(requested);
        }

        if (frameLoad > 1f)
        {
            _score += 1f;
        }

        if (underrun)
        {
            _score += UnderrunWeight;
        }

        if (!_steppedDown)
        {
            if (_trialFramesLeft > 0 && --_trialFramesLeft == 0)
            {
                // Maximum held up after coming back: the next step down waits the
                // shortest time again.
                _waitFrames = FirstWaitFrames;
            }

            if (active == StudioModel.DpdfNet8 && _score >= StepDownScore)
            {
                if (_trialFramesLeft > 0)
                {
                    // Back up too soon: the game is still running. Wait twice as long.
                    _waitFrames = Math.Min(_waitFrames * 2, LongestWaitFrames);
                }

                _steppedDown = true;
                StepDowns++;
                _score = 0f;
                _calmFrames = 0;
                _trialFramesLeft = 0;
            }
        }
        else if (active == StudioModel.DpdfNet2)
        {
            _calmFrames = _load < RoomToStepUp && _score < 1f ? _calmFrames + 1 : 0;
            if (_calmFrames >= _waitFrames)
            {
                _steppedDown = false;
                _score = 0f;
                _calmFrames = 0;
                _trialFramesLeft = TrialFrames;
            }
        }

        return Effective(requested);
    }

    private StudioModel Effective(StudioModel requested) =>
        _steppedDown && requested == StudioModel.DpdfNet8 ? StudioModel.DpdfNet2 : requested;
}

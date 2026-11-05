# Architecture

## Why a virtual cable

A desktop app can open existing capture and render endpoints through WASAPI,
but it can't publish a new microphone endpoint on its own. Discord and games
enumerate capture endpoints that audio drivers expose. A real "Muted
Microphone" would need a Windows WaveRT/AVStream driver, an administrator
install and Microsoft driver signing.

So the current version works like this:

```text
microphone capture endpoint
  → Muted user-mode DSP
  → virtual cable render endpoint
  → paired virtual cable capture endpoint
  → call app
```

That works with an existing, signed cable and keeps RNNoise entirely in user
mode.

## Realtime path

- WASAPI in shared, event-driven mode asks Windows for 48 kHz IEEE float mono.
  Shared mode converts the endpoint format when the physical microphone runs
  at another hardware format.
- The capture callback only writes into a fixed single-producer,
  single-consumer float ring buffer.
- An `AboveNormal` processing thread handles exactly 480 samples per call.
- Samples are scaled from `[-1, 1]` to the 16-bit amplitude range RNNoise
  expects, and back.
- The dry path is delayed by 960 samples before the dry/wet mix. The pinned
  RNNoise build combines an overlapping analysis window with an explicitly
  delayed spectrum; an impulse measurement confirms the 20 ms.
- On start, the requested WASAPI buffer plus 20 ms of headroom is filled. After
  that the output provider keeps a small 20 ms software FIFO. After a render
  stall the consumer drops old backlog above the high-water mark, so latency
  doesn't keep growing for minutes. Underruns are filled with silence, and
  drop and underrun counters stay measurable.
- The physical capture device and the virtual render device can run on
  different clocks, so the drift corrector occasionally makes a frame 479 or
  481 samples long through linear interpolation.

## Processing chain

Per frame of 480 samples, in this order:

```text
echo cancellation (optional) → input gain → high-pass (optional)
  → [shout analysis] → dry delay ∥ RNNoise → dry/wet mix → voice gate
  → voice character (optional) → automatic level (optional) → output gain
  → shout guard (optional) → limiter → mute → drift correction → cable
```

- The voice character (`VoiceCharacter`) is a low shelf, a proximity bell and
  dip, a high shelf with a split-band de-esser, an RMS compressor with a soft
  knee and make-up gain that doesn't lift silence, and 2x oversampled
  asymmetric tanh saturation. Switched on it adds 15 samples (0.31 ms); switched
  off it is bit-exact.
- The shout guard (`ShoutGuard`) analyses the input frame before RNNoise and
  applies its gain to the processed frame. Because that is exactly the 960
  samples of RNNoise and the dry delay later, the guard gets that look-ahead for
  free: the gain is fully closed before the first sample that is too loud,
  holds for 400 ms and opens in 150 ms.
- Calibration and automatic tuning look at the microphone before the input
  gain. During a calibration the audio thread copies every raw frame plus
  RNNoise's speech probability into a preallocated `CalibrationRecorder`;
  `VoiceAnalyzer` measures and advises afterwards, outside the realtime chain.
  `VoiceTracker` always runs, without allocations, and tracks the slow speech
  level, the noise floor and the 95th percentile of the speech probability in
  quiet moments. `AutoTuner` reads those on the UI timer and adjusts boost and
  gate in small, bounded steps.
- The high-pass is a second-order Butterworth before the delay, so dry and wet
  get the same filtering. Denormals are flushed per frame.
- Automatic level measures RMS and only adjusts while RNNoise reports speech;
  the gain ramps to its new value within the frame, without zipper noise.
- The limiter first finds the peak of the whole frame and the gain it needs, so
  the 0.97 ceiling is never exceeded. A per-sample safety covers the 1.5 ms
  attack.
- The reduction figure compares the RMS before and after RNNoise; that is what
  the UI shows as "noise removed".
- An optional second render path sends the same frame to a monitor output. That
  path has its own clock and trims its own backlog; if it fails, the cable keeps
  running and the engine only reports `MonitorFaulted`.

No managed objects are allocated in the capture callback or per DSP frame. The
UI only reads atomic measurements at 30 Hz and pauses that timer when no window
is visible. The waveform in the UI reads a separate ring buffer with one peak
value per frame.

A separate 5 Hz timer drives the tray speaking state and optional
notifications, also while the window is hidden. Session counters are built up
per audio frame, so window or timer delays don't affect the measured speaking
time. A session is one engine run.

Tests and diagnosis clips that you start yourself copy post-mute samples into a
preallocated buffer of at most 30 seconds. The audio thread never writes files.
Sealing the buffer prevents writes after it ends; WAV conversion and saving
happen outside the realtime chain. Playback uses a separate WASAPI render path
to a listening device and mutes the microphone output for the duration. Without
an export, a clip only lives in memory.

## Echo cancellation

The reference is a WASAPI loopback of the render endpoint the user listens to.
Loopback taps the render mix before it leaves the device, so the reference is by
definition ahead of its own echo, and a causal filter is enough.

- The filter is a partitioned frequency-domain adaptive filter: twenty
  partitions of 480 taps, 200 ms of tail in total, using overlap-save over a
  1024-point FFT. That costs six transforms per frame instead of twenty,
  because only one partition per frame is trimmed back in the time domain.
- The step size is normalised per bin on the summed reference power across the
  whole delay line (MDF normalisation).
- Double-talk detection: once the filter is worth anything, the rule is simply
  that the microphone contains more than the filter can explain. Before there
  is a model, it falls back to Geigel. Confidence in the model is tracked
  separately with a slowly decaying peak, because the reported ERLE collapses
  the moment you speak, and a detector that believed it would train on your own
  voice at exactly the wrong time.
- What the linear filter can't model, mostly loudspeaker distortion, is
  suppressed across the band based on the ratio between the residual error and
  the estimated echo. During double talk your own voice dominates that ratio,
  so nothing happens there.
- Loopback delivers nothing while nothing plays. The reference buffer then runs
  dry, the filter gets silence, and without excitation it doesn't adapt. When it
  falls more than eighty milliseconds behind, the buffer is trimmed, because a
  reference that lags describes audio older than the echo that needs to go.

.NET has no FFT, so there is one in `Muted.Core.Dsp.Fft`: radix-2, in place,
with precomputed twiddles and no allocation per call.

## Global shortcuts

`RegisterHotKey` only sees key-down, while push to talk also needs key-up. So
Muted uses a `WH_KEYBOARD_LL` hook on the UI thread, installed only once a key
is assigned. The hook passes every key on to the rest of Windows, so a game or
call keeps seeing it.

## A path without an external cable

A standalone distribution could later add a minimal, capture-only virtual
microphone based on Microsoft's SysVAD/WaveRT sample. The driver should only
provide the endpoint, the clock and ring-buffer transport. Capture, RNNoise,
settings and model updates stay in the existing user-mode process.

Such a driver is only ready for end users after, among other things:

- a slimmed-down, secured private driver interface;
- clocking, underrun and recovery tests;
- an installer with uninstall and upgrade paths;
- HLK/WHCP validation and Microsoft signing;
- crash, sleep/resume and hotplug tests on several Windows versions.

A generic capture APO is no replacement: modern APO distribution is tied to the
underlying audio driver and hardware, and it doesn't create a new endpoint
either. WASAPI process loopback intercepts render audio, not a physical
microphone.

Official references:

- [Microsoft SysVAD sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-driver-samples/sysvad-virtual-audio-device-driver-sample/)
- [Audio Processing Object architecture](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/audio-processing-object-architecture)
- [Low-latency audio](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/low-latency-audio)
- [Xiph RNNoise](https://github.com/xiph/rnnoise)

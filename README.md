# Muted

Muted is a small Windows app that runs your microphone through
[RNNoise](https://github.com/xiph/rnnoise), locally, before Discord, Teams, or
your game ever hears the signal. Fan noise, mic hiss, and keyboard clatter get
filtered out along the way.

```text
physical microphone
  → WASAPI shared mode (48 kHz mono)
  → optional echo cancellation, against a loopback of your speakers
  → input gain and optional high-pass
  → RNNoise (480 samples per call, 20 ms delay)
  → optional voice gate
  → optional warm radio voice (EQ, de-esser, compressor, saturation)
  → optional automatic level, output gain
  → optional shout guard (analysed 20 ms ahead), limiter
  → drift correction
  → playback side of a virtual audio cable
  → recording side of that cable in Discord / your call app / game
```

## Getting started

Windows only lets an app publish a new, selectable microphone through a
signed audio driver, and that's not something Muted ships. So it relies on a
virtual cable you've already installed, like [VB-CABLE](https://vb-audio.com/Cable/).

1. Install a signed virtual audio cable, and restart Windows if the installer
   asks for it.
2. Start Muted.
3. Pick your real microphone under **Microphone**.
4. Pick the cable's playback side under **Virtual cable output**, usually
   `CABLE Input` or `CABLE In 16ch` for VB-CABLE.
5. Hit the power button.
6. In Discord, Teams, Zoom, or your game, select the cable's recording side as
   your microphone, usually `CABLE Output` or `CABLE Out 16ch` for VB-CABLE.

Muted deliberately refuses to start into regular speakers or a headset.
Without that guard, a missing cable at autostart could quietly feed your own
output back into your microphone. Full setup, the 48 kHz requirement, and
troubleshooting live in [docs/VIRTUAL-CABLE.md](docs/VIRTUAL-CABLE.md).

Turn off your call app's built-in noise suppression if you hear pumping or
distortion. Two suppressors stacked on top of each other don't always beat
one.

## Simple by default

The Audio page shows four switches: noise suppression, echo cancellation, the
voice gate and a microphone boost. That is what a microphone needs.

Everything else, the dry/wet mix, the high-pass, the limiter, automatic
levelling, gate hold time, output gain, buffer size, monitoring, the echo
reference device and every live number, sits behind **Advanced** in Settings.
Switching it on changes nothing about how Muted sounds; it only stops hiding
the details.

## Shortcuts

Assign a push-to-talk key under **Shortcuts** and Muted stays muted until you
hold it. Push to mute is the opposite. There are also shortcuts for mute,
RNNoise, starting and stopping, and bringing the window back.

Shortcuts use a low-level keyboard hook, because Windows only reports key
presses to `RegisterHotKey` and push to talk needs the release too. Muted never
swallows a key: whatever you press still reaches the game or call you are in.
The hook is only installed once you actually assign a key.

## What it does

- runs the official, pinned Xiph RNNoise full-model build;
- captures audio at 48 kHz mono over WASAPI and renders event-driven;
- exposes a live dry/wet mix with a correctly delayed dry path;
- can optionally use RNNoise's own VAD to gate silence more aggressively, with
  an adjustable hold time;
- cancels the echo of your own speakers, so a headset is no longer required:
  a 200 ms adaptive filter learns the path from your speakers to your
  microphone and subtracts it, and stops learning while you talk over the
  other side;
- adds input and output gain, a high-pass for desk rumble, slow automatic
  levelling, and a limiter that catches peaks instead of clipping them;
- can send the processed voice to a second output so you hear what the other
  side hears;
- has global shortcuts for push to talk, push to mute, mute, RNNoise and
  start/stop, which keep working inside full-screen games;
- corrects clock drift sample by sample during long sessions;
- reconnects on its own when a device disappears mid-call, and can follow the
  Windows default microphone;
- draws the real microphone signal as a live waveform, with dBFS meters that
  have peak hold and a clip indicator, plus a readout of how much noise
  RNNoise actually removed;
- refreshes devices automatically on hotplug, and can minimize to the tray
  with autostart;
- has a compact always-on-top panel for the meter and a mute button;
- follows the Windows light/dark theme while Windows is switching, in a violet
  palette that both themes share, and optionally takes the Windows accent
  colour instead;
- keeps the interface to four switches unless you ask for the rest under
  Advanced, which then also brings back the dB scales and live readings;
- saves reusable audio profiles, including the whole processing chain, and
  switches them from the app or tray;
- offers tray controls for mute, RNNoise, monitoring, profiles, the compact
  panel, and setup diagnostics;
- checks virtual-cable routing, Windows sample formats, RNNoise availability,
  microphone signal, processing headroom and output stability, and copies the
  result as text for a bug report;
- stores settings in `%LOCALAPPDATA%\Muted\settings.json` and logs errors to
  `%LOCALAPPDATA%\Muted\Muted.log`, both reachable from Settings;
- has no account, no cloud, and no telemetry; records audio only when you explicitly start a test or diagnosis clip.

## Voice tools

**Tune to my voice** is a 24-second guided calibration: four seconds of silence,
fifteen of normal reading, then one loud phrase. Muted records the raw microphone and
RNNoise's speech probability in memory, then measures the room noise, your speech
level and peaks, your pitch, how sharp your s sounds are and how much your level
varies. From that it sets the microphone boost (speech at -22 dBFS, normal peaks
below -3 dBFS), the voice gate, the high-pass, the shout limit (between your normal
peaks and your shout) and the warm-voice amounts. The result plays straight away;
**Keep** or **Undo**. Nothing leaves the computer.

**Keep tuning while I talk** follows your speech level and the room while you talk,
and every eight seconds at most nudges the microphone boost (up to 1.5 dB per step,
never more than 6 dB from where it started) and the voice gate (up to 0.05 per
step, within 0.2). It ignores moments when the shout guard is blocking, and never
changes the shout guard or the warm voice. Applying a profile or calibrating sets a
new starting point.

**Warm radio voice** gives a full, close-up, smooth sound. Five sliders shape it:
warmth (a low shelf around 150 Hz), proximity (a bump around 220 Hz and a slight dip
around 3.5 kHz), smoothness (a softened top plus a de-esser for sharp s sounds),
compression (keeps quiet words present) and saturation (a subtle, oversampled tanh
warmth). It runs on the cleaned voice, after RNNoise and the gate, and adds 0.3 ms.

**Shout guard** cuts the output when you get too loud, so friends are spared an
earful. Set the loudness limit a few dB above your loudest normal speech, using the
input level shown next to it. It silences by default, or turns a shout down 20 dB.
Because RNNoise already delays the output by 20 ms, the guard analyses the input
that far ahead and is fully closed before the first loud sample; it then holds for
400 ms and fades back in over 150 ms. Presets never switch it off.

The Audio page includes Natural, Gaming, Podcast, Meeting and Warm Radio presets. Meeting enables
levelling and echo cancellation when a listening device is available; check that the
echo reference matches your speakers. Presets briefly restart running audio and can
be customized using the existing profile controls. Echo settings are now part of
profiles. Older settings retain their previous global echo settings during migration.

Import and export selected or all saved profiles as JSON. Device identifiers are
excluded, imported profiles receive new identifiers, and duplicate names receive a
suffix. Imports are validated before adding any profiles; the limit remains 20.

With audio running, **Start recording** captures processed, post-mute audio until you
press **Stop recording**, with a two-minute memory limit. The result can be played,
paused, scrubbed on the timeline, rewound by 10 seconds, replayed from the start,
deleted, and exported as WAV.
Select headphones or speakers as the playback output; virtual
cable playback is rejected. The microphone output is temporarily muted during replay
to prevent feedback. Cancel stops the operation. Temporary clips stay in memory and
are discarded when replaced, deleted, or the app closes. Only an explicit **Save
diagnosis WAV** action writes audio to disk; exported files remain until you delete them.

The tray shows speaking, muted, stopped and error states. Optional mute reminders
appear after sustained speech during manual mute, at most once per minute; idle
push-to-talk does not trigger reminders. Optional open/close sounds play through the
selected listening output (headphones recommended).

Audio health warnings detect sustained processing load, buffer buildup and recurring
dropouts, with a one-minute notification cooldown. Processing load measures the audio
frame time budget, not total system CPU; buffer time is not end-to-end latency.
Session statistics continue while minimized and show processed duration, estimated
unmuted speaking time, separate limiter attenuation periods, average noise reduction
and peak input. They cover the current or last audio run, reset on restart (including
profile changes/device recovery), and are not saved across application exits.

Hard keyboard clicks during speech can still partly leak through. That's a
limit of the model, not a bug.

## Speakers instead of a headset

RNNoise removes noise; it has no idea what your speakers are playing, so on
its own it cannot stop the other side from hearing themselves. Echo
cancellation can, and it lives under **Audio**.

Switch it on and pick the output you actually listen to. Muted takes a
loopback of that device and learns, in about a second of sound, how it
travels through the room into your microphone, then subtracts it. The tail
covers 200 ms, which is enough for the render buffer, the speakers and the
trip through the air. Adaptation stops while you talk over the far end,
because a filter that adapts to your own voice would subtract that too.

The reference device has to run at 48 kHz; the setup check says so if it
doesn't. Move your speakers or change their volume and it relearns. What is
left after the filter, mostly speaker distortion, is ducked by the residual
strength slider; turn that down if you both sound choppy when talking at
once.

## Requirements

- Windows 10/11 x64;
- .NET 9 Desktop Runtime for the smallest build;
- a virtual audio cable, so other apps can pick Muted's output as a
  microphone;
- Visual Studio 2022 C++ Build Tools, only needed if you rebuild the native
  RNNoise DLL yourself.

## Building and testing

```powershell
dotnet restore .\Muted.sln
dotnet build .\Muted.sln -c Release --no-restore
dotnet test .\Muted.sln -c Release --no-build
```

Run a development build:

```powershell
dotnet run --project .\src\Muted.App\Muted.App.csproj -c Debug
```

Create a small, framework-dependent distribution folder:

```powershell
.\scripts\publish.ps1
```

Or bundle the .NET runtime for a larger, standalone one:

```powershell
.\scripts\publish.ps1 -SelfContained
```

Both land under `artifacts\Muted-win-x64`, plus an
`artifacts\Muted-win-x64.zip`. The app is x64-only because the bundled
RNNoise DLL is too.

A local build isn't code-signed. For public distribution, both the EXE and
any installer should be signed with a trusted code-signing certificate, or
SmartScreen will warn users off.

## Automatic updates and releases

Installed copies check the latest published GitHub release in the background
when Muted starts. If a newer version exists, Muted asks before it downloads
anything: install now, remind me later, or skip this version. A skipped
version stays quiet in the background but still shows up when you press
**Check for updates** yourself. After approval it downloads the installer and
its SHA-256 checksum, verifies it, installs silently, and restarts on the new
version. The **Beta** channel in Settings also offers prereleases. Portable ZIP
copies do not install updates.

To publish version `0.4.1`, push a matching tag:

```powershell
git tag v0.4.1
git push origin v0.4.1
```

Or start **Actions → Release → Run workflow** and type the version there; the
workflow then creates the tag itself, after the installer has been built.

The release workflow builds and tests the app, creates the self-contained
installer and checksum, and publishes all release assets. A tag may also carry
a prerelease suffix, like `v0.3.0-beta1`; the build keeps the three numeric
parts and the release is marked as a prerelease, so only the beta channel
offers it. Users who installed a version from before automatic updating was
added need to install one updater-enabled release once by hand; releases after
that are offered automatically.

## Rebuilding native RNNoise

The verified runtime DLL already lives in the repo, so this is only needed if
you want to confirm the build is reproducible yourself. The script fetches
the exact pinned Xiph source and model, checks SHA-256 hashes, compiles
baseline/SSE4.1/AVX2 variants, and runs a native smoke test:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\native\rnnoise\build.ps1
```

Pins, exports, and build details are in
[native/rnnoise/README.md](native/rnnoise/README.md).

## Project layout

```text
src/Muted.Core           allocation-free DSP primitives, FFT, AEC and settings
src/Muted.Audio.Windows  WASAPI engine, device catalog, and RNNoise wrapper
src/Muted.App            WPF shell, views, controls, tray, and services
native/rnnoise           reproducible official native build
tests                    unit and native smoke tests
```

Why the virtual-cable route is the first version, and what a path to a real
driver would look like, is covered in
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Support

Muted is free and always will be. If it's useful to you, a coffee helps keep
it maintained: [buymeacoffee.com/yyyutakaaa](https://www.buymeacoffee.com/yyyutakaaa).

## License

Muted itself is MIT-licensed, see [LICENSE](LICENSE). RNNoise
(BSD-3-Clause) and NAudio (MIT) are bundled with their own notices intact,
see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and the `licenses`
folder.

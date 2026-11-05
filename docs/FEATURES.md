# Features

A tour of what Muted does and where to find it. For setup, start with the
[README](../README.md#getting-started) and the [virtual cable guide](VIRTUAL-CABLE.md).

## The audio path

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

## Where things are

Muted has three pages:

- **Home** is the microphone: one button to start or stop, your microphone, and
  a quick voice test.
- **Advanced** has your devices, noise removal strength, voice level, echo
  cancellation and the voice test, with the full processing chain, profiles,
  recording tools and keyboard shortcuts in sections below.
- **Settings** has startup, appearance, updates, the in-game indicator, the
  setup check and the setup guide.

Nothing on Advanced changes how Muted sounds until you change it yourself.

## Shortcuts

Assign a push-to-talk key under **Advanced → Keyboard shortcuts** and Muted stays muted until you
hold it. Push to mute is the opposite. There are also shortcuts for mute,
RNNoise, starting and stopping, and bringing the window back.

Shortcuts use a low-level keyboard hook, because Windows only reports key
presses to `RegisterHotKey` and push to talk needs the release too. Muted never
swallows a key: whatever you press still reaches the game or call you are in.
The hook is only installed once you actually assign a key.

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

Voice presets (Natural, Gaming, Podcast, Meeting and Warm Radio) are under
**Advanced → Profiles, recording export and other tools**. Meeting enables
levelling and echo cancellation when a listening device is available; check that the
echo reference matches your speakers. Presets briefly restart running audio and can
be customized using the existing profile controls. Echo settings are part of
profiles.

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

## Clean what you hear

The same RNNoise model can clean other people's audio: their fans, keyboards
and traffic. Muted can't sit between your call app and your headphones on its
own, so this takes a second virtual cable (VB-CABLE A+B, or Voicemeeter):

1. In Discord or Teams, set the **output** device to the second cable's
   playback side, for example `CABLE-A Input`.
2. In Muted, under **Advanced → Profiles, recording export and other tools →
   Clean what you hear**, pick that cable's recording side (`CABLE-A Output`)
   to listen to, and your headphones to play on.
3. Switch it on. The strength slider mixes in some of the original if you want
   it less strict.

This runs separately from your microphone, in mono, and adds about 20 ms.

## Before and after

Record a take under **Advanced → Profiles, recording export and other tools →
Microphone test & diagnosis**, then press **Save before/after WAV**. You get one file with the same moment twice: first
without Muted, then with it. Good for checking your setup, and for showing
friends what it does.

## In-game indicator and automatic profiles

**Settings → More preferences and tools → In-game indicator** puts a small *Live* or *Muted* pill on top of
every window while audio runs, so you can see your mute state mid-game. Clicks
go through it. Exclusive full-screen games draw over all windows, so use
borderless or windowed mode to see it.

**Advanced → Profiles, recording export and other tools → Automatic profiles**
switches to a profile while a program runs: Gaming while `valorant` runs, Meeting while `ms-teams` runs. When the program
closes, the profile you had comes back.

## Stream Deck, OBS and scripts

Start `Muted.exe` with a command and it is passed to the copy that is already
running, without opening a window. Point a Stream Deck *Open* action, an OBS
hotkey script or AutoHotkey at it:

```text
Muted.exe --toggle-mute        Muted.exe --mute / --unmute
Muted.exe --toggle             start or stop audio (also --start, --stop)
Muted.exe --toggle-suppression Muted.exe --toggle-overlay
Muted.exe --toggle-listening   Muted.exe --profile "Warm Radio"
Muted.exe --show
```

Commands only reach a copy run by the same Windows user.

## Speakers instead of a headset

RNNoise removes noise; it has no idea what your speakers are playing, so on
its own it cannot stop the other side from hearing themselves. Echo
cancellation can, and it lives under **Advanced → Sound**.

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

## Automatic updates

Installed copies check the latest published GitHub release in the background
when Muted starts. If a newer version exists, Muted asks before it downloads
anything: install now, remind me later, or skip this version. A skipped
version stays quiet in the background but still shows up when you press
**Check for updates** yourself. After approval it downloads the installer and
its SHA-256 checksum, verifies it, installs silently, and restarts on the new
version. The **Beta** channel in Settings also offers prereleases. Portable ZIP
copies do not install updates.

## Everything Muted does

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
- saves reusable audio profiles, including the whole processing chain, and
  switches them from the app or tray;
- offers tray controls for mute, RNNoise, monitoring, profiles, the compact
  panel, and setup diagnostics;
- checks virtual-cable routing, Windows sample formats, RNNoise availability,
  microphone signal, processing headroom and output stability, and copies the
  result as text for a bug report;
- stores settings in `%LOCALAPPDATA%\Muted\settings.json` and logs errors to
  `%LOCALAPPDATA%\Muted\Muted.log`, both reachable from Settings;
- keeps running totals of how long it cleaned and how much noise it removed,
  as numbers in the settings file;
- has no account, no cloud, and no telemetry; records audio only when you explicitly start a test or diagnosis clip.

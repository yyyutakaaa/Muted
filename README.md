# Muted

Muted is a small Windows app that runs your microphone through
[RNNoise](https://github.com/xiph/rnnoise), locally, before Discord, Teams, or
your game ever hears the signal. Fan noise, mic hiss, and keyboard clatter get
filtered out along the way.

```text
physical microphone
  → WASAPI shared mode (48 kHz mono)
  → RNNoise (480 samples per call, 20 ms delay)
  → optional voice gate
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

## What it does

- runs the official, pinned Xiph RNNoise full-model build;
- captures audio at 48 kHz mono over WASAPI and renders event-driven;
- exposes a live dry/wet mix with a correctly delayed dry path;
- can optionally use RNNoise's own VAD to gate silence more aggressively;
- corrects clock drift sample by sample during long sessions;
- shows input/output meters, refreshes devices automatically on hotplug, and
  can minimize to the tray with autostart;
- saves reusable audio profiles and switches them from the app or tray;
- offers tray controls for mute, RNNoise, profiles, and setup diagnostics;
- checks virtual-cable routing, Windows sample formats, RNNoise availability,
  microphone signal, and processing headroom;
- stores settings in `%LOCALAPPDATA%\Muted\settings.json` and logs errors to
  `%LOCALAPPDATA%\Muted\Muted.log`;
- has no account, no cloud, no telemetry, and records nothing.

RNNoise suppresses noise, it doesn't cancel echo. Sound coming back into your
mic from your speakers needs AEC, or just a headset. Hard keyboard clicks
during speech can still partly leak through. That's a limit of the model,
not a bug.

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
when Muted starts. If a newer stable version exists, Muted downloads the
installer and its SHA-256 checksum, verifies it, runs the installer silently,
and restarts on the new version. Portable ZIP copies do not install updates.

To publish version `0.2.0`, push a matching tag:

```powershell
git tag v0.2.0
git push origin v0.2.0
```

The release workflow builds and tests the app, creates the self-contained
installer and checksum, and publishes all release assets. The tag must contain
exactly three numeric version parts. Users who installed a version from before
automatic updating was added need to install one updater-enabled release once
by hand; releases after that are applied automatically.

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
src/Muted.Core           allocation-free DSP primitives and settings
src/Muted.Audio.Windows  WASAPI engine, device catalog, and RNNoise wrapper
src/Muted.App            WPF UI, tray, autostart, and settings
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

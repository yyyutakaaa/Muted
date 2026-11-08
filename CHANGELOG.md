# Changelog

All notable changes to Muted are listed here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/). While Muted is below 1.0, a minor
version may still change how a setting works.

When you cut a release, move the entries under **Unreleased** into a new section
named after the version, for example `## [0.9.0] - 2026-11-20`. The release
workflow puts that section at the top of the GitHub release notes.

## [Unreleased]

## [0.10.0-beta2] - 2026-11-08

### Added

- The version in the corner of the window now says whether it is a Beta or a
  Stable release, and which build it is: the first 7 characters of the commit
  it was built from, such as "Beta · c6bd6a6". About shows the same.

## [0.10.0-beta1] - 2026-11-08

### Added

- **Tune to my voice listens to what you say.** Whisper tiny.en runs on your PC
  and follows the sentence while you read it. As soon as the end is heard, the
  calibration moves on to the loud phrase, and it finishes when you have said
  that. No more waiting for a timer. What it heard shows under the sentence. If
  speech recognition cannot run, a pause after enough speech ends a step, and
  every step still has a time limit.
- **Only my voice**, under Advanced > Your voice. Tune to my voice now also
  learns how your voice sounds. With Only my voice on, Muted holds other voices
  back, like someone talking behind you or the TV, while you are not talking.
  A slider sets how strict it is, and Forget my voice deletes what it learned.
  When you and someone else talk at the same time, part of the other voice can
  still come through. The check runs in the background with WeSpeaker's CAM++
  model and never on the audio thread. When it is unsure, it lets the sound
  through.
- The installer is about 90 MB bigger because of the two speech models.

### Changed

- Muted is now source available under the PolyForm Noncommercial License 1.0.0
  instead of MIT. You can still read the code and use Muted for yourself.
  Commercial use needs permission. Versions up to and including 0.9.1 stay MIT.
- Advanced and Settings are reorganised, so every setting is in exactly one
  place. Advanced is how you sound: Sound, Your voice and Processing, then Voice
  test, Profiles, What you hear and Shortcuts. Fine-tune is gone; its controls
  and their "i" cards are on Processing, which also no longer hides half of the
  chain. Settings is the app itself, with the same menu on the left as Advanced:
  Devices, Startup, Indicators, Appearance, Updates, Help and About. Session
  statistics moved to the setup check.
- The cards under Processing fold shut, so the page is a short list until you
  open a card. A card keeps its on/off switch in its header; Levels and Buffer
  size say what they are set to. Noise removal and echo cancellation start open,
  and cards stay the way you leave them while Muted runs.
- The big Start processing button under the Advanced menu is now a quiet status
  line, such as "Stopped · Start". Clicking the logo goes back home from any
  page; right-click it for the window menu.
- Noise removal strength lives in one place, under Sound, with a Fine-tune
  slider below Gentle, Balanced and Strong. Processing links there instead of
  repeating it.
- The light theme is easier to read. Cards were white on an almost white page;
  the page is now grey-violet so every card stands out, and secondary text,
  borders, warnings and switches that are off are clearly darker. Secondary
  text is at least 7:1 against what it sits on.

### Fixed

- Studio Voice Maximum crackled while a heavy game such as Fortnite used the
  processor. Measured with every core busy, about one frame in five came too
  late. Maximum now steps down to Balanced by itself when frames come late or
  the output runs dry, and goes back to Maximum after a minute with room to
  spare. If the game is still running, it waits twice as long before the next
  try. The switch crossfades, so it is not heard. It can be turned off under
  Advanced > Sound with "Step down when the PC is busy".
- With Studio Voice on, Muted keeps 20 ms more audio in reserve, so one late
  frame no longer becomes a gap.

## [0.9.1] - 2026-11-08

### Added

- The version number now shows in the bottom right corner of the window, on
  every page.

### Changed

- New screenshots in the README and on the website.

### Removed

- The SignPath code signing policy and the optional signing step in the
  release workflow, because the SignPath Foundation application was not
  approved. Installers are not code signed, so Windows SmartScreen may warn
  when you run one.

## [0.9.0] - 2026-11-08

### Added

- **Studio Voice**: AI noise removal that also removes room echo, switchable on
  the home screen and under Advanced > Sound. Pick one of three models:
  **Classic** (DeepFilterNet3, the lightest), **Balanced** (DPDFNet 2) or
  **Maximum** (DPDFNet 8). The two DPDFNet models use dual-path recurrent blocks,
  which also remove noise that sits in the same frequency band as your voice.
  - Studio Voice takes over noise removal completely: nothing of the noisy
    original is mixed back in, and Gentle, Balanced and Strong step aside while
    it is on. It only removes noise; it never changes your gate, level,
    high-pass or limiter.
  - A model loads in the background. Until it is ready, or if it cannot run, the
    standard noise removal keeps working. Choosing another model loads it next
    to the one that is cleaning, lets it settle for 0.3 seconds and crossfades
    over 0.1 seconds, so the microphone never drops out.
  - All three models deliver the same 50 ms of delay, run on one processor
    thread and need about 2.5 ms (Classic, Balanced) or 6 ms (Maximum) of
    processing for every 10 ms of audio. The chosen model is remembered.
  - The voice gate opens only when both Studio Voice and the standard model hear
    a voice, so key and mouse clicks no longer open it.
  - If the model ever stops removing room noise, Muted restarts it by itself,
    the same state that used to need an off-and-on.
- Every slider under Advanced > Fine-tune has a small "i" next to its name.
  Hover it, or tab to it, and a card explains in plain words what the slider
  does, when it works best and what to expect. It also works while the slider
  itself is greyed out, and screen readers read the same three answers.

### Changed

- "Tune to my voice" no longer raises the microphone boost. A digital boost lifts
  the noise that is left over after the noise removal by the same amount, and
  before the noise removal loud peaks clip against the ceiling, so a quiet
  microphone used to end up at +10 dB and sound worse. The calibration now only
  turns a signal down when it is too hot. Quiet speech gets up to +6 dB of voice
  level after the noise removal (not when Automatic level is on), and when that
  is not enough it says how many dB to raise in the Windows sound settings.
  "Keep tuning while I talk" follows the same rule: it can turn the microphone
  down but never up.
- Automatic level follows your average speech level over a few seconds instead
  of reacting to every syllable, so soft word endings no longer push it up. Its
  largest boost is 12 dB instead of 15.5 dB.
- Every profile has its own name. The preset buttons (Natural, Gaming, Podcast,
  Meeting, Warm Radio) used to add a second "Meeting" or "Gaming" next to the
  default profile of the same name; now they update the profile that already
  carries the name. Saving a profile under a name that is taken adds " (2)".
  Settings from before are tidied up when Muted starts: of two profiles with the
  same name the one you are using stays, otherwise the preset one, and the
  active profile and program rules follow to the one that stayed.
- The update prompt is now a Muted window instead of a Windows message box.
  It shows the installed and new version, links to the release notes and
  shows download progress while the update installs.
- A calmer, easier-to-read look. The dark theme is less black, secondary
  text has more contrast, cards lose their outlines and the Advanced page
  drops its divider lines. The home screen title uses a heavier weight and
  Settings has less explanatory text.
- Buttons no longer keep a purple outline after a click; the focus ring only
  shows while you move through the app with the keyboard.
- Smoother motion: hover effects fade, buttons give under the cursor,
  sections slide open with a turning arrow, the home screen builds up in
  steps and the power button pulses while Muted is on.
- Checkboxes match the rest of Muted instead of the Windows default.
- The window keeps one size (780 x 880) instead of growing when you open
  Advanced and shrinking again on the home screen. Content keeps the same
  width too: room for the scrollbar is always reserved, so pages no longer
  get narrower when they are long enough to scroll.
- Advanced has a menu: Sound, Devices and Voice test, plus Fine-tune,
  Processing, Profiles & tools and Shortcuts under More. Only the chosen
  section is shown, the menu stays in place while it scrolls, and Muted
  remembers the last section you opened.

### Fixed

- The menu behind the three dots next to the preset picker showed empty boxes
  instead of text: it inherited the icon font of the button. Menus now always
  use the normal font.

## [0.8.1] - 2026-11-08

### Changed

- The installer now lists the publisher and links to the project, issues and
  releases in Windows' installed apps.
- Local builds report the current version instead of 0.5.0.
- The winget manifests point at 0.8.0.
- The README is shorter; the full feature tour moved to `docs/FEATURES.md`,
  with menu paths for the Home, Advanced and Settings pages.
- The architecture and virtual cable guides are in English; the Dutch versions
  moved to `docs/nl`.

### Fixed

- Installers from earlier updates no longer pile up in the `Updates` folder.
  Muted keeps only the installer of the version it runs and removes the rest
  at startup and before it downloads a new update.

### Added

- `CHANGELOG.md`, `CONTRIBUTING.md` and `SECURITY.md`.
- Release notes are taken from this changelog.
- A code signing policy and a privacy section in the README, linked from the
  website, for the SignPath Foundation application. Signed releases carry the
  SignPath attribution in their notes.

## [0.8.0] - 2026-10-29

### Changed

- The app is built around two pages: a simple microphone home screen and one
  Advanced page with every other setting.
- Settings are reorganised, with the voice test moved there.
- New installs start with automatic tuning and the dark theme. Existing
  settings are kept.

## [0.7.0] - 2026-10-29

### Added

- A four-step setup guide on first start.
- Clean what you hear: RNNoise on other people's audio, through a second
  virtual cable.
- Before/after recordings saved as one WAV file.
- An in-game indicator that shows whether you are live or muted.
- Automatic profiles that switch while a chosen program runs.
- Command-line commands for Stream Deck, OBS and scripts, such as
  `--toggle-mute` and `--profile`.
- Report a problem from the setup check, with the results filled in.
- Lifetime statistics.
- Install through Scoop, and winget manifests for a first submission.
- Optional code signing of the installer through SignPath.
- CI on every pull request, and a landing page on GitHub Pages.

## [0.6.0] - 2026-10-20

### Added

- Voice presets (Natural, Gaming, Podcast, Meeting, Warm Radio).
- Profile import and export as JSON.
- In-memory microphone recordings with playback and WAV export.
- Warm radio voice: warmth, proximity, smoothness, compression and
  saturation, after RNNoise.
- Shout guard, which uses the 20 ms RNNoise delay as look-ahead to catch a
  shout before its first loud sample.
- Tune to my voice: a guided 24-second calibration with Keep and Undo, and
  optional tuning while you talk.
- Tray status, audio health warnings and session statistics.

### Changed

- The main window is redesigned around a live waveform, segmented meters and
  a slide-out Advanced panel.

### Fixed

- A crash when the echo reference or monitor device was compared from the UI
  thread.
- Audio devices are released on the thread that created them.

## [0.5.1] - 2026-09-10

### Changed

- One violet palette across the app; amber and rose only for warnings and
  errors.

### Fixed

- The light theme now actually switches, also while Muted is running, and
  follows Windows by default.

## [0.5.0] - 2026-08-24

### Added

- Echo cancellation, so speakers work without a headset.
- Pages animate in, and controls react to the mouse.

### Changed

- The Audio page shows four switches; everything else moved behind an
  Advanced setting.
- The logo keeps its own colour instead of following the Windows accent.

### Fixed

- Most sound cards were refused by the echo reference format check.
- A missing style could crash the main window on open.

## [0.4.1] - 2026-08-23

### Fixed

- The level meters no longer starve the UI thread.

## [0.4.0] - 2026-08-22

### Added

- Global shortcuts for push to talk, push to mute, mute, RNNoise and
  start/stop, which keep working in full-screen games.
- Monitoring of your processed voice on a second output.
- High-pass filter, automatic level, input and output gain and a limiter.
- A hold-to-compare button that bypasses RNNoise while pressed.
- Automatic reconnect when a device disappears, and an option to follow the
  Windows default microphone.
- A live waveform, dBFS meters with peak hold, a compact always-on-top panel
  and a light theme.
- A beta update channel, and skipping a version.

### Changed

- Releases can be started from the Actions tab, and the tag is only created
  once the installer has built.

## [0.3.1] - 2026-07-14

### Added

- Muted asks before installing an update, and you can check for updates by
  hand.

## [0.3.0] - 2026-07-14

### Added

- Audio profiles, tray controls and a setup check.

## [0.2.2] - 2026-07-14

### Fixed

- Installed copies detect updates, and the version label is correct.

## [0.2.1] - 2026-07-14

### Added

- Automatic updates for installed copies, and a release workflow.

## [0.2.0] - 2026-07-12

Tagged only; there is no GitHub release for this version.

### Added

- A sponsor link.

### Fixed

- Broken icon references.

## [0.1.0] - 2026-07-12

First public preview.

### Added

- RNNoise noise suppression on your microphone, into a virtual audio cable.
- A dry/wet mix, an optional voice gate and live meters.
- Device refresh on hotplug, tray mode and autostart.
- An installer.

[Unreleased]: https://github.com/yyyutakaaa/Muted/compare/v0.10.0-beta2...HEAD
[0.10.0-beta2]: https://github.com/yyyutakaaa/Muted/compare/v0.10.0-beta1...v0.10.0-beta2
[0.10.0-beta1]: https://github.com/yyyutakaaa/Muted/compare/v0.9.1...v0.10.0-beta1
[0.9.1]: https://github.com/yyyutakaaa/Muted/compare/v0.9.0...v0.9.1
[0.9.0]: https://github.com/yyyutakaaa/Muted/compare/v0.8.1...v0.9.0
[0.8.1]: https://github.com/yyyutakaaa/Muted/compare/v0.8.0...v0.8.1
[0.8.0]: https://github.com/yyyutakaaa/Muted/compare/v0.7.0...v0.8.0
[0.7.0]: https://github.com/yyyutakaaa/Muted/compare/v0.6.0...v0.7.0
[0.6.0]: https://github.com/yyyutakaaa/Muted/compare/v0.5.1...v0.6.0
[0.5.1]: https://github.com/yyyutakaaa/Muted/compare/v0.5.0...v0.5.1
[0.5.0]: https://github.com/yyyutakaaa/Muted/compare/v0.4.1...v0.5.0
[0.4.1]: https://github.com/yyyutakaaa/Muted/compare/v0.4.0...v0.4.1
[0.4.0]: https://github.com/yyyutakaaa/Muted/compare/v0.3.1...v0.4.0
[0.3.1]: https://github.com/yyyutakaaa/Muted/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/yyyutakaaa/Muted/compare/v0.2.2...v0.3.0
[0.2.2]: https://github.com/yyyutakaaa/Muted/compare/v0.2.1...v0.2.2
[0.2.1]: https://github.com/yyyutakaaa/Muted/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/yyyutakaaa/Muted/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/yyyutakaaa/Muted/releases/tag/v0.1.0

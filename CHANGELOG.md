# Changelog

All notable changes to Muted are listed here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/). While Muted is below 1.0, a minor
version may still change how a setting works.

When you cut a release, move the entries under **Unreleased** into a new section
named after the version, for example `## [0.9.0] - 2026-11-20`. The release
workflow puts that section at the top of the GitHub release notes.

## [Unreleased]

### Changed

- The installer now lists the publisher and links to the project, issues and
  releases in Windows' installed apps.
- Local builds report the current version instead of 0.5.0.
- The winget manifests point at 0.8.0.

- The README is shorter; the full feature tour moved to `docs/FEATURES.md`,
  with menu paths for the Home, Advanced and Settings pages.
- The architecture and virtual cable guides are in English; the Dutch versions
  moved to `docs/nl`.

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

[Unreleased]: https://github.com/yyyutakaaa/Muted/compare/v0.8.0...HEAD
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

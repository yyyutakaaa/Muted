# Contributing to Muted

Thanks for wanting to help. Bug reports, setup questions, ideas and pull requests
are all welcome.

## Before you start

- **A bug?** Open a [bug report](https://github.com/yyyutakaaa/Muted/issues/new?template=bug_report.yml).
  The quickest way is **Settings → Check setup → Report a problem** in the app,
  which fills in your setup for you.
- **Stuck on setup?** Ask in [Discussions](https://github.com/yyyutakaaa/Muted/discussions).
  Most problems are the virtual cable or the 48 kHz format; see
  [docs/VIRTUAL-CABLE.md](docs/VIRTUAL-CABLE.md).
- **An idea?** Open a [feature request](https://github.com/yyyutakaaa/Muted/issues/new?template=feature_request.yml)
  before writing a large change, so we can agree on the approach first.
- **A security problem?** Don't open a public issue; see [SECURITY.md](SECURITY.md).

## Building

You need Windows 10 or 11 (x64) and the .NET SDK version pinned in
[global.json](global.json). Visual Studio 2022 or Rider work, but the command
line is enough:

```powershell
dotnet restore .\Muted.sln
dotnet build .\Muted.sln -c Release --no-restore
dotnet test .\Muted.sln -c Release --no-build
dotnet run --project .\src\Muted.App\Muted.App.csproj -c Debug
```

To try the app for real you also need a virtual audio cable such as
[VB-CABLE](https://vb-audio.com/Cable/).

### Distribution builds

```powershell
.\scripts\publish.ps1                 # small, needs the .NET 9 Desktop Runtime
.\scripts\publish.ps1 -SelfContained  # larger, includes the runtime
.\scripts\build-installer.ps1 -Version 0.9.0   # needs Inno Setup
```

Both publish variants land in `artifacts\Muted-win-x64`, plus
`artifacts\Muted-win-x64.zip`. The app is x64 only, because the bundled RNNoise
DLL is.

### Native RNNoise

The verified `rnnoise.dll` is already in the repository, so you only need this
to check that the build is reproducible or to change it. The script fetches the
pinned Xiph source and model, checks their SHA-256 hashes, builds
baseline/SSE4.1/AVX2 variants and runs a smoke test. It needs the Visual Studio
2022 C++ Build Tools:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\native\rnnoise\build.ps1
```

Pins and build details are in [native/rnnoise/README.md](native/rnnoise/README.md).

## Where things live

| Folder | What is in it |
| --- | --- |
| `src/Muted.Core` | DSP, settings and profiles. No Windows dependencies, so most tests live here. |
| `src/Muted.Audio.Windows` | The WASAPI engine, devices and the RNNoise wrapper. |
| `src/Muted.App` | The WPF app: windows, views, view models, tray and services. |
| `tests` | Unit tests for Core and integration tests for the audio layer. |
| `installer`, `scripts`, `packaging`, `bucket` | Installer, build scripts, winget and Scoop. |

[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) explains how the audio path is put
together and why.

## Making a change

1. Fork the repository and create a branch from `master`.
2. Keep the change focused. One fix or feature per pull request is easier to
   review and to revert.
3. Add or update tests for anything in `Muted.Core` or the audio engine.
4. Run the build and tests above before you push. CI runs the same on every
   pull request.
5. Add a line under **Unreleased** in [CHANGELOG.md](CHANGELOG.md) if users
   will notice the change.
6. Open the pull request and describe what changed and how you tested it. For
   UI changes, a screenshot helps a lot.

### A few rules for the audio code

- Per-frame processing must not allocate or block. Allocate buffers up front
  and reuse them; the DSP in `Muted.Core` is written that way.
- WASAPI and COM objects are created on a thread-pool thread; release them
  there too, never on the UI thread.
- New settings must be clamped on load, saved in profiles where it makes sense,
  and keep older settings files working.

### Commit messages

Write the subject as a short sentence in the imperative that says what the
change does for the user, for example `Cancel the echo of your own speakers`.
Use the body to explain why, when that is not obvious.

## Releases (maintainer)

1. Move the **Unreleased** entries in `CHANGELOG.md` into a new
   `## [x.y.z] - YYYY-MM-DD` section and merge that to `master`.
2. Push a tag: `git tag vx.y.z` and `git push origin vx.y.z`, or run
   **Actions → Release** with the version.

The workflow builds, tests and publishes the installer, the checksum and the
portable zip, uses the changelog section as the release notes, and updates
Scoop and winget.

A tag with a suffix, like `v0.9.0-beta1`, becomes a prerelease. The build keeps
the three numbers, and only the beta update channel offers it. Scoop and winget
skip prereleases.

Three optional steps switch themselves on when their settings exist:

- **Code signing** through [SignPath](https://signpath.org/), free for open
  source: set the `SIGNPATH_ORGANIZATION_ID` variable and the
  `SIGNPATH_API_TOKEN` secret. `SIGNPATH_PROJECT_SLUG` and
  `SIGNPATH_POLICY_SLUG` default to `muted` and `release-signing`. The artifact
  configuration is in `packaging/signpath`.
- **winget**: set the `WINGET_TOKEN` secret after the first manual submission;
  see [packaging/winget](packaging/winget/README.md).
- **Scoop**: stable releases update `bucket/muted.json` on the default branch.

## License

By contributing you agree that your contribution is licensed under the
[MIT License](LICENSE), like the rest of Muted.

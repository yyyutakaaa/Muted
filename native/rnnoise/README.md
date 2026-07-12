# Native RNNoise for Windows x64

This directory contains the reproducible native build used by Muted. It
downloads an exact official Xiph RNNoise source commit and the exact full
model selected by that commit, verifies every downloaded archive before
extracting it, and builds a release `rnnoise.dll` with the local MSVC x64
toolchain.

## Build

Requirements:

- Windows PowerShell 5.1 or newer;
- Visual Studio 2022 (or Build Tools) with **Desktop development with C++**;
- `tar.exe` (included with supported Windows versions);
- internet access to GitHub/Xiph on the first build.

From the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\native\rnnoise\build.ps1
```

Useful switches:

- `-ForceDownload` downloads both archives again;
- `-KeepBuildTree` preserves extracted sources and object files for debugging;
- `-CleanDownloads` removes the verified download cache after a successful
  build.

The final files are written to:

```text
src/Muted.Audio.Windows/runtimes/win-x64/native/rnnoise.dll
src/Muted.Audio.Windows/runtimes/win-x64/native/rnnoise.dll.sha256
```

The script refuses to publish the DLL unless all of these checks pass:

1. source and model archive SHA-256 values match `pins.json`;
2. the model selected by upstream's `model_version` matches the pin;
3. the extracted full-model C source/header and upstream license match their
   pinned SHA-256 values;
4. MSVC emits an AMD64 PE DLL with exactly the expected public C exports;
5. a native smoke test initializes the embedded model and processes a silent
   480-sample frame;
6. the staged and published DLL SHA-256 values are identical and the sidecar
   checksum validates.

The build uses the static release CRT (`/MT`) so the native binary has no
separate Visual C++ runtime deployment requirement. The generic path targets
the x64 SSE2 baseline; RNNoise dispatches to SSE4.1 or AVX2 kernels when the
CPU supports them. Link-time optimization and dead-code folding keep the DLL
as small as practical while retaining the full model.

## Runtime contract

RNNoise processes mono, 48 kHz audio in frames of 480 `float` samples (10 ms).
The upstream API uses the 16-bit PCM numeric scale: convert normalized
`[-1, 1]` samples to approximately `[-32768, 32767]` before
`rnnoise_process_frame`, then scale its output back when the managed pipeline
uses normalized floats. Keep one `DenoiseState` per continuous stream and
never allocate or destroy it on the real-time audio callback. In-place
processing is supported. RNNoise uses an overlapping analysis window and an
explicitly delayed spectrum. The pinned build has a measured 960-sample
(20 ms) end-to-end algorithmic delay; keep any dry/wet path delayed by the
same amount.

The stable exported API is listed in `exports.def`. Muted normally needs only
`rnnoise_get_frame_size`, `rnnoise_create`, `rnnoise_process_frame`, and
`rnnoise_destroy`.

## Pinning and third-party notice

`pins.json` is the source of truth for source/model provenance, hashes, build
variant, architecture, and ABI exports. The full model is intentionally used;
the smaller `rnnoise_data_little.c` from the same official archive is not
compiled.

RNNoise is distributed under the BSD 3-Clause license. The exact license from
the pinned upstream source is preserved in `LICENSE.txt` and must accompany
binary distributions that contain `rnnoise.dll`. Upstream project and model
URLs are recorded in `pins.json`.

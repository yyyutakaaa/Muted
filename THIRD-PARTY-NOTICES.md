# Third-party notices

Muted distributes or references the following third-party components.

## RNNoise

- Project: Xiph.Org RNNoise
- Source: https://github.com/xiph/rnnoise
- Pinned commit: `70f1d256acd4b34a572f999a05c87bf00b67730d`
- License: BSD-3-Clause
- Full notice: `licenses/RNNoise.txt` in a published build, sourced from
  `native/rnnoise/LICENSE.txt`

The embedded full-model provenance and verified hashes are recorded in
`native/rnnoise/pins.json`.

## NAudio

- Project: NAudio
- Source: https://github.com/naudio/NAudio
- Version: 2.3.0
- License: MIT
- Full notice: `licenses/NAudio.txt`

## DeepFilterNet (Studio Voice)

- Project: DeepFilterNet3 by Hendrik Schröter et al.
- Source: https://github.com/Rikorose/DeepFilterNet
- Streaming ONNX export: the torchDF path in https://github.com/grazder/DeepFilterNet,
  copied from https://huggingface.co/kimtos-labs/denoiser-dfn3
- File: `models/deepfilternet3.onnx`, SHA-256 in `models/deepfilternet3.onnx.sha256`
- License: MIT or Apache-2.0, at your option; Muted uses it under MIT
- Full notice: `licenses/DeepFilterNet.txt`

## DPDFNet (Studio Voice: Balanced and Maximum quality)

- Project: DPDFNet by Daniel Rika, Nino Sapir and Ido Gus (CEVA)
- Source: https://github.com/ceva-ip/DPDFNet
- Paper: https://arxiv.org/abs/2512.16420
- Models: `models/dpdfnet2_48khz_hr.onnx` and `models/dpdfnet8_48khz_hr.onnx`,
  each with its SHA-256 in the matching `.sha256` file
- License: Apache-2.0
- Full notice: `licenses/DPDFNet.txt`

## Whisper tiny.en (Tune to my voice)

- Project: Whisper by OpenAI
- Source: https://github.com/openai/whisper
- ONNX export: sherpa-onnx by k2-fsa (Apache-2.0),
  https://huggingface.co/csukuangfj/sherpa-onnx-whisper-tiny.en
- Files: `models/whisper-tiny.en-encoder.onnx` (the int8 encoder as published),
  `models/whisper-tiny.en-decoder.onnx` and `models/whisper-tiny.en-tokens.txt`,
  each with its SHA-256 in the matching `.sha256` file
- Modified: in the decoder, the token embedding table is stored as 16-bit floats
  and converted back when the model loads, which makes the file 40 MB smaller.
  The rest of the int8 decoder is unchanged.
- License: MIT
- Full notice: `licenses/Whisper.txt`

## WeSpeaker CAM++ (Only my voice)

- Project: WeSpeaker, CAM++ trained on VoxCeleb
- Source: https://github.com/wenet-e2e/wespeaker
- ONNX file: `wespeaker_en_voxceleb_CAM++.onnx` from the sherpa-onnx speaker
  recognition models, https://github.com/k2-fsa/sherpa-onnx/releases/tag/speaker-recongition-models
- File: `models/wespeaker-campplus.onnx`, SHA-256 in `models/wespeaker-campplus.onnx.sha256`
- License: Apache-2.0
- Full notice: `licenses/WeSpeaker.txt`

## ONNX Runtime

- Project: Microsoft ONNX Runtime
- Source: https://github.com/microsoft/onnxruntime
- Version: 1.20.1
- License: MIT
- Full notice: `licenses/ONNXRuntime.txt`

## Virtual audio cables

Muted does not bundle VB-CABLE, Virtual Audio Cable, Voicemeeter or another
virtual audio driver. Any cable is installed and licensed separately by the
user under its vendor's terms.

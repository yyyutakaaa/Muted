[CmdletBinding()]
param(
    [string] $PackagePath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\Muted-win-x64')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$package = (Resolve-Path -LiteralPath $PackagePath).Path
$requiredFiles = @(
    'Muted.exe',
    'Muted.dll',
    'icon.ico',
    'rnnoise.dll',
    'rnnoise.dll.sha256',
    'README.md',
    'THIRD-PARTY-NOTICES.md',
    'licenses\RNNoise.txt',
    'licenses\NAudio.txt',
    'licenses\DeepFilterNet.txt',
    'licenses\DPDFNet.txt',
    'licenses\ONNXRuntime.txt',
    'onnxruntime.dll',
    'models\deepfilternet3.onnx',
    'models\deepfilternet3.onnx.sha256',
    'models\dpdfnet2_48khz_hr.onnx',
    'models\dpdfnet2_48khz_hr.onnx.sha256',
    'models\dpdfnet8_48khz_hr.onnx',
    'models\dpdfnet8_48khz_hr.onnx.sha256'
)

foreach ($relativePath in $requiredFiles) {
    $candidate = Join-Path $package $relativePath
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "Published package is missing $relativePath."
    }
}

$checksumPath = Join-Path $package 'rnnoise.dll.sha256'
$checksumLine = (Get-Content -LiteralPath $checksumPath -TotalCount 1).Trim()
if ($checksumLine -notmatch '^([0-9a-fA-F]{64})(?:\s|$)') {
    throw 'rnnoise.dll.sha256 does not contain a valid SHA-256 value.'
}

$expectedHash = $Matches[1].ToLowerInvariant()
$actualHash = (Get-FileHash -LiteralPath (Join-Path $package 'rnnoise.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $expectedHash) {
    throw "rnnoise.dll hash mismatch. Expected $expectedHash, got $actualHash."
}

# Studio Voice models: every file that ships must be the one that was pinned.
foreach ($modelName in @('deepfilternet3', 'dpdfnet2_48khz_hr', 'dpdfnet8_48khz_hr')) {
    $modelPath = Join-Path $package "models\$modelName.onnx"
    $modelLine = (Get-Content -LiteralPath "$modelPath.sha256" -TotalCount 1).Trim()
    if ($modelLine -notmatch '^([0-9a-fA-F]{64})(?:\s|$)') {
        throw "$modelName.onnx.sha256 does not contain a valid SHA-256 value."
    }

    $expectedModelHash = $Matches[1].ToLowerInvariant()
    $actualModelHash = (Get-FileHash -LiteralPath $modelPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualModelHash -ne $expectedModelHash) {
        throw "$modelName.onnx hash mismatch. Expected $expectedModelHash, got $actualModelHash."
    }

    Write-Host "Studio Voice model $modelName SHA-256: $actualModelHash"
}

Write-Host "Verified package at $package"
Write-Host "RNNoise SHA-256: $actualHash"

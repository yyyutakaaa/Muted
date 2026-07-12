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
    'licenses\NAudio.txt'
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

Write-Host "Verified package at $package"
Write-Host "RNNoise SHA-256: $actualHash"

[CmdletBinding()]
param(
    [switch] $ForceDownload,
    [switch] $KeepBuildTree,
    [switch] $CleanDownloads
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Set-StrictMode -Version 2.0

function Get-NormalizedSha256 {
    param([Parameter(Mandatory = $true)][string] $Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-FileHash {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Expected,
        [Parameter(Mandatory = $true)][string] $Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description is missing: $Path"
    }

    $actual = Get-NormalizedSha256 -Path $Path
    if ($actual -ne $Expected.ToLowerInvariant()) {
        throw "$Description SHA-256 mismatch. Expected $Expected, got $actual ($Path)."
    }
}

function Get-VerifiedDownload {
    param(
        [Parameter(Mandatory = $true)][string] $Uri,
        [Parameter(Mandatory = $true)][string] $Destination,
        [Parameter(Mandatory = $true)][string] $ExpectedSha256,
        [Parameter(Mandatory = $true)][string] $Description
    )

    if ((Test-Path -LiteralPath $Destination -PathType Leaf) -and -not $ForceDownload) {
        $cachedHash = Get-NormalizedSha256 -Path $Destination
        if ($cachedHash -eq $ExpectedSha256.ToLowerInvariant()) {
            Write-Host "Using verified cached $Description."
            return
        }

        Write-Warning "Discarding cached $Description with SHA-256 $cachedHash."
        Remove-Item -LiteralPath $Destination -Force
    }

    $partial = "$Destination.partial"
    if (Test-Path -LiteralPath $partial) {
        Remove-Item -LiteralPath $partial -Force
    }

    Write-Host "Downloading $Description from $Uri"
    try {
        Invoke-WebRequest -UseBasicParsing -Uri $Uri -OutFile $partial
        Assert-FileHash -Path $partial -Expected $ExpectedSha256 -Description $Description
        Move-Item -LiteralPath $partial -Destination $Destination -Force
    }
    finally {
        if (Test-Path -LiteralPath $partial) {
            Remove-Item -LiteralPath $partial -Force
        }
    }
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)][string] $FilePath,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]] $Arguments,
        [Parameter(Mandatory = $true)][string] $Description
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Import-MsvcX64Environment {
    $vsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $installPath = $null

    if (Test-Path -LiteralPath $vsWhere -PathType Leaf) {
        $installPath = (& $vsWhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1)
    }

    if ([string]::IsNullOrWhiteSpace($installPath)) {
        $candidates = @(
            (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\2022\BuildTools'),
            (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\2022\Community'),
            (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\2022\Professional'),
            (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\2022\Enterprise')
        )

        foreach ($candidate in $candidates) {
            if (Test-Path -LiteralPath (Join-Path $candidate 'VC\Auxiliary\Build\vcvars64.bat') -PathType Leaf) {
                $installPath = $candidate
                break
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($installPath)) {
        throw 'No Visual Studio installation with the MSVC x64 toolchain was found. Install the Desktop development with C++ workload.'
    }

    $vcVars = Join-Path $installPath 'VC\Auxiliary\Build\vcvars64.bat'
    if (-not (Test-Path -LiteralPath $vcVars -PathType Leaf)) {
        throw "The x64 MSVC environment script was not found: $vcVars"
    }

    $environmentLines = & $env:ComSpec /d /s /c "`"$vcVars`" >nul && set"
    if ($LASTEXITCODE -ne 0) {
        throw "vcvars64.bat failed with exit code $LASTEXITCODE."
    }

    foreach ($line in $environmentLines) {
        $parts = $line -split '=', 2
        if ($parts.Count -eq 2) {
            [Environment]::SetEnvironmentVariable($parts[0], $parts[1], 'Process')
        }
    }

    foreach ($tool in @('cl.exe', 'link.exe', 'dumpbin.exe')) {
        if ($null -eq (Get-Command $tool -ErrorAction SilentlyContinue)) {
            throw "$tool was not available after loading the x64 MSVC environment."
        }
    }
}

$scriptRoot = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $scriptRoot '..\..')).Path
$pinsPath = Join-Path $scriptRoot 'pins.json'
$definitionFile = Join-Path $scriptRoot 'exports.def'
$smokeTestSource = Join-Path $scriptRoot 'smoke_test.c'
$noticePath = Join-Path $scriptRoot 'LICENSE.txt'
$downloadRoot = Join-Path $scriptRoot '.downloads'
$buildRoot = Join-Path $scriptRoot '.build'
$extractRoot = Join-Path $buildRoot 'source'
$objectRoot = Join-Path $buildRoot 'obj'
$stageRoot = Join-Path $buildRoot 'stage'
$outputRoot = Join-Path $repoRoot 'src\Muted.Audio.Windows\runtimes\win-x64\native'
$finalDll = Join-Path $outputRoot 'rnnoise.dll'
$finalChecksum = Join-Path $outputRoot 'rnnoise.dll.sha256'

$pins = Get-Content -LiteralPath $pinsPath -Raw | ConvertFrom-Json
if ($pins.schemaVersion -ne 1) {
    throw "Unsupported pin schema version: $($pins.schemaVersion)"
}
if ($pins.upstream.commit -notmatch '^[0-9a-f]{40}$') {
    throw 'The RNNoise commit pin is not a full lowercase Git object ID.'
}
if ($pins.upstream.sourceArchiveSha256 -notmatch '^[0-9a-f]{64}$' -or
    $pins.model.archiveSha256 -notmatch '^[0-9a-f]{64}$') {
    throw 'Every archive must have a full lowercase SHA-256 pin.'
}
if ($pins.model.variant -ne 'full' -or $pins.model.compiledSource -ne 'src/rnnoise_data.c') {
    throw 'This build intentionally requires the full embedded RNNoise model.'
}
if ($pins.build.architecture -ne 'x64' -or $pins.build.configuration -ne 'Release') {
    throw 'This script only builds the pinned Release x64 configuration.'
}

New-Item -ItemType Directory -Path $downloadRoot -Force | Out-Null
if (Test-Path -LiteralPath $buildRoot) {
    Remove-Item -LiteralPath $buildRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $extractRoot, $objectRoot, $stageRoot -Force | Out-Null

$sourceArchive = Join-Path $downloadRoot ("rnnoise-{0}.tar.gz" -f $pins.upstream.commit)
$modelArchive = Join-Path $downloadRoot ("rnnoise_data-{0}.tar.gz" -f $pins.model.version)

Get-VerifiedDownload -Uri $pins.upstream.sourceArchiveUrl -Destination $sourceArchive -ExpectedSha256 $pins.upstream.sourceArchiveSha256 -Description 'RNNoise source archive'
Get-VerifiedDownload -Uri $pins.model.archiveUrl -Destination $modelArchive -ExpectedSha256 $pins.model.archiveSha256 -Description 'RNNoise full-model archive'

$tarCommand = Get-Command tar.exe -ErrorAction SilentlyContinue
if ($null -eq $tarCommand) {
    throw 'tar.exe is required to extract the pinned .tar.gz archives.'
}

Invoke-Checked -FilePath $tarCommand.Source -Arguments @('-xzf', $sourceArchive, '-C', $extractRoot) -Description 'RNNoise source extraction'
$sourceRoot = Get-ChildItem -LiteralPath $extractRoot -Directory | Where-Object {
    Test-Path -LiteralPath (Join-Path $_.FullName 'include\rnnoise.h') -PathType Leaf
} | Select-Object -First 1
if ($null -eq $sourceRoot) {
    throw 'Could not locate include\rnnoise.h in the verified source archive.'
}
$sourceRoot = $sourceRoot.FullName

$upstreamModelVersion = (Get-Content -LiteralPath (Join-Path $sourceRoot 'model_version') -Raw).Trim()
if ($upstreamModelVersion -ne $pins.model.version) {
    throw "Pinned source selects model $upstreamModelVersion, but pins.json selects $($pins.model.version)."
}

Invoke-Checked -FilePath $tarCommand.Source -Arguments @(
    '-xzf', $modelArchive,
    '-C', $sourceRoot,
    $pins.model.compiledSource,
    $pins.model.compiledHeader
) -Description 'RNNoise full-model extraction'

Assert-FileHash -Path (Join-Path $sourceRoot $pins.model.compiledSource) -Expected $pins.model.compiledSourceSha256 -Description 'Extracted full-model source'
Assert-FileHash -Path (Join-Path $sourceRoot $pins.model.compiledHeader) -Expected $pins.model.compiledHeaderSha256 -Description 'Extracted full-model header'
Assert-FileHash -Path (Join-Path $sourceRoot 'COPYING') -Expected $pins.upstream.licenseFileSha256 -Description 'Upstream license'
Assert-FileHash -Path $noticePath -Expected $pins.upstream.licenseFileSha256 -Description 'Committed RNNoise license notice'

Import-MsvcX64Environment
$cl = (Get-Command cl.exe).Source
$link = (Get-Command link.exe).Source
$dumpbin = (Get-Command dumpbin.exe).Source
Write-Host "Building with MSVC toolset $env:VCToolsVersion ($cl)"

$commonSources = @(
    'src/denoise.c',
    'src/rnn.c',
    'src/pitch.c',
    'src/kiss_fft.c',
    'src/celt_lpc.c',
    'src/nnet.c',
    'src/nnet_default.c',
    'src/parse_lpcnet_weights.c',
    'src/rnnoise_data.c',
    'src/rnnoise_tables.c',
    'src/x86/x86_dnn_map.c',
    'src/x86/x86cpu.c'
)
$specializedSources = @(
    'src/x86/nnet_sse4_1.c',
    'src/x86/nnet_avx2.c'
)
$baseCompilerArguments = @(
    '/nologo',
    '/c',
    '/std:c11',
    '/O2',
    '/Ob2',
    '/Oi',
    '/Ot',
    '/GL',
    '/Gy',
    '/Gw',
    '/MT',
    '/fp:precise',
    '/W3',
    '/wd4244',
    '/wd4305',
    '/Brepro',
    '/DRNNOISE_BUILD',
    '/DDLL_EXPORT',
    '/DWIN32',
    '/D_CRT_SECURE_NO_WARNINGS',
    '/DRNN_ENABLE_X86_RTCD',
    '/DCPU_INFO_BY_ASM',
    '/DOPUS_X86_MAY_HAVE_SSE',
    '/DOPUS_X86_MAY_HAVE_SSE2',
    ("/I{0}" -f (Join-Path $sourceRoot 'include')),
    ("/I{0}" -f (Join-Path $sourceRoot 'src'))
)

$objects = New-Object System.Collections.Generic.List[string]
foreach ($relativeSource in ($commonSources + $specializedSources)) {
    $sourcePath = Join-Path $sourceRoot ($relativeSource -replace '/', '\')
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Pinned RNNoise source is missing: $relativeSource"
    }

    $objectName = (($relativeSource -replace '^src/', '') -replace '[\\/]', '_') -replace '\.c$', '.obj'
    $objectPath = Join-Path $objectRoot $objectName
    $arguments = New-Object System.Collections.Generic.List[string]
    $arguments.AddRange([string[]] $baseCompilerArguments)

    if ($relativeSource -eq 'src/x86/nnet_sse4_1.c') {
        $arguments.Add('/DOPUS_X86_MAY_HAVE_SSE4_1')
    }
    elseif ($relativeSource -eq 'src/x86/nnet_avx2.c') {
        $arguments.Add('/arch:AVX2')
        $arguments.Add('/DOPUS_X86_MAY_HAVE_SSE4_1')
    }

    $arguments.Add("/Fo$objectPath")
    $arguments.Add($sourcePath)
    Invoke-Checked -FilePath $cl -Arguments $arguments.ToArray() -Description "Compiling $relativeSource"
    $objects.Add($objectPath)
}

$stagedDll = Join-Path $stageRoot 'rnnoise.dll'
$importLibrary = Join-Path $stageRoot 'rnnoise.lib'
$linkArguments = New-Object System.Collections.Generic.List[string]
$linkArguments.AddRange([string[]] @(
    '/NOLOGO',
    '/DLL',
    '/MACHINE:X64',
    '/LTCG',
    '/OPT:REF',
    '/OPT:ICF',
    '/INCREMENTAL:NO',
    '/DYNAMICBASE',
    '/NXCOMPAT',
    '/HIGHENTROPYVA',
    '/Brepro',
    ("/DEF:{0}" -f $definitionFile),
    ("/OUT:{0}" -f $stagedDll),
    ("/IMPLIB:{0}" -f $importLibrary)
))
$linkArguments.AddRange($objects.ToArray())
Invoke-Checked -FilePath $link -Arguments $linkArguments.ToArray() -Description 'Linking rnnoise.dll'

$headers = (& $dumpbin /NOLOGO /HEADERS $stagedDll) -join "`n"
if ($LASTEXITCODE -ne 0) {
    throw "dumpbin /HEADERS failed with exit code $LASTEXITCODE."
}
if ($headers -notmatch '(?im)^\s*8664 machine \(x64\)') {
    throw 'The built RNNoise DLL is not an AMD64/x64 PE image.'
}

$exportOutput = & $dumpbin /NOLOGO /EXPORTS $stagedDll
if ($LASTEXITCODE -ne 0) {
    throw "dumpbin /EXPORTS failed with exit code $LASTEXITCODE."
}
$actualExports = @($exportOutput | ForEach-Object {
    if ($_ -match '^\s+\d+\s+[0-9A-Fa-f]+\s+[0-9A-Fa-f]+\s+(rnnoise_[A-Za-z0-9_]+)\s*$') {
        $Matches[1]
    }
})
$expectedExports = @($pins.build.expectedExports | ForEach-Object { [string] $_ })
$exportDifference = @(Compare-Object -ReferenceObject $expectedExports -DifferenceObject $actualExports)
if ($exportDifference.Count -ne 0) {
    $differenceText = ($exportDifference | Out-String).Trim()
    throw "rnnoise.dll exports differ from pins.json:`n$differenceText"
}
Write-Host ("Verified {0} stable RNNoise C exports." -f $actualExports.Count)

$smokeExecutable = Join-Path $objectRoot 'rnnoise_smoke_test.exe'
$smokeObject = Join-Path $objectRoot 'smoke_test.obj'
Invoke-Checked -FilePath $cl -Arguments @(
    '/nologo',
    '/std:c11',
    '/O2',
    '/MT',
    '/Brepro',
    ("/I{0}" -f (Join-Path $sourceRoot 'include')),
    ("/Fo{0}" -f $smokeObject),
    ("/Fe:{0}" -f $smokeExecutable),
    $smokeTestSource,
    $importLibrary,
    '/link',
    '/MACHINE:X64',
    '/INCREMENTAL:NO',
    '/Brepro'
) -Description 'Building native RNNoise smoke test'

$oldPath = $env:PATH
try {
    $env:PATH = "$stageRoot;$oldPath"
    Invoke-Checked -FilePath $smokeExecutable -Arguments @() -Description 'Native RNNoise smoke test'
}
finally {
    $env:PATH = $oldPath
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$pendingDll = "$finalDll.pending"
if (Test-Path -LiteralPath $pendingDll) {
    Remove-Item -LiteralPath $pendingDll -Force
}
Copy-Item -LiteralPath $stagedDll -Destination $pendingDll
Move-Item -LiteralPath $pendingDll -Destination $finalDll -Force

$stagedHash = Get-NormalizedSha256 -Path $stagedDll
$publishedHash = Get-NormalizedSha256 -Path $finalDll
if ($stagedHash -ne $publishedHash) {
    throw "Published DLL hash $publishedHash differs from staged DLL hash $stagedHash."
}

$pendingChecksum = "$finalChecksum.pending"
("{0}  rnnoise.dll`r`n" -f $publishedHash) | Set-Content -LiteralPath $pendingChecksum -Encoding Ascii -NoNewline
Move-Item -LiteralPath $pendingChecksum -Destination $finalChecksum -Force
$sidecarHash = ((Get-Content -LiteralPath $finalChecksum -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
if ($sidecarHash -ne (Get-NormalizedSha256 -Path $finalDll)) {
    throw 'The published rnnoise.dll.sha256 sidecar does not validate.'
}

Write-Host "Published $finalDll"
Write-Host "SHA-256: $publishedHash"

if (-not $KeepBuildTree -and (Test-Path -LiteralPath $buildRoot)) {
    Remove-Item -LiteralPath $buildRoot -Recurse -Force
}
if ($CleanDownloads -and (Test-Path -LiteralPath $downloadRoot)) {
    Remove-Item -LiteralPath $downloadRoot -Recurse -Force
}

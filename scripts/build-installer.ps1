[CmdletBinding()]
param(
    [switch] $SkipTests,
    [switch] $SkipPublish,
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version = '0.1.0'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$publishScript = Join-Path $PSScriptRoot 'publish.ps1'
$issScript = Join-Path $repoRoot 'installer\Muted.iss'
$publishOutput = Join-Path $repoRoot 'artifacts\Muted-win-x64'

if (-not $SkipPublish) {
    if ($SkipTests) {
        & $publishScript -SelfContained -SkipTests -Version $Version
    }
    else {
        & $publishScript -SelfContained -Version $Version
    }
    if ($LASTEXITCODE -ne 0) {
        throw "publish.ps1 failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $publishOutput 'Muted.exe'))) {
    throw "Publish output not found at $publishOutput. Run without -SkipPublish first."
}

function Find-InnoSetupCompiler {
    $onPath = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($onPath) {
        return $onPath.Source
    }

    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw 'Inno Setup compiler (ISCC.exe) was not found. Install it with: winget install JRSoftware.InnoSetup'
}

$iscc = Find-InnoSetupCompiler
Write-Host "Using Inno Setup compiler: $iscc"

& $iscc "/DMyAppVersion=$Version" $issScript
if ($LASTEXITCODE -ne 0) {
    throw "ISCC.exe failed with exit code $LASTEXITCODE."
}

$setupFiles = Get-ChildItem -Path (Join-Path $repoRoot 'artifacts') -Filter 'Muted-Setup-*.exe' |
    Sort-Object LastWriteTime -Descending
if (-not $setupFiles) {
    throw 'Inno Setup reported success but no Muted-Setup-*.exe was produced.'
}

Write-Host "Installer built: $($setupFiles[0].FullName)"

$installer = $setupFiles | Where-Object Name -EQ "Muted-Setup-$Version.exe" | Select-Object -First 1
if (-not $installer) {
    throw "Expected installer Muted-Setup-$Version.exe was not produced."
}

$checksumPath = $installer.FullName + '.sha256'
$checksum = (Get-FileHash -LiteralPath $installer.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($checksumPath, "$checksum  $($installer.Name)`n")
Write-Host "Checksum: $checksumPath"

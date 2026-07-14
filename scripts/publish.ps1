[CmdletBinding()]
param(
    [switch] $SelfContained,
    [switch] $SkipTests,
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version = '0.1.0'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solution = Join-Path $repoRoot 'Muted.sln'
$project = Join-Path $repoRoot 'src\Muted.App\Muted.App.csproj'
$artifactRoot = Join-Path $repoRoot 'artifacts'
$output = Join-Path $artifactRoot 'Muted-win-x64'
$archive = Join-Path $artifactRoot 'Muted-win-x64.zip'
$verifyScript = Join-Path $PSScriptRoot 'verify-package.ps1'

function Invoke-DotNet {
    param([Parameter(Mandatory = $true)][string[]] $Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
if (Test-Path -LiteralPath $output) {
    $resolvedArtifactRoot = [IO.Path]::GetFullPath($artifactRoot).TrimEnd('\') + '\'
    $resolvedOutput = (Resolve-Path -LiteralPath $output).Path
    if (-not $resolvedOutput.StartsWith($resolvedArtifactRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean output outside the artifact directory: $resolvedOutput"
    }

    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}

Invoke-DotNet -Arguments @('restore', $solution)
Invoke-DotNet -Arguments @(
    'build', $solution,
    '--configuration', 'Release',
    '--no-restore',
    "-p:Version=$Version",
    "-p:AssemblyVersion=$Version.0",
    "-p:FileVersion=$Version.0"
)
if (-not $SkipTests) {
    Invoke-DotNet -Arguments @('test', $solution, '--configuration', 'Release', '--no-build')
}

$selfContainedValue = if ($SelfContained) { 'true' } else { 'false' }
Invoke-DotNet -Arguments @(
    'publish', $project,
    '--configuration', 'Release',
    '--runtime', 'win-x64',
    '--self-contained', $selfContainedValue,
    '--output', $output,
    "-p:Version=$Version",
    "-p:AssemblyVersion=$Version.0",
    "-p:FileVersion=$Version.0",
    '-p:DebugSymbols=false',
    '-p:DebugType=None'
)

$executable = Join-Path $output 'Muted.exe'
$rnnoise = Join-Path $output 'rnnoise.dll'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf) -or
    -not (Test-Path -LiteralPath $rnnoise -PathType Leaf)) {
    throw 'Publish completed without Muted.exe or rnnoise.dll.'
}

& $verifyScript -PackagePath $output

$hashEntries = foreach ($relativePath in @('Muted.exe', 'Muted.dll', 'rnnoise.dll')) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $output $relativePath) -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $relativePath"
}
[IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS.txt'), $hashEntries)

if (Test-Path -LiteralPath $archive) {
    Remove-Item -LiteralPath $archive -Force
}
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $archive -CompressionLevel Optimal

Write-Host "Published Muted to $output"
Write-Host "Archive: $archive"
Write-Host ("Mode: " + $(if ($SelfContained) { 'self-contained' } else { 'framework-dependent (.NET 9 Desktop Runtime)' }))

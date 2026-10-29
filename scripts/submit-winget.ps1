<#
.SYNOPSIS
  Submits Muted to the Windows Package Manager community repository the first time.

.DESCRIPTION
  winget only takes a new package through a pull request on microsoft/winget-pkgs. This
  validates the manifests in packaging/winget, then lets wingetcreate open that pull
  request with a GitHub token that has the public_repo scope. Run it once, after the
  manifests are on the default branch. Later releases update winget by themselves when
  the WINGET_TOKEN secret exists.
#>
[CmdletBinding()]
param(
    [string] $Version = '0.6.0'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$manifests = Join-Path $root "packaging\winget\manifests\y\yyyutakaaa\Muted\$Version"
if (-not (Test-Path -LiteralPath $manifests)) {
    throw "No manifests for $Version at $manifests."
}

if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
    throw 'winget is not available on this PC.'
}

winget validate --manifest $manifests
if ($LASTEXITCODE -ne 0) {
    throw 'The manifests did not validate.'
}

if (-not (Get-Command wingetcreate -ErrorAction SilentlyContinue)) {
    winget install --id Microsoft.WingetCreate --exact --accept-package-agreements --accept-source-agreements
    Write-Host 'wingetcreate is installed. Open a new PowerShell window and run this script again.'
    exit 0
}

Start-Process 'https://github.com/settings/tokens/new?scopes=public_repo&description=Muted%20winget'
Write-Host 'Create a token on the page that just opened (scope public_repo) and paste it below.'
$secure = Read-Host 'Token' -AsSecureString
$token = [System.Net.NetworkCredential]::new('', $secure).Password
if (-not $token) {
    throw 'No token entered.'
}

wingetcreate submit --token $token $manifests
if ($LASTEXITCODE -ne 0) {
    throw "wingetcreate failed with exit code $LASTEXITCODE."
}

Write-Host 'Submitted. Microsoft reviews it, usually within a day or two. You can follow it on'
Write-Host 'https://github.com/microsoft/winget-pkgs/pulls'

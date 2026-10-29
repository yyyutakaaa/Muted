<#
.SYNOPSIS
  Does the one-time GitHub settings for Muted that cannot live in the repository.

.DESCRIPTION
  Needs the GitHub CLI (winget install GitHub.cli, then gh auth login). Every step asks
  first and can be skipped. Tokens are typed as hidden input and handed straight to
  "gh secret set"; they are never written to disk.

  1. Turns on GitHub Discussions.
  2. Turns on GitHub Pages, published from the Pages workflow.
  3. Stores the WINGET_TOKEN secret, so releases update winget by themselves.
  4. Stores the SignPath settings, once SignPath has approved the project.
#>
[CmdletBinding()]
param(
    [string] $Repository = 'yyyutakaaa/Muted'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

function Confirm-Step([string] $question) {
    $answer = Read-Host "$question (j/n)"
    return $answer -match '^(j|ja|y|yes)$'
}

function Read-Secret([string] $prompt) {
    $secure = Read-Host $prompt -AsSecureString
    return [System.Net.NetworkCredential]::new('', $secure).Password
}

function Set-RepositorySecret([string] $name, [string] $value) {
    $value | gh secret set $name --repo $Repository
    if ($LASTEXITCODE -ne 0) {
        throw "Could not store the secret $name."
    }
}

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Write-Host 'Install the GitHub CLI first:  winget install GitHub.cli'
    Write-Host 'Then sign in:                  gh auth login'
    exit 1
}

gh auth status
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Run "gh auth login" first.'
    exit 1
}

if (Confirm-Step 'Turn on GitHub Discussions') {
    gh api --method PATCH "repos/$Repository" -F has_discussions=true | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Write-Host 'Discussions are on. Create the categories "Q&A" and "Show your setup" at'
        Write-Host "https://github.com/$Repository/discussions/categories (the forms in .github/DISCUSSION_TEMPLATE match them)."
    }
}

if (Confirm-Step 'Turn on GitHub Pages (the website from docs/index.html)') {
    gh api --method POST "repos/$Repository/pages" -f build_type=workflow 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        # Already enabled: switch it to the workflow instead.
        gh api --method PUT "repos/$Repository/pages" -f build_type=workflow | Out-Null
    }

    if ($LASTEXITCODE -eq 0) {
        Write-Host 'Pages is on. Run the "Pages" workflow once from the Actions tab, or push a change to docs/.'
        Write-Host "The site will be at https://$($Repository.Split('/')[0].ToLower()).github.io/$($Repository.Split('/')[1])/"
    }
}

if (Confirm-Step 'Store a winget token so every release updates winget (the first submission is done with submit-winget.ps1)') {
    Start-Process 'https://github.com/settings/tokens/new?scopes=public_repo&description=Muted%20winget'
    Write-Host 'Create the token on the page that just opened (scope public_repo), then paste it here.'
    $token = Read-Secret 'Token'
    if ($token) {
        Set-RepositorySecret 'WINGET_TOKEN' $token
        Write-Host 'WINGET_TOKEN stored.'
    }
}

if (Confirm-Step 'Store the SignPath settings (only after SignPath approved the project)') {
    $organization = Read-Host 'SignPath organization ID'
    $apiToken = Read-Secret 'SignPath API token'
    if ($organization -and $apiToken) {
        gh variable set SIGNPATH_ORGANIZATION_ID --repo $Repository --body $organization
        Set-RepositorySecret 'SIGNPATH_API_TOKEN' $apiToken
        Write-Host 'SignPath is set. The next release signs the installer.'
    }
}
else {
    Write-Host 'Not approved yet? Apply at https://signpath.org/apply, see docs/MAINTAINING.md.'
}

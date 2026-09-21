[CmdletBinding()]
param(
    [switch]$SkipSync,
    [switch]$SkipTests,
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repositoryRoot
$gitSafeDirectory = $repositoryRoot.Replace('\', '/')
$gitOptions = @('-c', "safe.directory=$gitSafeDirectory")

function Invoke-Checked {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,
        [Parameter(ValueFromRemainingArguments)]
        [string[]]$ArgumentList
    )

    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($ArgumentList -join ' ')"
    }
}

if (-not $SkipSync) {
    $branchOutput = & git @gitOptions branch --show-current
    if ($LASTEXITCODE -ne 0) { throw 'Unable to read the current Git branch.' }
    $branch = ($branchOutput | Out-String).Trim()
    if ($branch -ne 'main') {
        throw "Expected branch 'main', but the current branch is '$branch'. Switch to main before building."
    }

    $changes = @(& git @gitOptions status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect the Git worktree.' }
    if ($changes.Count -gt 0) {
        throw "The worktree has local changes. Commit, stash, or remove them before syncing:`n$($changes -join "`n")"
    }

    Invoke-Checked git @gitOptions fetch origin main

    & git @gitOptions merge-base --is-ancestor HEAD origin/main
    if ($LASTEXITCODE -ne 0) {
        throw 'Local main contains commits that are not on origin/main, or the histories diverged. Resolve that explicitly before building.'
    }

    Invoke-Checked git @gitOptions merge --ff-only origin/main
}

$requiredSdk = (Get-Content (Join-Path $repositoryRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
try {
    $actualSdk = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'dotnet returned a nonzero exit code.' }
}
catch {
    throw "The required .NET SDK $requiredSdk is unavailable. Install it with: winget install --id Microsoft.DotNet.SDK.10 --exact --source winget"
}

if (-not $actualSdk.StartsWith('10.0.4')) {
    throw "This project requires a .NET 10.0.4xx SDK (pinned to $requiredSdk); dotnet selected $actualSdk."
}

if (-not $Version) {
    # Keep every numeric component within .NET assembly version limits.
    $Version = Get-Date -Format 'yyyy.MMdd.HHmm.ss'
}

$releaseParameters = @{
    Version = $Version
    NoUpload = $true
}
if ($SkipTests) { $releaseParameters.SkipTests = $true }

& (Join-Path $PSScriptRoot 'release.ps1') @releaseParameters
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }

$releaseName = "GameOverlay-$Version-win-x64"
$releaseRoot = Join-Path $repositoryRoot 'artifacts/releases'
$executablePath = Join-Path $releaseRoot "$releaseName/GameOverlay.exe"
$archivePath = Join-Path $releaseRoot "$releaseName.zip"

if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Expected executable was not created: $executablePath"
}
if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
    throw "Expected release archive was not created: $archivePath"
}

$hash = (Get-FileHash -LiteralPath $executablePath -Algorithm SHA256).Hash
Write-Host ''
Write-Host 'Ready to run:' -ForegroundColor Green
Write-Host "  EXE: $executablePath"
Write-Host "  ZIP: $archivePath"
Write-Host "  SHA256: $hash"

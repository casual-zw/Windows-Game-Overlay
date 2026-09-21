[CmdletBinding()]
param(
    [string]$Version,
    [string]$DriveFolder,
    [switch]$SkipTests,
    [switch]$NoUpload
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repositoryRoot

$localConfigPath = Join-Path $PSScriptRoot 'release.local.psd1'
$exampleConfigPath = Join-Path $PSScriptRoot 'release.local.psd1.example'

if ((-not $DriveFolder) -and (Test-Path $localConfigPath)) {
    $localConfig = Import-PowerShellDataFile $localConfigPath
    $DriveFolder = $localConfig.DriveFolder
}

if (-not $Version) {
    $Version = Get-Date -Format 'yyyy.MM.dd.HHmm'
}

if ($Version -notmatch '^[0-9]+(\.[0-9A-Za-z-]+){1,3}$') {
    throw "Version '$Version' is invalid. Use a value such as 0.1.0 or 2026.09.20.1430."
}

if (-not $SkipTests) {
    & dotnet run --project tests/Overlay.Core.Tests/Overlay.Core.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
}

$releaseRoot = Join-Path $repositoryRoot 'artifacts/releases'
$releaseName = "GameOverlay-$Version-win-x64"
$releaseDirectory = Join-Path $releaseRoot $releaseName
$executablePath = Join-Path $releaseDirectory 'GameOverlay.exe'
$archivePath = Join-Path $releaseRoot "$releaseName.zip"

if ((Test-Path $releaseDirectory) -or (Test-Path $archivePath)) {
    throw "Release '$Version' already exists. Choose -Version with a new value."
}

New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null

try {
    & dotnet publish src/Overlay.Windows/Overlay.Windows.csproj `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -o $releaseDirectory `
        "-p:Version=$Version" `
        '-p:PublishSingleFile=true' `
        '-p:IncludeNativeLibrariesForSelfExtract=true'
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

    if (-not (Test-Path $executablePath)) {
        throw "Expected executable was not created: $executablePath"
    }

    Copy-Item docs/WINDOWS-TEST.md (Join-Path $releaseDirectory 'READ-ME-FIRST.md')
    $releaseInfo = [ordered]@{
        version = $Version
        createdUtc = (Get-Date).ToUniversalTime().ToString('o')
        executable = (Split-Path $executablePath -Leaf)
        sha256 = (Get-FileHash $executablePath -Algorithm SHA256).Hash
    } | ConvertTo-Json
    Set-Content -Path (Join-Path $releaseDirectory 'release.json') -Value $releaseInfo -Encoding UTF8
    Compress-Archive -Path "$releaseDirectory/*" -DestinationPath $archivePath -Force

    if (-not $NoUpload) {
        if (-not $DriveFolder) {
            throw "No Drive folder is configured. Copy $exampleConfigPath to $localConfigPath and set DriveFolder once, or pass -DriveFolder. Use -NoUpload to package without uploading."
        }

        if (-not (Test-Path -LiteralPath $DriveFolder -PathType Container)) {
            throw "Configured Drive folder does not exist: $DriveFolder"
        }

        Copy-Item -LiteralPath $archivePath -Destination (Join-Path $DriveFolder (Split-Path $archivePath -Leaf)) -Force
        Write-Host "Uploaded to Google Drive sync folder: $DriveFolder"
    }

    Write-Host "Release executable: $executablePath"
    Write-Host "Release archive: $archivePath"
}
catch {
    Write-Warning "The local release files were kept at '$releaseRoot' for inspection or manual upload."
    throw
}

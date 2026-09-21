param([switch]$Publish, [switch]$Zip)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

& dotnet run --project tests/Overlay.Core.Tests/Overlay.Core.Tests.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
& dotnet build src/Overlay.Windows/Overlay.Windows.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Windows build failed.' }

if ($Publish -or $Zip) {
    $outputDir = Join-Path (Get-Location) 'artifacts/win-x64'
    if (Test-Path $outputDir) { Remove-Item $outputDir -Recurse -Force }
    & dotnet publish src/Overlay.Windows/Overlay.Windows.csproj -c Release -r win-x64 --self-contained true -o $outputDir
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item docs/WINDOWS-TEST.md (Join-Path $outputDir 'READ-ME-FIRST.md')
    Write-Host "Portable Windows build: $outputDir"
    if ($Zip) {
        $archive = Join-Path (Get-Location) 'artifacts/GameOverlay-win-x64.zip'
        Compress-Archive -Path "$outputDir/*" -DestinationPath $archive -Force
        Write-Host "Windows package: $archive"
    }
}

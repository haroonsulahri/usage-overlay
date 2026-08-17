$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$projectRoot = Split-Path -Parent $PSScriptRoot
$overlayProject = Join-Path $projectRoot 'src\QuotaRail\QuotaRail.csproj'
$outputDirectory = Join-Path $projectRoot 'artifacts\win-x64'
$legacyExecutable = Join-Path $outputDirectory 'CodexUsageOverlay.exe'

& (Join-Path $PSScriptRoot 'build.ps1')
if (Test-Path -LiteralPath $legacyExecutable) {
    Remove-Item -LiteralPath $legacyExecutable -Force
}
dotnet publish $overlayProject `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --output $outputDirectory `
    -p:PublishSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false

Write-Host "Package created at $outputDirectory"

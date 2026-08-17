$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$projectRoot = Split-Path -Parent $PSScriptRoot
$overlayProject = Join-Path $projectRoot 'src\CodexUsageOverlay\CodexUsageOverlay.csproj'
$outputDirectory = Join-Path $projectRoot 'artifacts\win-x64'

& (Join-Path $PSScriptRoot 'build.ps1')
dotnet publish $overlayProject `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --output $outputDirectory `
    -p:PublishSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false

Write-Host "Package created at $outputDirectory"

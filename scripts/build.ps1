$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$projectRoot = Split-Path -Parent $PSScriptRoot
$overlayProject = Join-Path $projectRoot 'src\QuotaRail\QuotaRail.csproj'
$specProject = Join-Path $projectRoot 'tests\CodexUsage.Core.Specs\CodexUsage.Core.Specs.csproj'

& (Join-Path $PSScriptRoot 'generate-icon.ps1')
& (Join-Path $PSScriptRoot 'verify-icon.ps1')
dotnet restore $overlayProject
dotnet restore $specProject
dotnet build $overlayProject --configuration Release --no-restore
dotnet run --project $specProject --configuration Release --no-restore

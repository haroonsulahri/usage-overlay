$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$projectRoot = Split-Path -Parent $PSScriptRoot
$overlayProject = Join-Path $projectRoot 'src\CodexUsageOverlay\CodexUsageOverlay.csproj'
$specProject = Join-Path $projectRoot 'tests\CodexUsage.Core.Specs\CodexUsage.Core.Specs.csproj'

dotnet restore $overlayProject
dotnet restore $specProject
dotnet build $overlayProject --configuration Release --no-restore
dotnet run --project $specProject --configuration Release --no-restore

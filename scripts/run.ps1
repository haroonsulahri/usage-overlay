$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$projectRoot = Split-Path -Parent $PSScriptRoot
$overlayProject = Join-Path $projectRoot 'src\QuotaRail\QuotaRail.csproj'

dotnet run --project $overlayProject --configuration Release

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$projectRoot = Split-Path -Parent $PSScriptRoot
$smokeProject = Join-Path $projectRoot 'tests\CodexUsage.LiveSmoke\CodexUsage.LiveSmoke.csproj'

dotnet run --project $smokeProject --configuration Release


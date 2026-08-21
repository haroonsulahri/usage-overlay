param(
    [string]$Version = (Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'VERSION') -Raw).Trim(),
    [string]$PackageDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\release-work\UsageOverlay'),
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\release')
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

if ($Version -notmatch '^\d+\.\d+\.\d+(?:[-.][0-9A-Za-z.-]+)?$') {
    throw "Invalid semantic version: $Version"
}

$projectRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$packageDirectory = [System.IO.Path]::GetFullPath($PackageDirectory)
$outputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$installerDefinition = Join-Path $projectRoot 'installer\UsageOverlay.iss'
$requiredPackageFiles = @(
    'UsageOverlay.exe',
    'README.md',
    'LICENSE',
    'CHANGELOG.md',
    'SECURITY.md',
    'VERSION',
    'assets\UsageOverlay.ico'
)

$missingFiles = $requiredPackageFiles | Where-Object {
    -not (Test-Path -LiteralPath (Join-Path $packageDirectory $_))
}
if ($missingFiles.Count -gt 0) {
    throw "Installer source is missing: $($missingFiles -join ', ')"
}

$compilerCandidates = @(
    $env:INNO_SETUP_COMPILER,
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compiler) {
    $command = Get-Command 'iscc.exe' -ErrorAction SilentlyContinue
    $compiler = $command.Source
}
if (-not $compiler) {
    throw 'Inno Setup 6 was not found. Install it or set INNO_SETUP_COMPILER to ISCC.exe.'
}

[void](New-Item -ItemType Directory -Path $outputDirectory -Force)
& $compiler `
    "/DAppVersion=$Version" `
    "/DPackageDirectory=$packageDirectory" `
    "/DOutputDirectory=$outputDirectory" `
    $installerDefinition

$installerPath = Join-Path $outputDirectory "usage-overlay-v$Version-win-x64-setup.exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Installer was not created at $installerPath"
}

Write-Host "Installer created at $installerPath"

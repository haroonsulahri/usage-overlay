param(
    [string]$Version = (Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'VERSION') -Raw).Trim()
)

$ErrorActionPreference = 'Stop'

$projectRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$releaseDirectory = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot 'release'))
$validationDirectory = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "validation\v$Version"))
$requiredPrefix = $artifactsRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar
if (-not $validationDirectory.StartsWith($requiredPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use a validation directory outside artifacts: $validationDirectory"
}

$archiveName = "codex-usage-overlay-v$Version-win-x64.zip"
$archivePath = Join-Path $releaseDirectory $archiveName
$checksumPath = Join-Path $releaseDirectory 'SHA256SUMS.txt'
if (-not (Test-Path -LiteralPath $archivePath) -or -not (Test-Path -LiteralPath $checksumPath)) {
    throw 'Release archive or checksum file is missing. Run package-release.ps1 first.'
}

$expectedHash = ((Get-Content -LiteralPath $checksumPath -Raw).Trim() -split '\s+')[0]
$actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $expectedHash) {
    throw "SHA-256 mismatch. Expected $expectedHash, got $actualHash."
}

if (Test-Path -LiteralPath $validationDirectory) {
    Remove-Item -LiteralPath $validationDirectory -Recurse -Force
}
[void](New-Item -ItemType Directory -Path $validationDirectory -Force)
Expand-Archive -LiteralPath $archivePath -DestinationPath $validationDirectory -Force

$packageDirectory = Join-Path $validationDirectory 'CodexUsageOverlay'
$requiredFiles = @(
    'CodexUsageOverlay.exe',
    'README.md',
    'LICENSE',
    'CHANGELOG.md',
    'SECURITY.md',
    'VERSION',
    'assets\icon.svg',
    'docs\architecture.md',
    'docs\privacy.md',
    'docs\images\settings.png',
    'scripts\install-start-menu.ps1',
    'scripts\uninstall-start-menu.ps1',
    'scripts\install-startup.ps1',
    'scripts\uninstall-startup.ps1'
)

$missingFiles = $requiredFiles | Where-Object {
    -not (Test-Path -LiteralPath (Join-Path $packageDirectory $_))
}
if ($missingFiles.Count -gt 0) {
    throw "Release package is missing: $($missingFiles -join ', ')"
}

$executable = Join-Path $packageDirectory 'CodexUsageOverlay.exe'
$versionInfo = (Get-Item -LiteralPath $executable).VersionInfo

[pscustomobject]@{
    Archive = $archivePath
    ArchiveSizeBytes = (Get-Item -LiteralPath $archivePath).Length
    SHA256 = $actualHash
    ExtractedDirectory = $packageDirectory
    ExtractedFileCount = (Get-ChildItem -LiteralPath $packageDirectory -Recurse -File).Count
    FileVersion = $versionInfo.FileVersion
    ProductVersion = $versionInfo.ProductVersion
}


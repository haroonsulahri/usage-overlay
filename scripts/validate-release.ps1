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

$archiveName = "usage-overlay-v$Version-win-x64.zip"
$archivePath = Join-Path $releaseDirectory $archiveName
$installerName = "usage-overlay-v$Version-win-x64-setup.exe"
$installerPath = Join-Path $releaseDirectory $installerName
$checksumPath = Join-Path $releaseDirectory 'SHA256SUMS.txt'
$manifestPath = Join-Path $releaseDirectory 'release-manifest.json'
if (-not (Test-Path -LiteralPath $archivePath) -or
    -not (Test-Path -LiteralPath $installerPath) -or
    -not (Test-Path -LiteralPath $checksumPath) -or
    -not (Test-Path -LiteralPath $manifestPath)) {
    throw 'Release installer, portable archive, checksum, or manifest is missing. Run package-release.ps1 first.'
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.product -ne 'Usage Overlay' -or $manifest.publisher -ne 'Haroone.com') {
    throw 'Release manifest contains unexpected product or publisher metadata.'
}
if ($manifest.installer -ne $installerName -or $manifest.portableArchive -ne $archiveName) {
    throw 'Release manifest contains unexpected artifact names.'
}

$expectedHashes = @{}
foreach ($line in Get-Content -LiteralPath $checksumPath) {
    if ($line -match '^([0-9a-fA-F]{64})\s+(.+)$') {
        $expectedHashes[$Matches[2]] = $Matches[1].ToLowerInvariant()
    }
}
$actualArchiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$actualInstallerHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($expectedHashes[$archiveName] -ne $actualArchiveHash) {
    throw "Portable archive SHA-256 mismatch. Expected $($expectedHashes[$archiveName]), got $actualArchiveHash."
}
if ($expectedHashes[$installerName] -ne $actualInstallerHash) {
    throw "Installer SHA-256 mismatch. Expected $($expectedHashes[$installerName]), got $actualInstallerHash."
}
if ($manifest.portableArchiveSha256 -ne $actualArchiveHash -or
    $manifest.installerSha256 -ne $actualInstallerHash) {
    throw 'Release manifest SHA-256 values do not match the generated artifacts.'
}

if (Test-Path -LiteralPath $validationDirectory) {
    Remove-Item -LiteralPath $validationDirectory -Recurse -Force
}
[void](New-Item -ItemType Directory -Path $validationDirectory -Force)
Expand-Archive -LiteralPath $archivePath -DestinationPath $validationDirectory -Force

$packageDirectory = Join-Path $validationDirectory 'UsageOverlay'
$requiredFiles = @(
    'UsageOverlay.exe',
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

$executable = Join-Path $packageDirectory 'UsageOverlay.exe'
$versionInfo = (Get-Item -LiteralPath $executable).VersionInfo
if ($versionInfo.ProductName -ne 'Usage Overlay' -or $versionInfo.CompanyName -ne 'Haroone.com') {
    throw 'Executable contains unexpected product or publisher metadata.'
}

[pscustomobject]@{
    Installer = $installerPath
    InstallerSizeBytes = (Get-Item -LiteralPath $installerPath).Length
    InstallerSHA256 = $actualInstallerHash
    InstallerSignature = (Get-AuthenticodeSignature -LiteralPath $installerPath).Status
    Archive = $archivePath
    ArchiveSizeBytes = (Get-Item -LiteralPath $archivePath).Length
    ArchiveSHA256 = $actualArchiveHash
    ExtractedDirectory = $packageDirectory
    ExtractedFileCount = (Get-ChildItem -LiteralPath $packageDirectory -Recurse -File).Count
    FileVersion = $versionInfo.FileVersion
    ProductVersion = $versionInfo.ProductVersion
    ProductName = $versionInfo.ProductName
    CompanyName = $versionInfo.CompanyName
}

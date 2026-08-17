param(
    [string]$Version = (Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'VERSION') -Raw).Trim()
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

if ($Version -notmatch '^\d+\.\d+\.\d+(?:[-.][0-9A-Za-z.-]+)?$') {
    throw "Invalid semantic version: $Version"
}

$projectRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$workRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot 'release-work'))
$publishDirectory = Join-Path $workRoot 'publish'
$packageDirectory = Join-Path $workRoot 'CodexUsageOverlay'
$releaseDirectory = Join-Path $artifactsRoot 'release'
$overlayProject = Join-Path $projectRoot 'src\CodexUsageOverlay\CodexUsageOverlay.csproj'

function Reset-ReleaseDirectory {
    param([Parameter(Mandatory)][string]$Path)

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $requiredPrefix = $artifactsRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedPath.StartsWith($requiredPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to reset a directory outside artifacts: $resolvedPath"
    }

    if (Test-Path -LiteralPath $resolvedPath) {
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }
    [void](New-Item -ItemType Directory -Path $resolvedPath -Force)
}

& (Join-Path $PSScriptRoot 'build.ps1')
Reset-ReleaseDirectory -Path $workRoot
Reset-ReleaseDirectory -Path $releaseDirectory
[void](New-Item -ItemType Directory -Path $publishDirectory -Force)
[void](New-Item -ItemType Directory -Path $packageDirectory -Force)
[void](New-Item -ItemType Directory -Path (Join-Path $packageDirectory 'scripts') -Force)

dotnet publish $overlayProject `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDirectory `
    -p:Version=$Version `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false

Copy-Item -LiteralPath (Join-Path $publishDirectory 'CodexUsageOverlay.exe') -Destination $packageDirectory
foreach ($document in @(
    'README.md',
    'LICENSE',
    'CHANGELOG.md',
    'SECURITY.md',
    'CONTRIBUTING.md',
    'CODE_OF_CONDUCT.md',
    'SUPPORT.md',
    'VERSION'
)) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $document) -Destination $packageDirectory
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets') -Destination $packageDirectory -Recurse
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $packageDirectory -Recurse
foreach ($script in @(
    'install-start-menu.ps1',
    'uninstall-start-menu.ps1',
    'install-startup.ps1',
    'uninstall-startup.ps1'
)) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $script) -Destination (Join-Path $packageDirectory 'scripts')
}

$archiveName = "codex-usage-overlay-v$Version-win-x64.zip"
$archivePath = Join-Path $releaseDirectory $archiveName
if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
Compress-Archive -Path $packageDirectory -DestinationPath $archivePath -CompressionLevel Optimal

$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = Join-Path $releaseDirectory 'SHA256SUMS.txt'
Set-Content -LiteralPath $checksumPath -Value "$hash  $archiveName" -Encoding ascii

$manifest = [ordered]@{
    version = $Version
    runtime = 'win-x64'
    selfContained = $true
    archive = $archiveName
    sha256 = $hash
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $releaseDirectory 'release-manifest.json') -Encoding utf8

Write-Host "Release package: $archivePath"
Write-Host "SHA-256: $hash"

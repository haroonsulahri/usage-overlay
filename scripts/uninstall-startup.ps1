$ErrorActionPreference = 'Stop'

$startupDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::Startup)
$shortcutPath = Join-Path $startupDirectory 'Codex Usage Overlay.lnk'

if (Test-Path -LiteralPath $shortcutPath) {
    Remove-Item -LiteralPath $shortcutPath -Force
    Write-Host "Automatic startup disabled. Removed $shortcutPath"
}
else {
    Write-Host 'Codex Usage Overlay automatic startup is not enabled.'
}


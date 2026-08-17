$ErrorActionPreference = 'Stop'

$programsDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$shortcutPath = Join-Path $programsDirectory 'Codex Usage Overlay.lnk'

if (Test-Path -LiteralPath $shortcutPath) {
    Remove-Item -LiteralPath $shortcutPath -Force
    Write-Host "Removed $shortcutPath"
}
else {
    Write-Host 'Codex Usage Overlay Start-menu shortcut is not installed.'
}


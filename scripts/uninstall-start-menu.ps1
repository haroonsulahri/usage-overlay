$ErrorActionPreference = 'Stop'

$programsDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$shortcutPaths = @(
    (Join-Path $programsDirectory 'Usage Overlay.lnk'),
    (Join-Path $programsDirectory 'QuotaRail for Codex.lnk'),
    (Join-Path $programsDirectory 'Codex Usage Overlay.lnk')
)
$removed = $false

foreach ($shortcutPath in $shortcutPaths) {
    if (Test-Path -LiteralPath $shortcutPath) {
        Remove-Item -LiteralPath $shortcutPath -Force
        Write-Host "Removed $shortcutPath"
        $removed = $true
    }
}

if (-not $removed) {
    Write-Host 'Usage Overlay Start-menu shortcut is not installed.'
}

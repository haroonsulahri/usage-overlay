$ErrorActionPreference = 'Stop'

$startupDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::Startup)
$shortcutPaths = @(
    (Join-Path $startupDirectory 'QuotaRail for Codex.lnk'),
    (Join-Path $startupDirectory 'Codex Usage Overlay.lnk')
)
$removed = $false

foreach ($shortcutPath in $shortcutPaths) {
    if (Test-Path -LiteralPath $shortcutPath) {
        Remove-Item -LiteralPath $shortcutPath -Force
        Write-Host "Automatic startup disabled. Removed $shortcutPath"
        $removed = $true
    }
}

if (-not $removed) {
    Write-Host 'QuotaRail for Codex automatic startup is not enabled.'
}

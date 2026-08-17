$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$releaseExecutable = Join-Path $projectRoot 'QuotaRail.exe'
$sourceExecutable = Join-Path $projectRoot 'artifacts\win-x64\QuotaRail.exe'
$executable = if (Test-Path -LiteralPath $releaseExecutable) {
    $releaseExecutable
}
else {
    $sourceExecutable
}
if (-not (Test-Path -LiteralPath $executable)) {
    throw 'QuotaRail.exe was not found beside scripts or under artifacts\win-x64.'
}

$programsDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$shortcutPath = Join-Path $programsDirectory 'QuotaRail for Codex.lnk'
$legacyShortcutPath = Join-Path $programsDirectory 'Codex Usage Overlay.lnk'
$shell = New-Object -ComObject WScript.Shell

try {
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $executable
    $shortcut.WorkingDirectory = Split-Path -Parent $executable
    $shortcut.Description = 'Show QuotaRail for Codex'
    $shortcut.IconLocation = "$executable,0"
    $shortcut.Save()
}
finally {
    if ($shortcut) {
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
    }
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
}

if (Test-Path -LiteralPath $legacyShortcutPath) {
    Remove-Item -LiteralPath $legacyShortcutPath -Force
}

Write-Host "Start-menu shortcut created at $shortcutPath"

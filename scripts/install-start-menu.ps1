$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $projectRoot 'artifacts\win-x64\CodexUsageOverlay.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    throw 'Packaged executable not found. Run scripts\package.ps1 first.'
}

$programsDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$shortcutPath = Join-Path $programsDirectory 'Codex Usage Overlay.lnk'
$shell = New-Object -ComObject WScript.Shell

try {
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $executable
    $shortcut.WorkingDirectory = Split-Path -Parent $executable
    $shortcut.Description = 'Show the Codex usage overlay'
    $shortcut.IconLocation = "$executable,0"
    $shortcut.Save()
}
finally {
    if ($shortcut) {
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
    }
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
}

Write-Host "Start-menu shortcut created at $shortcutPath"


$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $projectRoot 'artifacts\win-x64\CodexUsageOverlay.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    throw 'Packaged executable not found. Run scripts\package.ps1 first.'
}

$startupDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::Startup)
$shortcutPath = Join-Path $startupDirectory 'Codex Usage Overlay.lnk'
$shell = New-Object -ComObject WScript.Shell

try {
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $executable
    $shortcut.WorkingDirectory = Split-Path -Parent $executable
    $shortcut.Description = 'Start the Codex usage overlay automatically'
    $shortcut.IconLocation = "$executable,0"
    $shortcut.Save()
}
finally {
    if ($shortcut) {
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
    }
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
}

Write-Host "Automatic startup enabled at $shortcutPath"


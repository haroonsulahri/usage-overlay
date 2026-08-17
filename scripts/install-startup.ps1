$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$releaseExecutable = Join-Path $projectRoot 'CodexUsageOverlay.exe'
$sourceExecutable = Join-Path $projectRoot 'artifacts\win-x64\CodexUsageOverlay.exe'
$executable = if (Test-Path -LiteralPath $releaseExecutable) {
    $releaseExecutable
}
else {
    $sourceExecutable
}
if (-not (Test-Path -LiteralPath $executable)) {
    throw 'CodexUsageOverlay.exe was not found beside scripts or under artifacts\win-x64.'
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

param(
    [string]$Version = (Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'VERSION') -Raw).Trim()
)

$ErrorActionPreference = 'Stop'

$projectRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$installerPath = Join-Path $artifactsRoot "release\usage-overlay-v$Version-win-x64-setup.exe"
$testRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot 'installer-smoke'))
$installDirectory = Join-Path $testRoot 'installed'
$installLog = Join-Path $testRoot 'install.log'
$uninstallLog = Join-Path $testRoot 'uninstall.log'
$startupDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::Startup)
$startupShortcut = Join-Path $startupDirectory 'Usage Overlay.lnk'
$startupBackup = Join-Path $testRoot 'Usage Overlay.lnk.backup'
$programsDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$startMenuShortcut = Join-Path $programsDirectory 'Usage Overlay.lnk'
$uninstallRegistryPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*'
$requiredPrefix = $artifactsRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar

if (-not $testRoot.StartsWith($requiredPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use a test directory outside artifacts: $testRoot"
}
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Installer not found: $installerPath"
}

$previousProcess = Get-CimInstance Win32_Process -Filter "Name = 'UsageOverlay.exe'" |
    Select-Object -First 1 ExecutablePath, CommandLine
$uninstallerPath = $null

if (Test-Path -LiteralPath $testRoot) {
    Remove-Item -LiteralPath $testRoot -Recurse -Force
}
[void](New-Item -ItemType Directory -Path $testRoot -Force)
if (Test-Path -LiteralPath $startupShortcut) {
    Copy-Item -LiteralPath $startupShortcut -Destination $startupBackup
}

try {
    Get-Process -Name 'UsageOverlay' -ErrorAction SilentlyContinue | Stop-Process -Force

    $install = Start-Process -FilePath $installerPath -ArgumentList @(
        '/VERYSILENT',
        '/SUPPRESSMSGBOXES',
        '/NORESTART',
        "/DIR=$installDirectory",
        "/LOG=$installLog"
    ) -Wait -PassThru
    if ($install.ExitCode -ne 0) {
        throw "Installer exited with code $($install.ExitCode)."
    }

    $registration = Get-ItemProperty -Path $uninstallRegistryPath -ErrorAction SilentlyContinue |
        Where-Object DisplayName -eq 'Usage Overlay' |
        Select-Object -First 1
    if (-not $registration) {
        throw 'Usage Overlay was not registered in Windows Installed apps.'
    }

    $installedExecutable = Join-Path $installDirectory 'UsageOverlay.exe'
    if (-not (Test-Path -LiteralPath $installedExecutable)) {
        throw 'The installed executable is missing.'
    }
    if (-not (Test-Path -LiteralPath $startMenuShortcut)) {
        throw 'The Start Menu shortcut is missing.'
    }

    $launched = Start-Process -FilePath $installedExecutable -ArgumentList '--settings' -PassThru
    Start-Sleep -Seconds 3
    $runningInstalledProcess = Get-CimInstance Win32_Process -Filter "ProcessId = $($launched.Id)"
    if (-not $runningInstalledProcess -or
        -not [System.IO.Path]::GetFullPath($runningInstalledProcess.ExecutablePath).Equals(
            [System.IO.Path]::GetFullPath($installedExecutable),
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Usage Overlay did not launch from the installed directory.'
    }
    Stop-Process -Id $launched.Id -Force

    $uninstaller = Get-ChildItem -LiteralPath $installDirectory -Filter 'unins*.exe' -File |
        Select-Object -First 1
    if (-not $uninstaller) {
        throw 'The installer did not create an uninstaller.'
    }
    $uninstallerPath = $uninstaller.FullName
    $uninstall = Start-Process -FilePath $uninstallerPath -ArgumentList @(
        '/VERYSILENT',
        '/SUPPRESSMSGBOXES',
        '/NORESTART',
        "/LOG=$uninstallLog"
    ) -Wait -PassThru
    if ($uninstall.ExitCode -ne 0) {
        throw "Uninstaller exited with code $($uninstall.ExitCode)."
    }

    $remainingRegistration = Get-ItemProperty -Path $uninstallRegistryPath -ErrorAction SilentlyContinue |
        Where-Object DisplayName -eq 'Usage Overlay'
    if ($remainingRegistration) {
        throw 'Usage Overlay remains registered after uninstall.'
    }
    if (Test-Path -LiteralPath $installedExecutable) {
        throw 'The executable remains after uninstall.'
    }
    if (Test-Path -LiteralPath $startMenuShortcut) {
        throw 'The Start Menu shortcut remains after uninstall.'
    }
    if (Test-Path -LiteralPath $startupShortcut) {
        throw 'The startup shortcut remains after uninstall.'
    }

    [pscustomobject]@{
        Version = $Version
        InstallerExitCode = $install.ExitCode
        RegisteredDisplayName = $registration.DisplayName
        RegisteredDisplayVersion = $registration.DisplayVersion
        InstalledExecutable = $installedExecutable
        LaunchVerified = $true
        UninstallerExitCode = $uninstall.ExitCode
        RegistrationRemoved = $true
        FilesRemoved = $true
        ShortcutsRemoved = $true
    }
}
finally {
    Get-Process -Name 'UsageOverlay' -ErrorAction SilentlyContinue | Stop-Process -Force
    $remainingRegistration = Get-ItemProperty -Path $uninstallRegistryPath -ErrorAction SilentlyContinue |
        Where-Object DisplayName -eq 'Usage Overlay' |
        Select-Object -First 1
    if ($remainingRegistration) {
        if (-not $uninstallerPath) {
            $uninstallerPath = Get-ChildItem -LiteralPath $installDirectory -Filter 'unins*.exe' -File -ErrorAction SilentlyContinue |
                Select-Object -First 1 -ExpandProperty FullName
        }
        if ($uninstallerPath -and (Test-Path -LiteralPath $uninstallerPath)) {
            Start-Process -FilePath $uninstallerPath -ArgumentList @(
                '/VERYSILENT',
                '/SUPPRESSMSGBOXES',
                '/NORESTART'
            ) -Wait
        }
    }
    if (Test-Path -LiteralPath $startupBackup) {
        Copy-Item -LiteralPath $startupBackup -Destination $startupShortcut -Force
    }
    if ($previousProcess.ExecutablePath -and (Test-Path -LiteralPath $previousProcess.ExecutablePath)) {
        Start-Process -FilePath $previousProcess.ExecutablePath
    }
}

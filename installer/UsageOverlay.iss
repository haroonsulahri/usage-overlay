#ifndef AppVersion
  #error AppVersion must be provided.
#endif

#ifndef PackageDirectory
  #error PackageDirectory must be provided.
#endif

#ifndef OutputDirectory
  #error OutputDirectory must be provided.
#endif

[Setup]
AppId={{77D695F8-E781-429C-9AAD-D4C07285B9D0}
AppName=Usage Overlay
AppVersion={#AppVersion}
AppVerName=Usage Overlay {#AppVersion}
AppPublisher=Haroone.com
AppPublisherURL=https://haroone.com
AppSupportURL=https://github.com/haroonsulahri/usage-overlay/issues
AppUpdatesURL=https://github.com/haroonsulahri/usage-overlay/releases
DefaultDirName={localappdata}\Programs\Usage Overlay
DefaultGroupName=Usage Overlay
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#OutputDirectory}
OutputBaseFilename=usage-overlay-v{#AppVersion}-win-x64-setup
SetupIconFile={#PackageDirectory}\assets\UsageOverlay.ico
UninstallDisplayIcon={app}\UsageOverlay.exe
UninstallDisplayName=Usage Overlay
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
LicenseFile={#PackageDirectory}\LICENSE

[Files]
Source: "{#PackageDirectory}\UsageOverlay.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDirectory}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDirectory}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDirectory}\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDirectory}\SECURITY.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDirectory}\VERSION"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDirectory}\assets\*"; DestDir: "{app}\assets"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PackageDirectory}\docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Usage Overlay"; Filename: "{app}\UsageOverlay.exe"; Parameters: "--settings"; WorkingDir: "{app}"; IconFilename: "{app}\UsageOverlay.exe"

[Run]
Filename: "{app}\UsageOverlay.exe"; Description: "Launch Usage Overlay"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{userstartup}\Usage Overlay.lnk"
Type: files; Name: "{userstartup}\QuotaRail for Codex.lnk"
Type: files; Name: "{userstartup}\Codex Usage Overlay.lnk"
Type: files; Name: "{userprograms}\QuotaRail for Codex.lnk"
Type: files; Name: "{userprograms}\Codex Usage Overlay.lnk"

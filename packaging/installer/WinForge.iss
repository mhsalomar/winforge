#define MyAppName "WinForge"
#define MyAppVersion "1.0.0"
[Setup]
AppId={{7D813C21-2EC5-4D55-92BC-A6DC5BE30D17}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\WinForge
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\..\artifacts
OutputBaseFilename=WinForge-{#MyAppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
UninstallDisplayIcon={app}\WinForge.UI.exe
[Files]
Source: "..\..\artifacts\portable-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{autoprograms}\WinForge"; Filename: "{app}\WinForge.UI.exe"
Name: "{autodesktop}\WinForge"; Filename: "{app}\WinForge.UI.exe"; Tasks: desktopicon
[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

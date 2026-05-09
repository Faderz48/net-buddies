#define AppName "Net Buddies"
#define AppVersion "1.4"
#define Publisher "Net Buddies"
#define SourceDir "..\publish\win-x64-client-installer-files"
#define OutputDir "..\publish"

[Setup]
AppId={{9D719027-1B6C-45A1-A4AA-5C46E9289862}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
DefaultDirName={commonpf32}\NetBuddies
DefaultGroupName=Net Buddies
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=NetBuddies-Client-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
SetupIconFile=..\NetBuddies.App\Assets\netbuddies.ico
UninstallDisplayIcon={app}\NetBuddies.App.exe
CloseApplications=yes
RestartIfNeededByRun=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop icon"; GroupDescription: "Shortcuts:"; Flags: checkedonce
Name: "startup"; Description: "Start Net Buddies when Windows starts"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Net Buddies"; Filename: "{app}\NetBuddies.App.exe"; WorkingDir: "{app}"; IconFilename: "{app}\NetBuddies.App.exe"
Name: "{commondesktop}\Net Buddies"; Filename: "{app}\NetBuddies.App.exe"; WorkingDir: "{app}"; IconFilename: "{app}\NetBuddies.App.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Net Buddies"; ValueData: """{app}\NetBuddies.App.exe"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\NetBuddies.App.exe"; Description: "Launch Net Buddies"; Flags: nowait postinstall skipifsilent runasoriginaluser

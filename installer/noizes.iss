#define AppName "Noizes"
#ifndef APP_VERSION
#define APP_VERSION "0.0.0"
#endif

[Setup]
AppId={{7E1111C1-2A34-4E11-9C6F-8D5A1B2C3D4E}
AppName={#AppName}
AppVersion={#APP_VERSION}
AppPublisher=Denis Popov
DefaultDirName={localappdata}\Noizes
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=Noizes-{#APP_VERSION}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\Noizes.exe

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\Noizes"; Filename: "{app}\Noizes.exe"

[Run]
Filename: "{app}\Noizes.exe"; Description: "Launch Noizes"; Flags: nowait postinstall skipifsilent

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
; Setup and the uninstaller notify running apps (notably Explorer) to reload the
; environment from the registry - covers the PATH change made in the [Code] section.
ChangesEnvironment=yes

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "..\cli\noizes.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\cli\run.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Noizes"; Filename: "{app}\Noizes.exe"

[Run]
Filename: "{app}\Noizes.exe"; Description: "Launch Noizes"; Flags: nowait postinstall skipifsilent

[Code]

// Put {app} on the user PATH so `noizes setup` works from a fresh terminal, and take it
// off again on uninstall. The ChangesEnvironment directive makes Setup and the
// uninstaller broadcast WM_SETTINGCHANGE afterwards, so a NEW terminal picks up the
// change immediately - no manual SendMessage, no logoff needed.

const
  EnvironmentKey = 'Environment';

function PathHasEntry(const paths, dir: string): Boolean;
begin
  Result := Pos(';' + Uppercase(dir) + ';', ';' + Uppercase(paths) + ';') > 0;
end;

procedure EnvAddPath(const dir: string);
var
  Paths, Updated: string;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path', Paths) then
    Paths := '';
  if PathHasEntry(Paths, dir) then
    Exit;
  if Paths = '' then
    Updated := dir
  else
    Updated := Paths + ';' + dir;
  RegWriteStringValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path', Updated);
end;

procedure EnvRemovePath(const dir: string);
var
  Paths, Entry, Updated, Rest: string;
  Sep: Integer;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path', Paths) then
    Exit;
  Rest := Paths;
  Updated := '';
  while Rest <> '' do
  begin
    Sep := Pos(';', Rest);
    if Sep = 0 then
    begin
      Entry := Rest;
      Rest := '';
    end
    else
    begin
      Entry := Copy(Rest, 1, Sep - 1);
      Rest := Copy(Rest, Sep + 1, MaxInt);
    end;
    if (Entry <> '') and (Uppercase(Entry) <> Uppercase(dir)) then
    begin
      if Updated = '' then
        Updated := Entry
      else
        Updated := Updated + ';' + Entry;
    end;
  end;
  if Updated = Paths then
    Exit; // our entry was not there - nothing to change
  if Updated = '' then
    RegDeleteValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path')
  else
    RegWriteStringValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path', Updated);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    EnvAddPath(ExpandConstant('{app}'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    EnvRemovePath(ExpandConstant('{app}'));
end;

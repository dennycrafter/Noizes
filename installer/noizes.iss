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
Source: "..\cli\noizes.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\cli\run.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Noizes"; Filename: "{app}\Noizes.exe"

[Run]
Filename: "{app}\Noizes.exe"; Description: "Launch Noizes"; Flags: nowait postinstall skipifsilent

[Code]

// Put {app} on the user PATH so `noizes setup` works from a fresh terminal, and take it
// off again on uninstall. Running Explorer is told about the change (WM_SETTINGCHANGE)
// so a NEW terminal picks it up immediately - no logoff needed.

const
  EnvironmentKey = 'Environment';
  WM_SETTINGCHANGE = $001A;
  SMTO_ABORTIFHUNG = $0002;
  // HWND_BROADCAST ($FFFF) is predefined by Inno Setup's script engine - redeclaring it is a duplicate-identifier error

procedure SendMessageTimeout(hWnd: HWND; Msg: UINT; wParam: WPARAM; lParam: LPARAM;
  fuFlags: UINT; uTimeout: UINT; var lpdwResult: DWORD);
  external 'SendMessageTimeoutW@user32.dll stdcall';

procedure NotifyEnvironmentChanged;
var
  Res: DWORD;
begin
  SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, 0,
    LPARAM(PChar('Environment')), SMTO_ABORTIFHUNG, 5000, Res);
end;

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
  // expandsz keeps REG_EXPAND_SZ so entries like %USERPROFILE% keep working
  RegWriteExpandStringValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path', Updated);
  NotifyEnvironmentChanged;
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
    RegWriteExpandStringValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path', Updated);
  NotifyEnvironmentChanged;
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

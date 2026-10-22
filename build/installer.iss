; WebHut installer (Inno Setup 6). Built by build\build.ps1 -Package when ISCC.exe is available:
;   ISCC.exe /DAppVersion=1.6.2 /DStage=dist\WebHut /DOutDir=dist build\installer.iss
;
; - Per-user install, no admin rights: WebHut writes inside its own folder, so it must not go to Program Files.
; - Upgrades in place: the running panel is asked to close (servers keep running), the two .exe files are replaced,
;   and nothing else in the folder is touched.
; - Uninstall stops the servers, removes the programs WebHut downloaded, and asks before deleting your sites,
;   databases and settings (kept by default).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef Stage
  #define Stage "..\dist\WebHut"
#endif
#ifndef OutDir
  #define OutDir "..\dist"
#endif

[Setup]
AppId={{6E7B1F4A-2C9D-4E8B-9A51-0B3F7C2D8E14}
AppName=WebHut
AppVersion={#AppVersion}
AppVerName=WebHut {#AppVersion}
AppPublisher=WebHut contributors
AppPublisherURL=https://github.com/hashencoder/webhut
AppSupportURL=https://github.com/hashencoder/webhut/issues
AppUpdatesURL=https://github.com/hashencoder/webhut/releases
VersionInfoVersion={#AppVersion}
VersionInfoProductName=WebHut
VersionInfoDescription=WebHut setup - Apache, PHP, MariaDB and Node.js for Windows
DefaultDirName={sd}\WebHut
DisableDirPage=no
DefaultGroupName=WebHut
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#OutDir}
OutputBaseFilename=WebHut-Setup-{#AppVersion}
SetupIconFile=..\assets\webhut.ico
UninstallDisplayIcon={app}\WebHut.exe
UninstallDisplayName=WebHut
WizardStyle=modern
Compression=lzma2/ultra
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=no
RestartApplications=no
LicenseFile=..\LICENSE
UsePreviousAppDir=yes
DirExistsWarning=no

[Messages]
SelectDirDesc=Where should WebHut be installed?
SelectDirLabel3=WebHut keeps its servers, sites and databases in this folder. Choose a short path without spaces, outside Program Files (for example C:\WebHut).

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "{#Stage}\WebHut.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Stage}\WebHut.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Stage}\whut.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Stage}\whut.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Stage}\README.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Stage}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Stage}\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion

[Icons]
Name: "{group}\WebHut"; Filename: "{app}\WebHut.exe"; WorkingDir: "{app}"
Name: "{group}\WebHut folder"; Filename: "{app}"
Name: "{group}\Uninstall WebHut"; Filename: "{uninstallexe}"
Name: "{userdesktop}\WebHut"; Filename: "{app}\WebHut.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\WebHut.exe"; Description: "Open WebHut now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Stop Apache, MariaDB and Node apps cleanly before their files are removed.
Filename: "{app}\whut.exe"; Parameters: "stop all"; Flags: runhidden waituntilterminated; RunOnceId: "StopServers"

[Code]
const
  WaitPanelMs = 8000;

function PanelRunning(): Boolean;
var
  Code: Integer;
begin
  { tasklist exits 0 either way; findstr decides. }
  Result := Exec(ExpandConstant('{cmd}'), '/c tasklist /FI "IMAGENAME eq WebHut.exe" /NH | findstr /I "WebHut.exe" >nul', '',
                 SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
end;

{ Ask a running panel to close (WebHut 1.6.2+ understands --quit); older panels are ended.
  Servers are separate processes and keep running either way. }
procedure ClosePanel(Dir: String);
var
  Code, Waited: Integer;
begin
  if not PanelRunning() then exit;
  if FileExists(AddBackslash(Dir) + 'WebHut.exe') then
    Exec(AddBackslash(Dir) + 'WebHut.exe', '--quit', Dir, SW_HIDE, ewWaitUntilTerminated, Code);
  Waited := 0;
  while PanelRunning() and (Waited < WaitPanelMs) do
  begin
    Sleep(250);
    Waited := Waited + 250;
  end;
  if PanelRunning() then
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM WebHut.exe /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(500);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Dir: String;
begin
  Result := True;
  if CurPageID = wpSelectDir then
  begin
    Dir := WizardDirValue();
    if Pos(' ', Dir) > 0 then
    begin
      MsgBox('Please choose a folder without spaces (for example C:\WebHut). Apache and PHP configuration paths break on spaces.', mbError, MB_OK);
      Result := False;
    end
    else if (Pos(LowerCase(ExpandConstant('{commonpf}')), LowerCase(Dir)) = 1) or (Pos(LowerCase(ExpandConstant('{commonpf32}')), LowerCase(Dir)) = 1) then
    begin
      MsgBox('WebHut can''t live inside Program Files: it needs to write its own files there without admin rights. Choose a folder like C:\WebHut.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  ClosePanel(WizardDirValue());
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  ClosePanel(ExpandConstant('{app}'));
end;

procedure DeleteDir(Name: String);
begin
  DelTree(ExpandConstant('{app}\') + Name, True, True, True);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    { Programs WebHut downloaded: always removed (they can be downloaded again). }
    DeleteDir('apache');
    DeleteDir('php');
    DeleteDir('mariadb');
    DeleteDir('node');
    DeleteDir('tools');
    DeleteDir('ssl');
    DeleteDir('apps');
    DeleteDir('tmp');
    DeleteDir('logs');
    DeleteFile(ExpandConstant('{app}\WebHut.old.exe'));
    DeleteFile(ExpandConstant('{app}\whut.old.exe'));
    { Your work: kept unless you say otherwise. }
    if (not UninstallSilent()) and
       (MsgBox('WebHut has been removed.' + #13#10#13#10 +
               'Also delete your sites, databases, backups and settings?' + #13#10 +
               '  ' + ExpandConstant('{app}') + '\www  (your sites)' + #13#10 +
               '  ' + ExpandConstant('{app}') + '\data  (databases and backups)' + #13#10 +
               '  ' + ExpandConstant('{app}') + '\config, webhut.json, node-apps.json' + #13#10#13#10 +
               'Choose No to keep them (you can reinstall WebHut over them later).',
               mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
    begin
      DeleteDir('www');
      DeleteDir('data');
      DeleteDir('config');
      DeleteFile(ExpandConstant('{app}\webhut.json'));
      DeleteFile(ExpandConstant('{app}\node-apps.json'));
      RemoveDir(ExpandConstant('{app}'));
    end;
  end;
end;

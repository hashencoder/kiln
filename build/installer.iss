; ServNook installer (Inno Setup 6). Built by build\build.ps1 -Package when ISCC.exe is available:
;   ISCC.exe /DAppVersion=1.6.2 /DStage=dist\ServNook /DOutDir=dist build\installer.iss
;
; - Per-user install, no admin rights: ServNook writes inside its own folder, so it must not go to Program Files.
; - Upgrades in place: the running panel is asked to close (servers keep running), the two .exe files are replaced,
;   and nothing else in the folder is touched.
; - Uninstall stops the servers, removes the programs ServNook downloaded, and asks before deleting your sites,
;   databases and settings (kept by default).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef Stage
  #define Stage "..\dist\ServNook"
#endif
#ifndef OutDir
  #define OutDir "..\dist"
#endif

#if Ver < EncodeVer(6, 7, 0)
  #error Inno Setup 6.7 or newer is needed (WizardBackColor, dark mode).
#endif

[Setup]
AppId={{6E7B1F4A-2C9D-4E8B-9A51-0B3F7C2D8E14}
AppName=ServNook
AppVersion={#AppVersion}
AppVerName=ServNook {#AppVersion}
AppPublisher=ServNook contributors
AppPublisherURL=https://github.com/hashencoder/servnook
AppSupportURL=https://github.com/hashencoder/servnook/issues
AppUpdatesURL=https://github.com/hashencoder/servnook/releases
VersionInfoVersion={#AppVersion}
VersionInfoProductName=ServNook
VersionInfoDescription=ServNook setup - Apache, PHP, MariaDB and Node.js for Windows
DefaultDirName={sd}\ServNook
DisableDirPage=no
DefaultGroupName=ServNook
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#OutDir}
OutputBaseFilename=ServNook-Setup-{#AppVersion}
SetupIconFile=..\assets\servnook.ico
UninstallDisplayIcon={app}\ServNook.exe
UninstallDisplayName=ServNook
; ServNook look: the windows11 style on ServNook's own background colours, following Windows light/dark
; (needs Inno Setup 6.7+), an orange ServNook panel on the welcome/finish pages and the logo on every page.
WizardStyle=modern dynamic windows11
WizardBackColor=#F6F8FB
WizardBackColorDynamicDark=#10151D
WizardImageFile=installer-side.png
WizardSmallImageFile=installer-small.png
Compression=lzma2/ultra
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=no
RestartApplications=no
LicenseFile=..\LICENSE
UsePreviousAppDir=yes
; The Start menu folder was "WebHut" up to 1.6.5; always use the current name.
UsePreviousGroup=no
DirExistsWarning=no
; Ask Explorer to refresh its icon cache after install, so pinned taskbar and Start shortcuts
; pick up a changed ServNook icon instead of showing the cached old one.
ChangesAssociations=yes

[Messages]
SelectDirDesc=Where should ServNook be installed?
SelectDirLabel3=ServNook keeps its servers, sites and databases in this folder. Choose a short path without spaces, outside Program Files (for example C:\ServNook).

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "{#Stage}\ServNook.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Stage}\ServNook.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Stage}\snook.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Stage}\snook.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Stage}\README.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Stage}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Stage}\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion

[InstallDelete]
; WebHut was renamed ServNook in 1.7: remove the old programs and shortcuts when upgrading in place.
Type: files; Name: "{app}\WebHut.exe"
Type: files; Name: "{app}\WebHut.exe.config"
Type: files; Name: "{app}\whut.exe"
Type: files; Name: "{app}\whut.exe.config"
Type: filesandordirs; Name: "{autoprograms}\WebHut"
Type: files; Name: "{autodesktop}\WebHut.lnk"

[Icons]
Name: "{group}\ServNook"; Filename: "{app}\ServNook.exe"; WorkingDir: "{app}"
Name: "{group}\ServNook folder"; Filename: "{app}"
Name: "{group}\Uninstall ServNook"; Filename: "{uninstallexe}"
Name: "{userdesktop}\ServNook"; Filename: "{app}\ServNook.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\ServNook.exe"; Description: "Open ServNook now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Stop Apache, MariaDB and Node apps cleanly before their files are removed.
Filename: "{app}\snook.exe"; Parameters: "stop all"; Flags: runhidden waituntilterminated; RunOnceId: "StopServers"

[Code]
const
  WaitPanelMs = 8000;

function ExeRunning(Exe: String): Boolean;
var
  Code: Integer;
begin
  { tasklist exits 0 either way; findstr decides. }
  Result := Exec(ExpandConstant('{cmd}'), '/c tasklist /FI "IMAGENAME eq ' + Exe + '" /NH | findstr /I "' + Exe + '" >nul', '',
                 SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
end;

{ The panel is ServNook.exe; up to 1.6.5 it was WebHut.exe. }
function PanelRunning(): Boolean;
begin
  Result := ExeRunning('ServNook.exe') or ExeRunning('WebHut.exe');
end;

{ Ask a running panel to close (1.6.2+ understands --quit); older panels are ended.
  Servers are separate processes and keep running either way. }
procedure ClosePanel(Dir: String);
var
  Code, Waited: Integer;
begin
  if not PanelRunning() then exit;
  if FileExists(AddBackslash(Dir) + 'ServNook.exe') then
    Exec(AddBackslash(Dir) + 'ServNook.exe', '--quit', Dir, SW_HIDE, ewWaitUntilTerminated, Code);
  if FileExists(AddBackslash(Dir) + 'WebHut.exe') then
    Exec(AddBackslash(Dir) + 'WebHut.exe', '--quit', Dir, SW_HIDE, ewWaitUntilTerminated, Code);
  Waited := 0;
  while PanelRunning() and (Waited < WaitPanelMs) do
  begin
    Sleep(250);
    Waited := Waited + 250;
  end;
  if PanelRunning() then
  begin
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM ServNook.exe /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM WebHut.exe /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
  end;
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
      MsgBox('Please choose a folder without spaces (for example C:\ServNook). Apache and PHP configuration paths break on spaces.', mbError, MB_OK);
      Result := False;
    end
    else if (Pos(LowerCase(ExpandConstant('{commonpf}')), LowerCase(Dir)) = 1) or (Pos(LowerCase(ExpandConstant('{commonpf32}')), LowerCase(Dir)) = 1) then
    begin
      MsgBox('ServNook can''t live inside Program Files: it needs to write its own files there without admin rights. Choose a folder like C:\ServNook.', mbError, MB_OK);
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
    { Programs ServNook downloaded: always removed (they can be downloaded again). }
    DeleteDir('apache');
    DeleteDir('php');
    DeleteDir('mariadb');
    DeleteDir('node');
    DeleteDir('tools');
    DeleteDir('ssl');
    DeleteDir('apps');
    DeleteDir('mysql');
    DeleteDir('postgresql');
    DeleteDir('mongodb');
    DeleteDir('mongosh');
    DeleteDir('tmp');
    DeleteDir('logs');
    DeleteFile(ExpandConstant('{app}\ServNook.old.exe'));
    DeleteFile(ExpandConstant('{app}\snook.old.exe'));
    DeleteFile(ExpandConstant('{app}\WebHut.old.exe'));
    DeleteFile(ExpandConstant('{app}\whut.old.exe'));
    { Your work: kept unless you say otherwise. }
    if (not UninstallSilent()) and
       (MsgBox('ServNook has been removed.' + #13#10#13#10 +
               'Also delete your sites, databases, backups and settings?' + #13#10 +
               '  ' + ExpandConstant('{app}') + '\www  (your sites)' + #13#10 +
               '  ' + ExpandConstant('{app}') + '\data  (databases and backups)' + #13#10 +
               '  ' + ExpandConstant('{app}') + '\config, servnook.json, node-apps.json' + #13#10#13#10 +
               'Choose No to keep them (you can reinstall ServNook over them later).',
               mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
    begin
      DeleteDir('www');
      DeleteDir('data');
      DeleteDir('config');
      DeleteFile(ExpandConstant('{app}\servnook.json'));
      DeleteFile(ExpandConstant('{app}\node-apps.json'));
      RemoveDir(ExpandConstant('{app}'));
    end;
  end;
end;

; ============================================================
; Breakers of E v2 — Inno Setup Installer Script
; ============================================================
; Prerequisites:
;   1. Install Inno Setup 6: https://jrsoftware.org/isinfo.php
;   2. Run Publish_v2.bat first to build the app
;   3. Open this .iss file in Inno Setup and click Build > Compile
;
; Upgrading from v1: same AppId, so v2 installs over v1 in place.
; Setup only replaces the program. The data in Documents\Breakers of E is
; converted by BoE itself, the first time v2 starts (v1 files kept in
; "v1 Backups"). Before anything is touched, a page says v1 is being
; replaced and is no longer supported; Cancel there leaves v1 as it was.
; ============================================================

#define AppName      "Breakers of E"
#define AppVersion   "2.0.0"
#define AppPublisher "Breakers Of E"
#define AppExeName   "BreakersOfE.exe"
#define AppURL       ""
#define AppGUID      "{{A7B3C2D1-E4F5-4A6B-8C9D-1E2F3A4B5C6D}"
; The same id as it's written in the registry (one brace).
#define AppIdPlain   "{A7B3C2D1-E4F5-4A6B-8C9D-1E2F3A4B5C6D}"
; Left over by early v2 test builds; removed if found.
#define OldAgentExe  "BreakersOfE.Agent.exe"
; The app's own icon (also used for setup itself and Installed Apps).
#define AppIcon      "..\BreakersOfE_v2\BreakersOfE\Resources\Icons\BreakersOfE.ico"

[Setup]
AppId={#AppGUID}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
OutputDir=Output
OutputBaseFilename=BreakersOfE_Setup_v{#AppVersion}
SetupIconFile={#AppIcon}
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x86 x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
MinVersion=10.0
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; BoE must be closed while it's replaced (files in use): setup asks.
CloseApplications=yes
; At the end, Windows is told icons changed: every shortcut to BoE (desktop,
; Start menu, Public Desktop, pinned) shows the new icon, not v1's cached one.
ChangesAssociations=yes
RestartApplications=no

[Registry]
Root: HKCU; Subkey: "Software\Breakers of E"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon";    Description: "Create a &desktop shortcut";           GroupDescription: "Additional shortcuts:"
Name: "startmenu";      Description: "Create a &Start Menu shortcut";        GroupDescription: "Additional shortcuts:"; Flags: checkedonce

[Files]
; ── x64 ──
Source: "publish\v2\x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: Is64BitInstallMode
; ── x86 ──
Source: "publish\v2\x86\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: not Is64BitInstallMode

[Icons]
; The boxes mean "make sure I have one": when a shortcut that opens BoE is
; already there (any name; yours or the shared one), none is added, so there
; are never two. Nothing is ever deleted. (The icon refresh covers the old ones.)
; Start Menu
Name: "{group}\{#AppName}";                    Filename: "{app}\{#AppExeName}"; Tasks: startmenu;   Check: not HasStartMenuShortcut
Name: "{group}\Uninstall {#AppName}";          Filename: "{uninstallexe}";      Tasks: startmenu;   Check: not HasStartMenuShortcut
; Desktop
Name: "{userdesktop}\{#AppName}";              Filename: "{app}\{#AppExeName}"; Tasks: desktopicon; Check: not HasDesktopShortcut

[Run]
; Launch after install, as the signed-in user (so BoE finds YOUR Documents
; folder even if setup ran as administrator). The first start converts v1
; data, then downloads the card data by itself.
Filename: "{app}\{#AppExeName}"; \
    Description: "Launch {#AppName}"; \
    Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
var
  V1Version: String;      // '' when v1 isn't installed
  V1Page: TOutputMsgWizardPage;
  ShortcutNote: TNewStaticText;
  DesktopFound: Integer;  // -1 not looked yet, 0 no, 1 yes
  StartFound: Integer;
  WShell: Variant;        // Windows' shortcut reader, made once

{ ── Shortcuts already there ─────────────────────────────────────────── }

{ Does this shortcut file open BoE (this install's program)? }
function LinkOpensApp(const Lnk: String): Boolean;
var
  Link: Variant;
begin
  Result := False;
  try
    if VarIsEmpty(WShell) then WShell := CreateOleObject('WScript.Shell');
    Link := WShell.CreateShortcut(Lnk);
    Result := CompareText(String(Link.TargetPath), ExpandConstant('{app}\{#AppExeName}')) = 0;
  except
    Result := False;
  end;
end;

{ Any shortcut in this folder (and Depth levels of subfolders) that opens BoE? }
function FolderHasAppLink(const Folder: String; Depth: Integer): Boolean;
var
  FR: TFindRec;
begin
  Result := False;
  if (Folder = '') or not DirExists(Folder) then exit;
  if FindFirst(Folder + '\*', FR) then
  try
    repeat
      if (FR.Name <> '.') and (FR.Name <> '..') then
      begin
        if (FR.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          if Depth > 0 then
            if FolderHasAppLink(Folder + '\' + FR.Name, Depth - 1) then Result := True;
        end
        else if CompareText(ExtractFileExt(FR.Name), '.lnk') = 0 then
          if LinkOpensApp(Folder + '\' + FR.Name) then Result := True;
      end;
    until Result or not FindNext(FR);
  finally
    FindClose(FR);
  end;
end;

{ A shortcut to BoE on your desktop or the shared (Public) one. }
function HasDesktopShortcut(): Boolean;
begin
  if DesktopFound < 0 then
  begin
    DesktopFound := 0;
    if FolderHasAppLink(ExpandConstant('{userdesktop}'), 0) or
       FolderHasAppLink(GetEnv('PUBLIC') + '\Desktop', 0) then DesktopFound := 1;
  end;
  Result := DesktopFound = 1;
end;

{ A shortcut to BoE in your Start menu or the shared one (and their folders). }
function HasStartMenuShortcut(): Boolean;
begin
  if StartFound < 0 then
  begin
    StartFound := 0;
    if FolderHasAppLink(ExpandConstant('{userprograms}'), 2) or
       FolderHasAppLink(GetEnv('ProgramData') + '\Microsoft\Windows\Start Menu\Programs', 2) then StartFound := 1;
  end;
  Result := StartFound = 1;
end;

{ The installed version of this app (same AppId for v1 and v2), from Windows'
  list of installed programs: per-user first, then per-machine (both views). }
function InstalledVersion(): String;
var
  Key: String;
begin
  Result := '';
  Key := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppIdPlain}_is1';
  if RegQueryStringValue(HKCU, Key, 'DisplayVersion', Result) then exit;
  if IsWin64 then
    if RegQueryStringValue(HKLM64, Key, 'DisplayVersion', Result) then exit;
  if RegQueryStringValue(HKLM32, Key, 'DisplayVersion', Result) then exit;
  Result := '';
end;

function InitializeSetup(): Boolean;
var
  V: String;
begin
  V := InstalledVersion();
  if Copy(V, 1, 2) = '1.' then V1Version := V else V1Version := '';
  DesktopFound := -1;
  StartFound := -1;
  Result := True;
end;

procedure InitializeWizard();
begin
  { Shown only when v1 is installed, before anything is touched. Cancel here: v1 stays as it is. }
  V1Page := CreateOutputMsgPage(wpWelcome,
    'Replacing Breakers of E v1',
    'Breakers of E ' + V1Version + ' is installed on this computer.',
    'Breakers of E v2 is a new program. It replaces v1, and v1 is no longer supported.' + #13#10 + #13#10 +
    'Your collection and decks are converted the first time v2 starts. Before anything is changed, ' +
    'your v1 files are copied to a "v1 Backups" folder in Documents\Breakers of E, and every card is ' +
    'counted before and after converting.' + #13#10 + #13#10 +
    'Right after, v2 downloads the new card data by itself (internet needed, a few minutes).' + #13#10 + #13#10 +
    'To keep v1, click Cancel now: nothing has been changed. Click Next to install v2.');

  { Under the shortcut boxes: says when one is already there (filled in when the page shows). }
  WizardForm.TasksList.Height := WizardForm.TasksList.Height - ScaleY(56);
  ShortcutNote := TNewStaticText.Create(WizardForm);
  ShortcutNote.Parent := WizardForm.SelectTasksPage;
  ShortcutNote.Left := WizardForm.TasksList.Left;
  ShortcutNote.Top := WizardForm.TasksList.Top + WizardForm.TasksList.Height + ScaleY(8);
  ShortcutNote.Width := WizardForm.TasksList.Width;
  ShortcutNote.AutoSize := False;
  ShortcutNote.Height := ScaleY(48);
  ShortcutNote.WordWrap := True;
  ShortcutNote.Caption := '';
end;

procedure CurPageChanged(CurPageID: Integer);
var
  D, S: Boolean;
begin
  if CurPageID <> wpSelectTasks then exit;
  DesktopFound := -1;               { look again: the install folder may have just been chosen }
  StartFound := -1;
  D := HasDesktopShortcut();
  S := HasStartMenuShortcut();
  if D and S then
    ShortcutNote.Caption := 'You already have Breakers of E shortcuts on the desktop and in the Start menu. ' +
                            'They will be kept and get the new icon; no second ones are made.'
  else if D then
    ShortcutNote.Caption := 'You already have a Breakers of E shortcut on the desktop. ' +
                            'It will be kept and get the new icon; no second one is made.'
  else if S then
    ShortcutNote.Caption := 'You already have a Breakers of E shortcut in the Start menu. ' +
                            'It will be kept and get the new icon; no second one is made.'
  else
    ShortcutNote.Caption := '';
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = V1Page.ID) and (V1Version = '');
end;

{ v1's program files, cleared before v2's are copied (so none are left mixed in).
  The uninstaller (unins000.*) stays: it remembers the shortcuts made before, so
  uninstalling still removes them. Never a data folder: if a collection database
  is there, the folder is left alone. }
procedure RemoveV1ProgramFiles();
var
  App: String;
  FR: TFindRec;
begin
  App := ExpandConstant('{app}');
  if not DirExists(App) then exit;
  if FileExists(App + '\collection.db') or FileExists(App + '\Collection\collection.db') or
     FileExists(App + '\breakersofe.db') then
  begin
    Log('Program folder holds BoE data: v1 files not cleared.');
    exit;
  end;
  if FindFirst(App + '\*', FR) then
  try
    repeat
      if (FR.Name <> '.') and (FR.Name <> '..') and (Pos('unins', Lowercase(FR.Name)) <> 1) then
      begin
        if (FR.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          if not DelTree(App + '\' + FR.Name, True, True, True) then Log('Could not remove ' + FR.Name);
        end
        else if not DeleteFile(App + '\' + FR.Name) then Log('Could not remove ' + FR.Name);
      end;
    until not FindNext(FR);
  finally
    FindClose(FR);
  end;
end;

{ The Background Agent from early v2 test builds: stopped, its startup entry and folder removed. }
procedure RemoveOldAgent();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#OldAgentExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'BreakersOfE Agent');
  DelTree(ExpandConstant('{app}\Agent'), True, True, True);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    RemoveOldAgent();
    if V1Version <> '' then RemoveV1ProgramFiles();
  end;
end;

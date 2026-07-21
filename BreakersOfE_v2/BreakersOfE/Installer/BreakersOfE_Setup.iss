; ============================================================
; Breakers of E — Inno Setup Installer Script
; ============================================================
; Prerequisites:
;   1. Install Inno Setup 6: https://jrsoftware.org/isinfo.php
;   2. Publish the app first:
;      dotnet publish BreakersOfE/BreakersOfE.csproj ^
;        -c Release -r win-x64 --self-contained true ^
;        -p:PublishSingleFile=false ^
;        -o Installer/publish/x64
;      dotnet publish BreakersOfE/BreakersOfE.csproj ^
;        -c Release -r win-x86 --self-contained true ^
;        -p:PublishSingleFile=false ^
;        -o Installer/publish/x86
;   3. Open this .iss file in Inno Setup and click Build > Compile
; ============================================================

#define AppName      "Breakers of E"
#define AppVersion   "1.2.1"
#define AppPublisher "Breakers Of E"
#define AppExeName   "BreakersOfE.exe"
#define AppURL       ""
#define AppGUID      "{{A7B3C2D1-E4F5-4A6B-8C9D-1E2F3A4B5C6D}"

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
; Output
OutputDir=Output
OutputBaseFilename=BreakersOfE_Setup_v{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
; Architecture
ArchitecturesAllowed=x86 x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Appearance
WizardStyle=modern
; Windows version requirement
MinVersion=10.0
; Privileges
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; Prevent app from requesting elevation at runtime
[Registry]
Root: HKCU; Subkey: "Software\Breakers of E"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
english.DownloadDBLabel=Download card database on first launch (~98,000 cards from Scryfall)
english.DownloadDBDescription=Recommended. Downloads the latest Magic: The Gathering card data. Required before using the app. You can also do this later via Tools %> Update Card Database.

[Types]
Name: "full";    Description: "Full installation"
Name: "custom";  Description: "Custom installation"; Flags: iscustom

[Components]
Name: "main";     Description: "Breakers of E (required)";  Types: full custom; Flags: fixed

[Tasks]
Name: "desktopicon";  Description: "Create a &desktop shortcut";        GroupDescription: "Additional shortcuts:"
Name: "startmenu";    Description: "Create a &Start Menu shortcut";     GroupDescription: "Additional shortcuts:"; Flags: checkedonce

[Files]
; x64 binaries — installed on 64-bit Windows
Source: "publish\x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: Is64BitInstallMode

; x86 binaries — installed on 32-bit Windows
Source: "publish\x86\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: not Is64BitInstallMode

[Icons]
; Start Menu
Name: "{group}\{#AppName}";                    Filename: "{app}\{#AppExeName}"; Tasks: startmenu
Name: "{group}\Uninstall {#AppName}";          Filename: "{uninstallexe}";      Tasks: startmenu
; Desktop
Name: "{userdesktop}\{#AppName}";            Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; Launch app after install (optional)
Filename: "{app}\{#AppExeName}"; \
    Description: "Launch {#AppName}"; \
    Flags: nowait postinstall skipifsilent

[Code]
// ── Custom page: Download database option ────────────────────────────────────
var
  DownloadPage: TWizardPage;
  DownloadCheckBox: TNewCheckBox;

procedure InitializeWizard();
begin
  // Create a custom page after the tasks page
  DownloadPage := CreateCustomPage(
    wpSelectTasks,
    'First Launch Options',
    'Choose what happens when Breakers of E starts for the first time.');

  DownloadCheckBox := TNewCheckBox.Create(WizardForm);
  DownloadCheckBox.Parent := DownloadPage.Surface;
  DownloadCheckBox.Left := 0;
  DownloadCheckBox.Top := 16;
  DownloadCheckBox.Width := DownloadPage.SurfaceWidth;
  DownloadCheckBox.Height := 24;
  DownloadCheckBox.Caption := 'Download card database on first launch (~98,000 cards from Scryfall)';
  DownloadCheckBox.Checked := True;  // Checked by default

  // Description label
  with TNewStaticText.Create(WizardForm) do
  begin
    Parent := DownloadPage.Surface;
    Left   := 20;
    Top    := 44;
    Width  := DownloadPage.SurfaceWidth - 20;
    Height := 60;
    Caption :=
      'Recommended. Downloads the latest Magic: The Gathering card data from Scryfall.' + #13#10 +
      'Required before using the app. This may take a few minutes.' + #13#10 +
      'You can also do this later via Tools > Update Card Database.';
    WordWrap := True;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    // If user checked the download option, run the app to download
    // the database now (during install) rather than on first launch.
    if DownloadCheckBox.Checked then
    begin
      WizardForm.StatusLabel.Caption := 'Downloading card database from Scryfall...';
      Exec(ExpandConstant('{app}\{#AppExeName}'), '--update-db', '',
           SW_SHOW, ewWaitUntilTerminated, ResultCode);
    end;
  end;
end;

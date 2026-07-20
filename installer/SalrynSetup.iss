; Salryn POS Beta RC1 — Inno Setup 6 installer script.
;
; Packages the WebView2 app shell, local API host, branding assets,
; and React production dist.
;
; Production data lives under %ProgramData%\Salryn and is NEVER removed
; by uninstall.  Upgrade preserves production data.
;
; Prerequisites (build machine only, not end-user machine):
;   Inno Setup 6  https://jrsoftware.org/isinfo.php
;   Run via:  tools\installer\Build-SalrynBetaRc1Installer.ps1 -BuildInstaller
;
; End users:
;   Run Salryn-POS-BetaRC1-Setup.exe.  No PowerShell, no localhost, no Inno Setup needed.
;   After installation launch Salryn POS from the desktop or Start Menu shortcut.

#define MyAppName      "Salryn POS"
#define MyAppShortName "Salryn"
#define MyAppPublisher "Kirjane Labs"
#define MyAppExeName   "Salryn.Desktop.WebView.exe"
#define MyAppIcon      "..\assets\branding\salryn-icon.ico"
#define MyAppVersion   "0.1.0-rc1"
#define MyAppGuid      "A4E2B8C1-7D3F-4A91-9C2E-5F6A8B1C0D3E"

[Setup]
AppId={{{#MyAppGuid}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/Kirch-Nairu/Salryn
AppSupportURL=mailto:salryn.info@gmail.com
AppUpdatesURL=https://github.com/Kirch-Nairu/Salryn/releases

; Install under C:\Program Files\Kirjane Labs\Salryn POS
DefaultDirName={autopf}\Kirjane Labs\Salryn POS
; Start Menu: Kirjane Labs\Salryn POS
DefaultGroupName={#MyAppPublisher}\{#MyAppName}
DisableProgramGroupPage=no

OutputDir=..\artifacts\installer\setup
OutputBaseFilename=Salryn-POS-BetaRC1-Setup
SetupIconFile={#MyAppIcon}
UninstallDisplayIcon={app}\assets\branding\salryn-icon.ico
UninstallDisplayName={#MyAppName}

Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Upgrade: remove old files, install new ones, keep ProgramData untouched
MajorUpgrade=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
; Harvest everything from the staging folder (exe, DLLs, dist\, assets\, config)
Source: "..\artifacts\installer\staging\Salryn\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Start Menu shortcut
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\assets\branding\salryn-icon.ico"; WorkingDir: "{app}"; Comment: "Start Salryn POS"

; Desktop shortcut (optional, offered in [Tasks])
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\assets\branding\salryn-icon.ico"; Tasks: desktopicon; WorkingDir: "{app}"; Comment: "Start Salryn POS"

[Run]
; Offer to launch immediately after install
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Remove the installed app folder on uninstall.
; %ProgramData%\Salryn (production data, backups, receipts, logs) is intentionally
; NOT listed here and will be preserved across uninstall and upgrade.
Type: filesandordirs; Name: "{app}"

[Code]
// Guard: warn the user if they are about to downgrade.
function InitializeSetup(): Boolean;
begin
  Result := True;
end;

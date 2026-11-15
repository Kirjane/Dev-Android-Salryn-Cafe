; Salryn POS Beta RC1 — Inno Setup 6 installer script.
;
; Packages the WebView2 app shell, local API host, branding assets,
; and React production dist.
;
; Also installs Microsoft Edge WebView2 Runtime (Evergreen Bootstrapper)
; when the runtime is not already present. Clients do not install WebView2
; manually. Internet is required during install if the bootstrapper must
; download the runtime.
;
; Production data lives under %ProgramData%\Salryn and is NEVER removed
; by uninstall or upgrade.  Reinstalling over the same AppId replaces
; application files under {app} only.
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
#define MyAppPublisher "Kirjane Information Technology Services"
#define MyAppExeName   "Salryn.Desktop.WebView.exe"
#define MyAppIcon      "..\assets\branding\salryn-icon.ico"
#ifndef MyAppVersion
  #define MyAppVersion "0.1.0-rc1"
#endif
#ifndef StagingDir
  #define StagingDir "..\artifacts\installer\staging\Salryn"
#endif
#ifndef SetupOutputDir
  #define SetupOutputDir "..\artifacts\installer\setup"
#endif
#ifndef MySourceRevision
  #define MySourceRevision ""
#endif
; Stable AppId — must not change between Salryn POS installer builds.
; Inno Setup uses this to recognize upgrades of the same installed product.
#define MyAppGuid      "A4E2B8C1-7D3F-4A91-9C2E-5F6A8B1C0D3E"
#define WebView2ClientId "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"

[Setup]
AppId={{{#MyAppGuid}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/Kirch-Nairu/Salryn
AppSupportURL=mailto:salryn.info@gmail.com
AppUpdatesURL=https://github.com/Kirch-Nairu/Salryn/releases

; Install under the company publisher folder.
DefaultDirName={autopf}\Kirjane Information Technology Services\Salryn POS
; Start Menu: Kirjane Information Technology Services\Salryn POS
DefaultGroupName={#MyAppPublisher}\{#MyAppName}
DisableProgramGroupPage=no
UsePreviousAppDir=yes

OutputDir={#SetupOutputDir}
OutputBaseFilename=Salryn-POS-BetaRC1-Setup
SetupIconFile={#MyAppIcon}
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Dirs]
; The local API runs as the signed-in Windows operator after Setup exits. Grant
; standard users Modify on the shared data tree so SQLite can create and update
; its -wal/-shm sidecars. Re-applying these entries on upgrade also repairs
; installations whose sidecars inherited read-only Users access from ProgramData.
Name: "{commonappdata}\Salryn"; Permissions: users-modify
Name: "{commonappdata}\Salryn\data"; Permissions: users-modify
Name: "{commonappdata}\Salryn\backups"; Permissions: users-modify
Name: "{commonappdata}\Salryn\config"; Permissions: users-modify
Name: "{commonappdata}\Salryn\exports"; Permissions: users-modify
Name: "{commonappdata}\Salryn\logs"; Permissions: users-modify
Name: "{commonappdata}\Salryn\receipts"; Permissions: users-modify

[Files]
; Harvest everything from the staging folder (exe, DLLs, dist\, assets\, config).
; ignoreversion replaces existing app files on upgrade without touching ProgramData.
Source: "{#StagingDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; WebView2 Evergreen Bootstrapper — extracted to {tmp}, deleted after install.
; Official Microsoft redistributable; not left in {app}.
Source: "redist\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

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
; C:\ProgramData\Salryn (production data, backups, receipts, logs) is intentionally
; NOT listed here and will be preserved across uninstall, upgrade, and reinstall.
Type: filesandordirs; Name: "{app}"

[Code]
{ Microsoft-documented WebView2 Runtime client GUID and pv (REG_SZ) detection.
  See: https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution }

function IsWebView2RuntimeInstalled: Boolean;
var
  Version: String;
begin
  Result := False;

  if IsWin64 then
  begin
    if RegQueryStringValue(HKEY_LOCAL_MACHINE,
         'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\' + '{#WebView2ClientId}',
         'pv', Version) or
       RegQueryStringValue(HKEY_CURRENT_USER,
         'Software\Microsoft\EdgeUpdate\Clients\' + '{#WebView2ClientId}',
         'pv', Version) then
    begin
      Result := (Version <> '') and (Version <> '0.0.0.0');
    end;
  end
  else
  begin
    if RegQueryStringValue(HKEY_LOCAL_MACHINE,
         'SOFTWARE\Microsoft\EdgeUpdate\Clients\' + '{#WebView2ClientId}',
         'pv', Version) or
       RegQueryStringValue(HKEY_CURRENT_USER,
         'Software\Microsoft\EdgeUpdate\Clients\' + '{#WebView2ClientId}',
         'pv', Version) then
    begin
      Result := (Version <> '') and (Version <> '0.0.0.0');
    end;
  end;
end;

function InstallWebView2Runtime: Boolean;
var
  ResultCode: Integer;
  Bootstrapper: String;
begin
  Bootstrapper := ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe');
  if not FileExists(Bootstrapper) then
  begin
    MsgBox(
      'Salryn POS setup could not find the Microsoft Edge WebView2 Runtime bootstrapper.' + #13#10 +
      'Re-download Salryn-POS-BetaRC1-Setup.exe from Kirjane Information Technology Services and try again.',
      mbError, MB_OK);
    Result := False;
    Exit;
  end;

  WizardForm.StatusLabel.Caption := 'Installing Microsoft Edge WebView2 Runtime...';
  WizardForm.FilenameLabel.Caption := 'This may take a minute and requires internet access.';

  { Official silent flags: MicrosoftEdgeWebview2Setup.exe /silent /install
    See Microsoft Learn: Distribute your app and the WebView2 Runtime. }
  if not Exec(Bootstrapper, '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    MsgBox(
      'Salryn POS could not start the WebView2 Runtime installer.' + #13#10 +
      'Check your internet connection and run Salryn-POS-BetaRC1-Setup.exe again as administrator.',
      mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if ResultCode <> 0 then
  begin
    MsgBox(
      'Microsoft Edge WebView2 Runtime installation failed (exit code ' + IntToStr(ResultCode) + ').' + #13#10 +
      'Salryn POS needs WebView2 to display the app window.' + #13#10 +
      'Check your internet connection, then re-run the Salryn installer as administrator.',
      mbError, MB_OK);
    Result := False;
    Exit;
  end;

  { Bootstrapper may return before registry is fully written; re-check briefly. }
  if not IsWebView2RuntimeInstalled then
  begin
    Sleep(2000);
  end;

  if not IsWebView2RuntimeInstalled then
  begin
    MsgBox(
      'WebView2 Runtime setup finished, but Salryn could not confirm the runtime is installed.' + #13#10 +
      'Reboot this PC, then open Salryn POS from the Start Menu or desktop shortcut.' + #13#10 +
      'If the app still cannot start, re-run Salryn-POS-BetaRC1-Setup.exe.',
      mbError, MB_OK);
    Result := False;
    Exit;
  end;

  Result := True;
end;

{ Prefer machine install confirmation; also accept HKCU per-user installs.
  Skip bootstrapper only when a non-zero pv is present. If registry is absent
  or empty, always run Evergreen Bootstrapper (internet required). }

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if IsWebView2RuntimeInstalled then
    begin
      Log('WebView2 Runtime already present (registry pv); skipping bootstrapper.');
    end
    else
    begin
      Log('WebView2 Runtime not detected via registry; running Evergreen Bootstrapper.');
      if not InstallWebView2Runtime then
      begin
        RaiseException(
          'Salryn POS requires Microsoft Edge WebView2 Runtime. Setup could not install it. ' +
          'Re-run the installer with internet access, or contact salryn.info@gmail.com.');
      end;
    end;
  end;
end;

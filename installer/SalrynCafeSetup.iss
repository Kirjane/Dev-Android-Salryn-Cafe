; Salryn Cafe — Independent Inno Setup 6 installer script (ADR-I1 / Issue #3).
;
; Coexists side-by-side with protected General Merchandise / Retail ("Salryn POS"):
;   - Protected Retail dir:   C:\Program Files\Kirjane Information Technology Services
;   - Independent Cafe dir:   C:\Program Files\Kirjane Information Technology Services - Cafe
;   - Independent Data root:  C:\ProgramData\SalrynCafe  (NEVER removed on uninstall/upgrade)
;   - Independent LocalApp:   %LocalAppData%\SalrynCafe  (Licensing, WebView2, NativePdf)
;   - Independent Ports:      127.0.0.1:5399 (loopback) / :5400 (LAN HTTPS)
;   - Distinct AppId GUID:    {8F4C2E19-6B3A-4D7E-9A51-3C8E7F2B4D60}
;   - Distinct Mutexes:       Global\Kirjane.SalrynCafe.Desktop, Global\Kirjane.SalrynCafe.LocalApi

#define MyAppName      "Salryn Cafe"
#define MyAppShortName "SalrynCafe"
#define MyAppPublisher "Kirjane Information Technology Services"
#define MyAppExeName   "Salryn.Desktop.WebView.exe"
#define MyAppIcon      "..\assets\branding\salryn-icon.ico"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0-rc1"
#endif
#ifndef StagingDir
  #define StagingDir "..\artifacts\installer\staging\SalrynCafe"
#endif
#ifndef SetupOutputDir
  #define SetupOutputDir "..\artifacts\installer\setup"
#endif
#ifndef MySourceRevision
  #define MySourceRevision ""
#endif
#if MySourceRevision != ""
  #define MyVersionInfoDesc "Salryn Cafe " + MySourceRevision
  #define MyAppComments     "Salryn Cafe source revision " + MySourceRevision
#else
  #define MyVersionInfoDesc "Salryn Cafe Setup (" + MyAppVersion + ")"
  #define MyAppComments     "Salryn Cafe"
#endif
; Independent static AppId for Salryn Cafe — MUST NEVER match Retail Salryn POS GUID.
#define MyAppGuid      "8F4C2E19-6B3A-4D7E-9A51-3C8E7F2B4D60"
#define WebView2ClientId "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"

[Setup]
AppId={{{#MyAppGuid}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppComments={#MyAppComments}
VersionInfoDescription={#MyVersionInfoDesc}
VersionInfoProductName={#MyAppName}
VersionInfoProductTextVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/Kirjane/Dev-Salryn-Cafe
AppSupportURL=mailto:salryn.info@gmail.com
AppUpdatesURL=https://github.com/Kirjane/Dev-Salryn-Cafe/releases
AppMutex=Global\Kirjane.SalrynCafe.Desktop,Global\Kirjane.SalrynCafe.LocalApi

; Independent Cafe installation root — NEVER under Retail's "Kirjane Information Technology Services" folder.
DefaultDirName={autopf}\Kirjane Information Technology Services - Cafe
DefaultGroupName={#MyAppPublisher}\{#MyAppName}
DisableProgramGroupPage=no
UsePreviousAppDir=yes

OutputDir={#SetupOutputDir}
OutputBaseFilename=Salryn-Cafe-Setup
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
; Isolated Salryn Cafe shared data tree under C:\ProgramData\SalrynCafe.
; Never touches C:\ProgramData\Salryn (General Merchandise / Retail).
Name: "{commonappdata}\SalrynCafe"; Permissions: users-modify
Name: "{commonappdata}\SalrynCafe\data"; Permissions: users-modify
Name: "{commonappdata}\SalrynCafe\backups"; Permissions: users-modify
Name: "{commonappdata}\SalrynCafe\config"; Permissions: users-modify
Name: "{commonappdata}\SalrynCafe\exports"; Permissions: users-modify
Name: "{commonappdata}\SalrynCafe\logs"; Permissions: users-modify
Name: "{commonappdata}\SalrynCafe\receipts"; Permissions: users-modify

[Files]
Source: "{#StagingDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "redist\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\assets\branding\salryn-icon.ico"; WorkingDir: "{app}"; Comment: "Start Salryn Cafe"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\assets\branding\salryn-icon.ico"; Tasks: desktopicon; WorkingDir: "{app}"; Comment: "Start Salryn Cafe"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Remove only the Salryn Cafe application binaries folder on uninstall.
; C:\ProgramData\SalrynCafe (production database, backups, receipts, logs) is intentionally preserved.
Type: filesandordirs; Name: "{app}"

[Code]
function IsProtectedRetailInstallPath(const CandidateDir: String): Boolean;
var
  NormalizedCandidate: String;
  ProtectedRetailRoot: String;
begin
  NormalizedCandidate := Lowercase(RemoveBackslashUnlessRoot(Trim(CandidateDir)));
  ProtectedRetailRoot := Lowercase(RemoveBackslashUnlessRoot(ExpandConstant('{autopf}\Kirjane Information Technology Services')));
  Result :=
    (NormalizedCandidate = ProtectedRetailRoot) or
    (Pos(ProtectedRetailRoot + '\', NormalizedCandidate) = 1);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  // This gate also runs for /SILENT and /VERYSILENT, which skip the directory page.
  if IsProtectedRetailInstallPath(WizardDirValue) then
    Result := 'Salryn Cafe cannot be installed inside the protected Salryn General Merchandise folder.';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = wpSelectDir then
  begin
    if IsProtectedRetailInstallPath(WizardDirValue) then
    begin
      SuppressibleMsgBox(
        'Salryn Cafe cannot be installed inside the protected Salryn General Merchandise folder:' + #13#10 +
        ExpandConstant('{autopf}\Kirjane Information Technology Services') + #13#10 + #13#10 +
        'Please use the independent Salryn Cafe folder:' + #13#10 +
        ExpandConstant('{autopf}\Kirjane Information Technology Services - Cafe'),
        mbError, MB_OK, IDOK);
      Result := False;
    end;
  end;
end;

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
    SuppressibleMsgBox(
      'Salryn Cafe setup could not find the Microsoft Edge WebView2 Runtime bootstrapper.' + #13#10 +
      'Re-download Salryn-Cafe-Setup.exe from Kirjane Information Technology Services and try again.',
      mbError, MB_OK, IDOK);
    Result := False;
    Exit;
  end;

  WizardForm.StatusLabel.Caption := 'Installing Microsoft Edge WebView2 Runtime...';
  WizardForm.FilenameLabel.Caption := 'This may take a minute and requires internet access.';

  if not Exec(Bootstrapper, '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    SuppressibleMsgBox(
      'Salryn Cafe could not start the WebView2 Runtime installer.' + #13#10 +
      'Check your internet connection and run Salryn-Cafe-Setup.exe again as administrator.',
      mbError, MB_OK, IDOK);
    Result := False;
    Exit;
  end;

  if ResultCode <> 0 then
  begin
    SuppressibleMsgBox(
      'Microsoft Edge WebView2 Runtime installation failed (exit code ' + IntToStr(ResultCode) + ').' + #13#10 +
      'Salryn Cafe needs WebView2 to display the app window.',
      mbError, MB_OK, IDOK);
    Result := False;
    Exit;
  end;

  if not IsWebView2RuntimeInstalled then
  begin
    Sleep(2000);
  end;

  if not IsWebView2RuntimeInstalled then
  begin
    SuppressibleMsgBox(
      'WebView2 Runtime setup finished, but Salryn Cafe could not confirm the runtime is installed.' + #13#10 +
      'Reboot this PC, then open Salryn Cafe from the Start Menu or desktop shortcut.',
      mbError, MB_OK, IDOK);
    Result := False;
    Exit;
  end;

  Result := True;
end;

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
          'Salryn Cafe requires Microsoft Edge WebView2 Runtime. Setup could not install it. ' +
          'Re-run the installer with internet access, or contact salryn.info@gmail.com.');
      end;
    end;
  end;
end;

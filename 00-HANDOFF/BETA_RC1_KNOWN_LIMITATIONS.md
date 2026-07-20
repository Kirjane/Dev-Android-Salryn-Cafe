# Salryn POS Beta RC1 — Known Limitations

## Installer EXE

| Item | Status |
|---|---|
| Installer script (`installer/SalrynSetup.iss`) | Complete |
| Build script (`tools/installer/Build-SalrynBetaRc1Installer.ps1`) | Complete |
| Installer staging pipeline | Working |
| Setup EXE compile | **Blocked — Inno Setup 6 not yet installed on build machine** |

To unblock: install Inno Setup 6 from <https://jrsoftware.org/isinfo.php>, then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\installer\Build-SalrynBetaRc1Installer.ps1 -BuildInstaller
```

## Runtime / product limitations

- **No auto-update**: this is a manual-distribution beta.  Clients receive a new
  `Salryn-POS-BetaRC1-Setup.exe` from Kirjane Labs and re-run the installer.
- **Unsigned installer**: the setup EXE is not code-signed for Beta RC1.  Windows may
  show a SmartScreen warning ("Windows protected your PC").  The client must click
  "More info → Run anyway".
- **WebView2 runtime required**: if the client machine does not have Microsoft Edge
  WebView2 Runtime installed, Salryn POS will show a prompt to install it.  This is
  expected and the runtime is free.
- **No firewall rules**: the installer does not add Windows Firewall rules.  The local
  API binds to `127.0.0.1` only and is not reachable from other machines on the network
  via the app-shell path.
- **LAN terminals**: Beta RC1 installer does not automatically configure LAN terminal
  access.  LAN setup requires manual Host operator steps inside Salryn POS.
- **No BIR/RMO accreditation**: Beta RC1 is a private beta.  Official BIR accreditation
  is not claimed and is not part of this release.
- **Framework-dependent publish**: the installed application requires .NET 10 to be
  present on the machine.  A future release may use a self-contained publish to
  eliminate this dependency.

## Branding

- Banner assets (`salryn-banner-clear.png`, `salryn-banner-filled.png`) are packaged
  in the install folder for future installer wizard graphics but are not yet used as
  wizard side/header images in this script.

## Not in scope for this lane

- Cloud backup
- Licensing enforcement changes
- RMO / BIR / eSales production behaviour
- Hardware (receipt printers, barcode scanners, cash drawers) proof
- Automated update channel

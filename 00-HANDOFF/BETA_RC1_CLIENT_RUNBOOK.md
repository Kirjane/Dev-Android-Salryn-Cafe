# Salryn POS Beta RC1 — Client Runbook

This document is written for the person who receives the Salryn POS Beta RC1 installer
and wants to install, launch, and operate it.  No technical expertise is required.

---

## What you receive

- `Salryn-POS-BetaRC1-Setup.exe` — the Windows installer.

---

## System requirements

- Windows 10 or Windows 11 (64-bit)
- An administrator account (for the initial installation)
- Internet access during installation (so Salryn can install Microsoft Edge WebView2 Runtime if it is not already on the PC)
- .NET 10 Desktop Runtime (the installer or first launch will inform you if it is missing)

You do **not** need to install Microsoft Edge WebView2 Runtime yourself.
Salryn setup installs or confirms it automatically.

---

## Installation

1. Double-click **`Salryn-POS-BetaRC1-Setup.exe`**.
2. Accept the UAC prompt ("Do you want to allow this app to make changes?").
3. Follow the on-screen wizard.  The default install location is:
   ```
   C:\Program Files\Kirjane Labs\Salryn POS
   ```
4. When prompted you can opt in to a **desktop shortcut**.
5. If WebView2 Runtime is missing, setup installs it quietly (may take a minute).
6. Click **Finish**.  You can choose to launch Salryn POS immediately.

---

## Launching Salryn POS

- **Desktop shortcut**: double-click **Salryn POS** on the desktop (if created).
- **Start Menu**: open Start → search "Salryn" → click **Salryn POS** under
  _Kirjane Labs\Salryn POS_.

You do **not** need to open PowerShell, a browser, or navigate to localhost.
Salryn POS opens its own window and manages its own local server automatically.

---

## First run

On first launch Salryn POS will:

1. Start its local API service in the background.
2. Open the Salryn POS window (a self-contained desktop application).
3. Prompt you to activate your license.

Email your Machine Code to **salryn.info@gmail.com** to receive a signed license file.
Import it through the License page inside Salryn POS.

---

## Production data

Your store data (products, sales, receipts, backups) is stored under:

```
C:\ProgramData\Salryn\
```

This folder is **never deleted** by the uninstaller or by an upgrade.
Back up this folder regularly using the Backup & Restore page inside Salryn POS.

---

## Updating / reinstalling

Run the new `Salryn-POS-*-Setup.exe` over the existing installation.
Your production data in `C:\ProgramData\Salryn` will be preserved.

---

## Uninstalling

Open **Settings → Apps** (or **Control Panel → Programs**) and uninstall **Salryn POS**.
Your production data in `C:\ProgramData\Salryn` will **not** be deleted.

---

## Support

Beta support: **salryn.info@gmail.com**

Please include your Machine Code, a description of the issue, and any screenshots.

# Salryn I1 Pre-Beta Package Runbook

## Package type

This is a controlled, framework-dependent browser package for pre-beta proof. It is not an installer, auto-updater, Windows service, or production-activation package.

The package contains the published `Salryn.Api.Local.exe` host and the production web `dist` beside it. A compatible .NET 10 runtime is required on the Windows machine.

## Launch

Before the first normal launch on a client PC, open PowerShell in the extracted package folder and run:

```powershell
.\Prepare-Salryn-PortableBeta.ps1
```

An Administrator PowerShell may be required to create `C:\ProgramData\Salryn` and grant the current Windows user Modify access. The script creates the required `backups`, `config`, `data`, `exports`, `logs`, and `receipts` folders, verifies that the data folder is writable, and reports a clear success or failure. It does not delete store data, alter the active license, or enable development mode.

After preparation succeeds, launch from PowerShell:

```powershell
cd C:\Users\kirch\Salryn\artifacts\i1-package\Salryn
.\Salryn.Api.Local.exe
```

Then open:

```text
http://127.0.0.1:5299/
```

For isolated maintainer proof only, `--dev` stores the database under the repository's `.salryn-dev` folder. Beta users should not use `--dev`.

No npm, Vite dev server, Visual Studio, or source checkout is required for normal package launch.

## First run

1. Open `http://127.0.0.1:5299/`.
2. On the activation page, copy the Host Machine Code.
3. Obtain a valid signed Host license for that exact machine from the authorized license desk.
4. Import or paste the signed Host license.
5. Complete the first-admin setup.
6. Sign in with the new Admin account.

The active local license is preserved outside the package at `%LocalAppData%\Salryn\Licensing\license.json`. Store data is preserved outside the package under `ProgramData\Salryn` in normal production launch mode.

## Normal operator checks

- Confirm the Dashboard loads after sign-in.
- Open a shift before checkout.
- Confirm the product and quantity before completing payment.
- Use Sales & Receipts to view the saved receipt snapshot and request a reprint.
- A reprint must not create a sale, payment, or stock movement.

## Backup and restore

- Use Backup & Restore with an authorized Admin account.
- Create and verify a backup before relying on it.
- Restore only from a known verified package during controlled downtime.
- The restore confirmation phrase is `RESTORE SALRYN BACKUP`.
- Preserve any automatic safety archive and restart Salryn after restore when directed by the UI/runbook.

## LAN terminals

Use Terminal Management for the current reachable terminal link and firewall guidance. Physical terminal reachability remains a manual check. A changed LAN bind may require restarting Salryn; no-restart runtime rebind is deferred.

## Stop

Close the PowerShell window or stop the `Salryn.Api.Local.exe` process after confirming no checkout, backup, or restore operation is active.

## Desktop-shell status

`DESKTOP_APP_SHELL_DEFERRED`

The current WebView2 project remains a development host and is not included as the pre-beta launch path. No desktop shortcut or packaged product icon is claimed. The supported I1 proof launch is the browser package above.

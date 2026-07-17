# Fresh-state helpers for local Salryn QA

These scripts are for **local developer QA only**. They rename state folders to timestamped backups. They do **not** delete data. They do **not** touch production `C:\ProgramData\Salryn` unless you intentionally run a future production-specific script (not provided here).

## State truth

| What | Path | Used by |
| --- | --- | --- |
| **--dev SQLite root** | `{repo}\.salryn-dev` | `dotnet run --project src\Salryn.Api.Local -- --dev` |
| **--dev database** | `{repo}\.salryn-dev\data\salryn.db` | Users, store name, audit, LAN terminals, sessions |
| **Production root** | `C:\ProgramData\Salryn` | Non-`--dev` launches |
| **Production DB** | `C:\ProgramData\Salryn\data\salryn.db` | Production store data |
| **License + installation id** | `%LOCALAPPDATA%\Salryn\Licensing\` | Signed license, machine binding material |
| **Native PDF reports** | `%LOCALAPPDATA%\Salryn\Reports\NativePdf\` | Report artifacts |
| **Browser storage** | cookies / localStorage / sessionStorage for the site origin | Host session cookie client-side residual, LAN terminal client, hidden terminals |

## Why the old reset failed

`Backup-And-Reset-DevSalrynState.ps1` previously only moved `%LOCALAPPDATA%\Salryn`.

Launching with `--dev` still read `{repo}\.salryn-dev\data\salryn.db`, so operators still saw DEVKIRCH store names, users, audit history, and LAN footprints.

## Which script to use

| Goal | Script |
| --- | --- |
| Fresh **--dev DB** (users/store/audit/LAN) | `Backup-And-Reset-DevProjectState.ps1` |
| Fresh **license / machine identity** only | `Backup-And-Reset-LocalAppDataState.ps1` |
| Full local **--dev QA** reset | `Backup-And-Reset-AllDevState.ps1` |
| Legacy name (wrapper) | `Backup-And-Reset-DevSalrynState.ps1` → same as AllDevState |

## Recommended full --dev fresh sequence

```powershell
cd C:\Users\kirch\Salryn

powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev\Backup-And-Reset-AllDevState.ps1 -WhatIf

powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev\Backup-And-Reset-AllDevState.ps1

dotnet run --project src\Salryn.Api.Local -- --dev
```

Open: `http://127.0.0.1:5299/license`

### Browser cleanup (manual)

1. DevTools → Application → Clear site data for `http://127.0.0.1:5299`
2. Close/reopen tab or Ctrl+F5
3. Clear other origins if testing LAN terminal browser state

### Expected after full reset

- No DEVKIRCH TEST STORE from old `.salryn-dev`
- No signed-in DEVKirch / old users
- No audit log history from old DB
- No LAN terminal rows from old DB
- License/activation or first-run path depending on whether a license remains and env flags
- If `SALRYN_LICENSE_DEVELOPMENT_MODE=1`, License page may still be skipped by design

## Restore

Stop Salryn processes first, then rename backups back:

```powershell
# Example — use the printed backup paths from the script output
Rename-Item -LiteralPath "C:\Users\kirch\Salryn\.salryn-dev.backup-YYYYMMDD-HHMMSS" -NewName ".salryn-dev"
Rename-Item -LiteralPath "$env:LOCALAPPDATA\Salryn.backup-YYYYMMDD-HHMMSS" -NewName "Salryn"
```

## Safety

- Uses `Rename-Item` only (no `Remove-Item` state wipe)
- Supports `-WhatIf`
- Blocks if `Salryn.Api.Local` or `Salryn.App.WinForms` is running
- Does not require admin rights for LocalAppData / repo folder operations

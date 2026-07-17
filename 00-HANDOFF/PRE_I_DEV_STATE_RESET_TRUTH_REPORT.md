# PRE-I Dev State Reset Truth — Repair Report

## 1. Branch / base / final SHA

| Item | Value |
| --- | --- |
| Branch | `KIRCH-PRE-I-DEV-STATE-RESET-TRUTH` |
| Base branch | `KIRCH-PRE-I-SURGICAL-STATE-LICENSE-UX-REVIEW` |
| Base SHA | `e01d52616da2943dbe4ce91f2b5ab48a82792710` |
| Final tip SHA | `d9104198adccd20b2b5eec13d8bcd985d22d6aec` |
| Protected | `KIRCH-SANDBOX`, `KIRCH-INSTALLER-M1` not modified |

## 2. Problem proven by Kirch screenshot / run

Kirch ran the previous `Backup-And-Reset-DevSalrynState.ps1`, then:

```text
dotnet run --project src\Salryn.Api.Local -- --dev
```

and still observed:

- DEVKIRCH TEST STORE
- signed-in DEVKirch
- audit logs
- users
- LAN/state footprints

That residual data lives in **`{repo}\.salryn-dev\data\salryn.db`**, which the old helper never moved.

## 3. State path truth table

| Mode | Root | Database |
| --- | --- | --- |
| `--dev` | `{repo}\.salryn-dev` | `{repo}\.salryn-dev\data\salryn.db` |
| Production | `C:\ProgramData\Salryn` | `C:\ProgramData\Salryn\data\salryn.db` |
| License / installation id | `%LOCALAPPDATA%\Salryn\Licensing\` | `license.json`, `installation-id.txt` |
| Native PDF reports | `%LOCALAPPDATA%\Salryn\Reports\NativePdf\` | artifacts |
| Browser | cookies / localStorage / sessionStorage | not on disk under those roots |

Evidence: `DbPaths.CreateDevelopment` → `.salryn-dev`; `CreateProduction` → `CommonApplicationData\Salryn`.

## 4. Why old reset failed

| Claim (old helper) | Reality |
| --- | --- |
| “Fresh-state for local QA” | Only renamed `%LOCALAPPDATA%\Salryn` |
| Expect no users / no login after reset | False for `--dev` — SQLite users remain in `.salryn-dev` |
| License landing guaranteed | Only if license was in LocalAppData **and** DB path was also fresh |

**Root cause:** instruction + helper **mismatch** with real `--dev` DB path (documented in surgical review). Not an App.tsx routing bug.

## 5. Scripts added/changed

| Path | Action |
| --- | --- |
| `tools/dev/Backup-And-Reset-DevProjectState.ps1` | **Added** — resets `.salryn-dev` |
| `tools/dev/Backup-And-Reset-LocalAppDataState.ps1` | **Added** — resets `%LOCALAPPDATA%\Salryn` |
| `tools/dev/Backup-And-Reset-AllDevState.ps1` | **Added** — both + browser checklist |
| `tools/dev/Backup-And-Reset-DevSalrynState.ps1` | **Changed** — compatibility wrapper → AllDevState |
| `tools/dev/README-FRESH-STATE.md` | **Added** — operator guide |

## 6. What each script resets

| Script | Resets |
| --- | --- |
| DevProjectState | `{repo}\.salryn-dev` (users, store, audit, LAN, sessions for `--dev`) |
| LocalAppDataState | `%LOCALAPPDATA%\Salryn` (license, installation id, local reports) |
| AllDevState | Both of the above |
| DevSalrynState (compat) | Same as AllDevState |

## 7. What each script does not reset

- `C:\ProgramData\Salryn` (production)
- Browser cookies / localStorage / sessionStorage (manual checklist only)
- Running process memory until process is stopped
- `SALRYN_LICENSE_DEVELOPMENT_MODE` environment flag (operator-controlled)

## 8. Safety behavior

- `Rename-Item` only — **no** `Remove-Item` for state wipe
- `-WhatIf` dry run on all scripts
- Blocks if `Salryn.Api.Local` or `Salryn.App.WinForms` is running
- Prints source + backup paths
- PowerShell 5.1 compatible, no external modules
- No admin required for LocalAppData / repo folder rename

## 9. Browser storage instructions

After filesystem reset:

1. Stop API/host processes  
2. DevTools on `http://127.0.0.1:5299` → Clear site data  
3. Clear other origins if testing LAN terminal browser state  
4. Close/reopen tab or Ctrl+F5  

Printed by `Backup-And-Reset-AllDevState.ps1`.

## 10. Exact user commands

```powershell
cd C:\Users\kirch\Salryn

powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev\Backup-And-Reset-AllDevState.ps1 -WhatIf

powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev\Backup-And-Reset-AllDevState.ps1

dotnet run --project src\Salryn.Api.Local -- --dev
```

Open: `http://127.0.0.1:5299/license`

Expect: no DEVKIRCH store, no signed-in user, no audit history, no LAN DB footprints (after browser clear if needed).

## 11. Restore commands

```powershell
# Use paths printed by the scripts
Rename-Item -LiteralPath "C:\Users\kirch\Salryn\.salryn-dev.backup-YYYYMMDD-HHMMSS" -NewName ".salryn-dev"
Rename-Item -LiteralPath "$env:LOCALAPPDATA\Salryn.backup-YYYYMMDD-HHMMSS" -NewName "Salryn"
```

App must be stopped first.

## 12. Tests/gates

| Gate / check | Result |
| --- | --- |
| `pre-i-dev-state-reset-truth` | PASS (source + script inspection) |
| Script `-WhatIf` smoke (manual/CI-friendly) | PASS |
| Related PRE-I gates | PASS after compat-helper guard update |
| Full CoreSmoke | PASS |
| `npm run build` | PASS |
| `git diff --check` | PASS |

## 13. Remaining risks

- Operators may still run only LocalAppData reset by habit — docs + wrapper messaging mitigate  
- Browser state can still show residual client UI until cleared  
- Development license env can skip License page by design  
- Production ProgramData wipe is intentionally out of scope  

## 14. Final decision

**`PRE_I_DEV_STATE_RESET_TRUTH_REPAIRED_AND_PASSED`**

No License page design work. No package/I1. No promotion.

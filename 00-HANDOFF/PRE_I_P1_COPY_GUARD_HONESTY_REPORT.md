# PRE-I P1 Copy / Guard Honesty Report

## 1. Branch / base / final SHA

| Item | Value |
| --- | --- |
| Branch | `KIRCH-PRE-I-P1-COPY-GUARD-HONESTY` |
| Base branch | `KIRCH-PRE-I-TERMINAL-LIFECYCLE-UX-REPAIR` |
| Base SHA | `bbcfab0822d71b8a49d347e7ace1c88d1982d6e6` |
| Audit guidance | `KIRCH-PRE-I-EXTREME-CODE-QUALITY-AUDIT` @ `f5d8499…` |
| Final SHA | branch tip after push (`git rev-parse HEAD`) |
| Protected refs | Untouched |

## 2. Audit findings addressed

| ID | Status |
| --- | --- |
| P1-001 sr-only theater | Fixed |
| P1-002 string-only over-reliance | Thin behavioral restore + fresh DB proof added |
| P1-003/004 Revoke residue | Operator Remove/Removed; enum stays Revoked |
| P1-005 Clear sessions confirm | ConfirmDialog |
| P1-006 Replace license re-ack | terms + replace confirm required |
| P1-007 LicensingOps-heavy UI | Softened primary; desk wording; request block still support-compatible |
| P1-008 debug wording | Advanced support only |
| P1-009 restore phrase | UI types and sends `RESTORE SALRYN BACKUP` |
| P1-010 schema label | Backup format version |
| P1-011 Sale ID | Collapsed under Support details |
| P1-012/013 report SHA | This report stamps tip on commit |

## 3. Hidden guard theater removed

Deleted `TerminalActivationPage.tsx` `sr-only` / `aria-hidden` phrase dump. Required phrases remain in **visible** wizard/import copy.

## 4. Guard improvements

- New `PreIP1CopyGuardHonestyChecks` + CoreSmoke `pre-i-p1-copy-guard-honesty`
- Behavioral: backup restore rejects `RESTORE` alone; accepts `RESTORE SALRYN BACKUP`
- Behavioral: fresh DB init has **zero users**, no license JSON under data root
- Source: replace requires `termsAccepted && replaceConfirmed`; restore sends typed text
- Updated lifecycle group title guard to `Retired / removed terminals`

## 5. Copy cleanup

- Host LAN: Remove (revoke) → Remove terminal; Removed/removed history wording
- Status label for Revoked → **Removed** (UI)
- LicensingOps softened toward Kirjane Labs / Salryn Infodesk; request block retains desk tooling line
- Remote POS product ID: Advanced support only
- License placeholders: Infodesk-facing

## 6. Clear sessions confirmation

`pendingClearSessionsTerminal` + ConfirmDialog:

- Title: Clear operator sessions?
- Body: signs out sessions; terminal remains paired
- Confirm / Cancel: Clear sessions / Keep sessions

## 7. Replace license re-ack

`canInstallReplace` requires:

1. Non-empty license text  
2. Terms re-ack checkbox  
3. “replaces the active local license for this device” checkbox  

First-time wizard terms gate unchanged.

## 8. Backup restore phrase alignment

| Layer | Phrase |
| --- | --- |
| UI type box | `RESTORE SALRYN BACKUP` |
| API body | same typed string |
| Backend | `SqliteBackupRecoveryService.RestoreConfirmationText` |

No silent dual-phrase constant.

## 9. Checkout completion cleanup

Primary: receipt number + store records wording.  
Collapsed **Support details**: checkout receipt number non-official note + Sale ID.

## 10. Fresh Salryn state proof

**Automated**

- App route order: license → bootstrap → login  
- Fresh composition DB: `users` count = 0; no `*license*.json` under root  
- Existing `pre-i1-fresh-license-first-flow` still PASS  

**Helper (optional)**

`tools/dev/Backup-And-Reset-DevSalrynState.ps1` — renames `%LOCALAPPDATA%\Salryn` to `Salryn.backup-YYYYMMDD-HHMMSS` (no delete).

## 11. Fresh-state manual reset commands

```powershell
# 1) Stop app processes
Get-Process Salryn.Api.Local,Salryn.App.WinForms -ErrorAction SilentlyContinue | Stop-Process -Force

# 2) Backup-rename local state (safe)
$src = Join-Path $env:LOCALAPPDATA "Salryn"
$bak = Join-Path $env:LOCALAPPDATA ("Salryn.backup-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
if (Test-Path $src) { Rename-Item -LiteralPath $src -NewName (Split-Path $bak -Leaf) }

# Or:
# powershell -File tools\dev\Backup-And-Reset-DevSalrynState.ps1

# 3) Launch
dotnet run --project src\Salryn.Api.Local -- --dev

# 4) Confirm in browser
# - /license fresh landing
# - no active license, no login/dashboard/bootstrap first
# - do NOT set SALRYN_LICENSE_DEVELOPMENT_MODE=1 for this QA

# 5) Restore later (app stopped)
# Rename-Item $bak Salryn
```

Do not commit local DB/state. Existing DB upgrade path remains separate from fresh path.

## 12. Files changed

| File | Change |
| --- | --- |
| `TerminalActivationPage.tsx` | Remove sr-only dump |
| `HostLanControlPage.tsx` | Copy cleanup; clear-sessions ConfirmDialog |
| `LicensePage.tsx` | Replace re-ack; Infodesk wording |
| `BackupRestorePage.tsx` / `api/backups.ts` | Full restore phrase |
| `CheckoutPage.tsx` | Sale ID under support details |
| `RemotePosClientPage.tsx` | Advanced support only |
| `terminalLifecycleUx.ts` | Removed labels / group title |
| `PreIP1CopyGuardHonestyChecks.cs` | New focused + behavioral proofs |
| `PreIActivationLanding…` / `V2TerminalLifecycle…` | Guard updates |
| `CoreSmoke/Program.cs` | Register check |
| `tools/dev/Backup-And-Reset-DevSalrynState.ps1` | Safe reset helper |
| This report | Handoff |

## 13. Tests / gates

| Gate | Result |
| --- | --- |
| pre-i-p1-copy-guard-honesty | PASS |
| pre-i-license-landing-sell-upgrade | PASS |
| pre-i-activation-landing-manual-qa-repair | PASS |
| pre-i-terminal-lifecycle-ux-repair | PASS |
| pre-i1-fresh-license-first-flow | PASS |
| pre-beta-d-portable-backup-import-export | PASS |
| pre-i1-lan-terminal-activation-management-beta | PASS |
| web-a3 / web-a4 | PASS |
| v2-terminal-lifecycle / v2-host-lan-control-ux | PASS |
| lic-beta-manual-host-terminal-activation | PASS |
| **full CoreSmoke** | **PASS** |
| npm web build | PASS |

## 14. Remaining P1/P2/P3 risks

- **P2:** giant pages, internal official pages in bundle, WinForms wording drift  
- **P3:** no logo assets; hardware proof pending; terms still UI-state only  
- **Manual:** Kirch still needs screenshot QA on fresh rename + lifecycle + restore phrase  
- **LicensingOps** may still appear in admin request block (intentional support desk)

## 15. Final decision

**PRE_I_P1_COPY_GUARD_HONESTY_REPAIRED_AND_PASSED**

No package / Lane I / promotion in this lane.

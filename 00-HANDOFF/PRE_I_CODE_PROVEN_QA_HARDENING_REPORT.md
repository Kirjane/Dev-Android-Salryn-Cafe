# PRE-I Code-Proven QA Hardening Report

## 1. Branch / base / final SHA

| Item | Value |
| --- | --- |
| Branch | `KIRCH-PRE-I-CODE-PROVEN-QA-HARDENING` |
| Base branch | `KIRCH-PRE-I-AUTOMATED-QA-PROOF` |
| Base SHA | `f3b4bdf705890806a86bec729bb7b43ad689d666` |
| Final SHA | *(filled after commit/push)* |

Protected refs (`KIRCH-SANDBOX`, production tags) were not promoted, merged, or reset.

## 2. Code proof added

New focused CoreSmoke gate: **`pre-i-code-proven-qa-hardening`** (also wired into full CoreSmoke).

| File | Role |
| --- | --- |
| `tests/Salryn.Tests/PreICodeProvenQaHardeningChecks.cs` | Orchestrator |
| `tests/Salryn.Tests/PreICodeProvenFreshStateChecks.cs` | Isolated fresh backend + route contract |
| `tests/Salryn.Tests/PreICodeProvenBackupRestoreChecks.cs` | Phrase + backend accept/reject fixture |
| `tests/Salryn.Tests/PreICodeProvenTerminalLifecycleChecks.cs` | Service lifecycle + helper contracts |
| `tests/Salryn.Tests/PreICodeProvenClientSurfaceGuards.cs` | Forbidden claims, dialogs, replace, checkout |
| `tests/Salryn.Tests/PreICodeProvenCheckSupport.cs` | Shared assertion helpers |
| `tools/Salryn.Tools.CoreSmoke/Program.cs` | Register `--only pre-i-code-proven-qa-hardening` + full run |

No product redesign. No package/I1. No real LocalAppData mutation.

## 3. What is now fully code-proven

| Target | Proof type |
| --- | --- |
| Fresh backend state | **Behavioral** isolated temp root/DB |
| No license/session/users | **Behavioral** (same isolated root) |
| Backup restore phrase + backend | **Behavioral** temporary package fixture + phrase constants |
| Terminal lifecycle backend | **Behavioral** TerminalRegistryService on isolated DB |
| Forbidden claim regression | **Source guards** over client surfaces (no browser) |

## 4. What is still source-proven only

| Target | Why |
| --- | --- |
| Terminal Management dialog wiring | No React harness; structural ConfirmDialog + pending-state proof |
| Replace License terms/re-ack | Source gating (`canInstallWizard` / `canInstallReplace` / import→terms) |
| Checkout completion hierarchy | Source order of receipt vs Support details vs Sale ID |
| Host-local non-removable clients | UI helpers in `terminalLifecycleUx.ts`; backend is permission-scoped, not role-scoped |
| Full browser route for unlicensed host `/license` | Dev license baseline can short-circuit host browser; not fabricated |

## 5. Fresh backend state proof

Isolated root: `%TEMP%\salryn-code-proven-fresh-<guid>` via `DbPaths.CreateDevelopment`.

Asserts:

- unique temp root / paths bound under isolation
- DB initializes (`salryn.db` exists)
- `users` = 0
- `cashier_sessions` = 0 (including active open sessions)
- `deployment_identity` = 1 baseline row
- no `DEVKIRCH` / `Salryn Dev` in `store_settings.store_name`
- license store Unlicensed, no Host/Terminal type, no license JSON under root
- API session unauthenticated; session store empty
- App.tsx order: License before Bootstrap before Login; renders `<LicensePage />`

## 6. No license/session/users proof

Explicitly re-asserted inside the same fresh-state fixture (users 0, sessions 0, license Unlicensed/missing file, no auth session). Not a separate product surface.

## 7. Backup restore proof

| Check | Result |
| --- | --- |
| UI phrase `RESTORE SALRYN BACKUP` | Source + runtime constant |
| API client constant matches backend | Source |
| UI sends `restoreText` unchanged | Source `restoreBackup(restoreTarget, restoreText)` |
| Label **Backup format version** | Source |
| Rejects `Schema / app migration` label | Source |
| Backend rejects `RESTORE` | Behavioral |
| Backend rejects `RESTORE BACKUP` | Behavioral |
| Backend rejects empty / whitespace | Behavioral |
| Backend accepts exact phrase | Behavioral (temp fixture only) |

## 8. Terminal lifecycle backend proof

Isolated DB + `TerminalRegistryService`:

| Step | Result |
| --- | --- |
| Seed Active CashierClient | Pass |
| View-only actor cannot disable | Pass (manage permission required) |
| Active → Disabled + `DisabledAtUtc` set | Pass |
| Disabled → Active + `DisabledAtUtc` cleared | Pass |
| Active cannot enable again | Pass (`EnableRequiresDisabled`) |
| Disabled → Revoked + `RevokedAtUtc` | Pass |
| Revoked cannot enable | Pass (`CannotEnableRevoked`) |
| HostConsole row can exist without being managed remote | Pass (seed + presence) |

Helper source contracts: Active/Disable, Disabled/Enable+Remove, managed remote roles, host-local roles, Removed label, host-local group.

## 9. Forbidden claim regression proof

Client surfaces scanned (joined source, ignore-case where helpers use it):

LicensePage, TerminalActivationPage, HostLanControlPage, StoreSettingsPage, BackupRestorePage, CheckoutPage, RemotePosClientPage, ProductCreditsCard, AppShell.

Rejected: cloud activation, BIR-ready, official receipt accreditation, ESC/POS supported, cash drawer supported, drawer kick, cutter supported, hardware bundle, Products of Kirjane Labs, mock sample, Build Type, Publisher block, find Host License ID / Store ID tasks.

TerminalActivation additionally rejects payload/schema/wrapper/raw/debug as normal copy.

Historical handoff docs are not scanned.

## 10. Terminal Management dialog wiring proof

Structural source proof on HostLanControlPage:

- pending disable/enable/remove/clear-sessions state
- four ConfirmDialog titles
- row handlers set pending only (`setPending*`)
- row does not call destructive mutations directly
- confirm handlers call `disableTerminalMutation` / `enableDisabledTerminalMutation` / `revokeTerminalMutation` / `clearTerminalSessionsMutation`
- showDisable / showEnable / showRemove gates
- no `Restore (not supported)` / primary `>Revoke<`
- help copy “Remove terminal only when…”

## 11. Replace License re-ack proof

Source contract:

- first install: text + terms (`canInstallWizard`)
- replace: text + terms re-ack + replace consequence (`canInstallReplace`)
- import step forced through terms
- replace button `disabled={!canInstallReplace}`
- re-ack copy strings present
- no Host/Store ID task burden

## 12. Checkout completion hierarchy proof

Source order on CheckoutPage:

1. `Cash sale committed`
2. `Keep this receipt number for your store records.`
3. `Support details`
4. `Sale ID:`

No `debug` wording on the normal Checkout page source.

## 13. Tests/gates run

| Gate / command | Result |
| --- | --- |
| `dotnet build Salryn.sln` | PASS |
| `--only pre-i-code-proven-qa-hardening` | PASS |
| `--only pre-i-automated-qa-proof` | PASS |
| `--only pre-i-p1-copy-guard-honesty` | PASS |
| `--only pre-i-terminal-lifecycle-ux-repair` | PASS |
| `--only pre-i-license-landing-sell-upgrade` | PASS |
| `--only pre-beta-d-portable-backup-import-export` | PASS |
| `--only pre-i1-fresh-license-first-flow` | PASS |
| `--only pre-i1-lan-terminal-activation-management-beta` | PASS |
| Full CoreSmoke | *(recorded in final handoff if green)* |
| `npm --prefix src\Salryn.App.Web run build` | PASS (bundle-size advisory only) |
| `git diff --check` | PASS |

## 14. Remaining manual-only screenshots

- Fresh host `/license` visual (no Dashboard bypass under true unlicensed install)
- Terminal Management Active/Disabled confirm dialogs (authenticated host)
- Backup restore dialog typing the full phrase
- Replace License re-ack UI with active license
- Checkout Sale Completed hierarchy (after real sale)

No screenshots committed; no fake browser proof claimed.

## 15. Remaining risks

- Host browser license landing still hard to auto-prove under development license baseline without manufacturing product state.
- Terminal Management and Replace License remain structural UI contracts until a React harness exists.
- Backend will still allow manage-permission actors to disable a HostConsole row if called outside UI helpers; host-local protection is intentional UI-layer.
- No package/I1, BIR/RMO certification, printer, or hardware claims authorized.

## 16. Final decision

**`PRE_I_CODE_PROVEN_QA_HARDENING_PASSED`**

Requested fully code-provable items are behavioral against isolated roots. Dialog/replace/checkout hierarchy are stronger structural source contracts, honestly labeled, not browser theater.

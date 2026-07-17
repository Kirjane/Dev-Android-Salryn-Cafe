# PRE-I - Terminal Lifecycle UX Repair Report

## 1. Branch / base / final SHA

| Item | Value |
| --- | --- |
| Branch | `KIRCH-PRE-I-TERMINAL-LIFECYCLE-UX-REPAIR` |
| Base branch | `KIRCH-PRE-I-LICENSE-LANDING-SELL-UPGRADE` |
| Base SHA | `b0ab67ba0c44cc388fe85dc074ca430cd458a5d8` |
| Final SHA | branch tip after push (see git log -1) |
| Protected refs | Untouched |

## 2. Manual QA issue

Previous PRE-I work left Terminal Management lifecycle as:

- Disable / Revoke / Clear operator sessions
- Restore (not supported) disabled button
- Copy: Re-enable is not supported in this build

Kirch required product labels **Disable terminal / Enable terminal / Remove terminal**, confirmation guards, Remove only after Disable, and real Enable (no fake restore).

## 3. Files changed

| File | Change |
| --- | --- |
| `src/Salryn.Core/Services/ITerminalRegistryService.cs` | `EnableTerminalAsync` |
| `src/Salryn.Infrastructure/Data/Services/TerminalRegistryService.cs` | Enable Disabled only; refuse Revoked |
| `src/Salryn.Api.Local/Services/LocalLanControlService.cs` | Enable action + honest messages |
| `src/Salryn.Api.Local/LocalLanControlEndpoints.cs` | `POST .../terminals/{id}/enable` |
| `src/Salryn.App.Web/src/api/remoteReadOnlyClient.ts` | `enableLocalLanControlDisabledTerminal` |
| `src/Salryn.App.Web/src/hooks/useLocalLanControl.ts` | `enableDisabledTerminalMutation` |
| `src/Salryn.App.Web/src/utils/terminalLifecycleUx.ts` | Enable/Remove gates; Remove only when Disabled |
| `src/Salryn.App.Web/src/pages/HostLanControlPage.tsx` | Labels + ConfirmDialogs; no fake restore |
| `tests/Salryn.Tests/V2TerminalLifecycleUxChecks.cs` | Updated for real Enable + new labels |
| `tests/Salryn.Tests/V2HostLanControlUxChecks.cs` | Require Enable path |
| `tests/Salryn.Tests/LanM15CLocalControlChecks.cs` | Enable service guard |
| `tests/Salryn.Tests/PreITerminalLifecycleUxRepairChecks.cs` | PRE-I lifecycle source guards |
| `tools/Salryn.Tools.CoreSmoke/Program.cs` | Register check |
| `00-HANDOFF/PRE_I_TERMINAL_LIFECYCLE_UX_REPAIR_REPORT.md` | This report |

## 4. Backend lifecycle support status

| Operation | API / code | Semantics |
| --- | --- | --- |
| Disable | existing `/disable` + `DisableTerminalAsync` | Blocks access; status Disabled |
| Enable | **new** `/enable` + `EnableTerminalAsync` | Only if status is Disabled; sets Active; clears `DisabledAtUtc`; **refuses Revoked** |
| Remove | existing `/revoke` + `RevokeTerminalAsync` | Permanent trust removal; UI label **Remove terminal** |

Enable does **not** re-pair. Revoked terminals cannot be enabled; client must request access again.

## 5. Active terminal UX

- Status: Active
- Primary: **Disable terminal** (opens confirmation)
- Secondary: **Clear operator sessions** (ghost)
- **Remove terminal** not shown while Active

## 6. Disabled terminal UX

- Status: Disabled
- **Enable terminal** (confirmation)
- **Remove terminal** (destructive confirmation)
- Clear sessions / Hide from list remain secondary where applicable
- No Restore (not supported) control
- No Re-enable is not supported copy

## 7. Remove semantics

- Visible label: **Remove terminal**
- Under the hood: existing **revoke** permanent trust removal
- Confirmation: stronger body + Keep disabled cancel
- After remove: retired/revoked group; no Enable; may Hide from list

## 8. Confirmation guards

Local state:

- `pendingDisableTerminal`
- `pendingEnableTerminal`
- `pendingRemoveTerminal`

Uses `ConfirmDialog`. Disable and Remove do not mutate from the row button click.

## 9. Tests / gates

| Gate | Result |
| --- | --- |
| pre-i-terminal-lifecycle-ux-repair | PASS |
| v2-terminal-lifecycle | PASS |
| v2-host-lan-control-ux | PASS |
| pre-i1-lan-terminal-activation-management-beta | PASS |
| pre-beta-e-lan-client-shell-parity | PASS |
| pre-i-activation-landing-manual-qa-repair | PASS |
| web-a3 / web-a4 | PASS |
| full CoreSmoke | PASS |
| npm web build | PASS |

## 10. Manual visual check

Source and gates verified. Kirch should confirm in UI:

- Terminal Management Active row: Disable terminal only (no Remove)
- Disable opens confirm
- Disabled row: Enable terminal + Remove terminal
- Remove opens strong confirm
- No primary Revoke label
- No Restore not supported

Optional: `dotnet run --project src\Salryn.Api.Local -- --dev`

## 11. Remaining risks

- WinForms terminal registry UI not updated in this lane (Host web Terminal Management only)
- Soft hide is still localStorage-only; not Remove
- Enable restores trust without re-validating terminal license expiry (same as pre-disable pairing state)
- No interactive screenshot proof in this agent run

## 12. Final decision

**TERMINAL_LIFECYCLE_UX_REPAIR_REPAIRED_AND_PASSED**

No branch promotion. No package/I1. No cloud activation. No BIR/RMO/eSales claim.

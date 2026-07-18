# PRE-I LAN Setup UX Clarity Report

## 1. Branch / base / final SHA

- Branch: `KIRCH-PRE-I-LAN-SETUP-UX-CLARITY`
- Accepted base branch: `KIRCH-PRE-I-LICENSE-PAGE-MINIMALIST-REBUILD`
- Accepted base SHA: `c3b69fcc5a3bd04882187aeef17811b94e732e61`
- Final implementation SHA: `1e50db302ea163fab244bd3614e14e4655df8a92`
- The delivery summary records the final branch SHA after this evidence report is committed.

## 2. Problem fixed

The Terminal Management page mixed operator language with backend/runtime terminology, hid the exact terminal URL among technical details, and allowed disruptive LAN-level actions without consistent confirmation. The page now presents a direct store-operator workflow while preserving the existing LAN, pairing, terminal lifecycle, and licensing APIs.

## 3. Copy / terminology changes

- The primary task is now `Connect terminals over LAN`.
- Normal operator copy consistently uses `Host PC`, `terminal device`, `Terminal link`, `LAN access`, and `Terminal POS access`.
- The page presents a numbered setup sequence: confirm the Host PC LAN address, start LAN access, open the terminal link, pair the terminal, and obtain a Terminal license.
- Runtime, transport, client-URL, raw bind, and listener wording remains available only inside collapsed advanced diagnostics.
- Terminal lifecycle labels were aligned with the same Host PC / terminal-device vocabulary.

## 4. Terminal link visibility behavior

- With a valid IPv4 address and port, the page always shows `http://<ip>:<port>/remote-client` prominently.
- When LAN is ready, the success instruction is `Terminal devices can open this link`.
- Before readiness, the exact preview remains visible with `After starting LAN access, open this link on the terminal device`.
- `Copy terminal link` remains readiness-gated to preserve existing behavior; link visibility is not readiness-gated.

## 5. Destructive confirmations added

New confirmation guards cover:

- Stop LAN access.
- Stop Terminal POS access.
- Reject a pairing request.
- Create/rotate a Host join code.
- Repair a saved LAN address when the repair changes it.

The original API mutation payloads remain unchanged and execute only from the corresponding confirmation handlers. Start LAN access, allow Terminal POS access, accept pairing, and copy do not add unnecessary confirmation. Existing Disable terminal, Enable terminal, Remove terminal, and Clear operator sessions confirmations remain intact.

## 6. Support / license-desk collapse

- Normal copy explains that each terminal device needs its own Terminal license.
- The operator-facing instruction says to send only the Terminal Machine Code and optional name/location to the license desk.
- Store ID, Host License ID, and the larger request block are under collapsed `License desk details`.
- `LicensingOps` is not exposed in the normal Terminal Management view.

## 7. Files changed

Product source:

- `src/Salryn.App.Web/src/pages/HostLanControlPage.tsx`
- `src/Salryn.App.Web/src/utils/terminalLifecycleUx.ts`

Focused gate and gate registration:

- `tests/Salryn.Tests/PreILanSetupUxClarityChecks.cs`
- `tools/Salryn.Tools.CoreSmoke/Program.cs`

Updated source guards:

- `tests/Salryn.Tests/LanM16RcBEntryBoundaryChecks.cs`
- `tests/Salryn.Tests/LocalLicensingBetaManualHostTerminalActivationChecks.cs`
- `tests/Salryn.Tests/PreBetaLicenseCreditsPagePolishChecks.cs`
- `tests/Salryn.Tests/PreI1LanTerminalActivationManagementBetaChecks.cs`
- `tests/Salryn.Tests/PreIActivationLandingManualQaRepairChecks.cs`
- `tests/Salryn.Tests/PreICodeProvenClientSurfaceGuards.cs`
- `tests/Salryn.Tests/PreITerminalLifecycleUxRepairChecks.cs`
- `tests/Salryn.Tests/V2HostLanControlUxChecks.cs`
- `tests/Salryn.Tests/V2LanBindTruthChecks.cs`
- `tests/Salryn.Tests/V2TerminalLifecycleUxChecks.cs`

No backend route, database path, pairing protocol, license model, package/I1, installer, or promotion file was changed.

## 8. Gates run

All required gates passed on the implementation commit:

- `dotnet build Salryn.sln` - PASS, 0 warnings, 0 errors.
- `pre-i-lan-setup-ux-clarity` - PASS.
- `pre-i-terminal-lifecycle-ux-repair` - PASS.
- `pre-i-code-proven-qa-hardening` - PASS.
- `pre-i-dev-state-reset-truth` - PASS.
- `pre-i-license-page-minimalist-rebuild` - PASS.
- `pre-i1-lan-terminal-activation-management-beta` - PASS.
- Full `Salryn.Tools.CoreSmoke` - PASS.
- `npm --prefix src\Salryn.App.Web run build` - PASS.
- `git diff --check` - PASS.

The Vite build emitted only its existing advisory that one minified chunk is larger than 500 kB.

## 9. Manual QA checklist for Kirch

Automated source, build, and regression proof is complete. Authenticated visual/operator interaction was not executed in this lane. Kirch should verify:

1. Open Terminal Management.
2. Confirm the page immediately answers what to open on the other device.
3. Confirm the exact `http://192.168.x.x:5299/remote-client` terminal link is prominent.
4. Confirm normal wording uses Host PC and terminal device.
5. Confirm Start LAN access is clear.
6. Confirm Stop LAN access opens its confirmation.
7. Confirm Stop Terminal POS access opens its confirmation.
8. Confirm Reject pairing request opens its confirmation.
9. Confirm terminal Remove and Clear operator sessions confirmations still work.
10. Confirm Advanced diagnostics and License desk details start collapsed.

## 10. Remaining risks

- Final visual spacing, scanability, and confirmation interaction still require Kirch's manual QA on a configured Host PC with representative terminal state.
- The existing Vite chunk-size advisory remains; no bundle redesign was in scope.
- This lane intentionally does not prove installation/package/I1 behavior, production activation, printer/PDF behavior, BIR/eSales/RMO behavior, or branch promotion.

## 11. Final decision

`PRE_I_LAN_SETUP_UX_CLARITY_REPAIRED_AND_PASSED`

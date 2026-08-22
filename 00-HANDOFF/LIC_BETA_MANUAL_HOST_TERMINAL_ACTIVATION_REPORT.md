# LIC-BETA — Manual Host/Terminal Activation Report

**Branch:** `KIRCH-LIC-BETA-MANUAL-HOST-TERMINAL-ACTIVATION`
**Base branch:** `KIRCH-LIC-ARCH-R1-BETA-MANUAL-HOST-TERMINAL-STUDY`
**Base SHA:** `e4f7bc5af839d5c93b23594493988b2300e3c57d`
**Final SHA:** branch tip (`git rev-parse HEAD` on `KIRCH-LIC-BETA-MANUAL-HOST-TERMINAL-ACTIVATION`)
**Implementation SHA:** `10332e20aa73c76a311b5929f70630ba4d0b9e63`
**Status:** `CODE_COMPLETE_UNGATED_VERIFICATION_PARKED` (licensing gates green; full CoreSmoke blocked by pre-existing R3/Backup UI stack conflict)

**Protected refs (unchanged):**
- `KIRCH-SANDBOX` = `1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `KIRCH-INSTALLER-M1` = `6b9c46676390b8a86a989eca600cb02e406ce865`

**Official beta contact:** `salryn.info@gmail.com`
**Branch pushed:** NO

---

## PASS 1 findings

### Already existed
- Offline local license file store under `LocalApplicationData/Salryn/Licensing/license.json`
- Install/status/entitlements APIs (loopback + auth/permission)
- RSA **public** key verifier; unsigned/missing verifier fail closed
- Admin License page + nav/permissions (Settings.Manage)
- Feature list: Pos, Rmo, Reports, Users, Backup, Licensing, LanTerminals
- Passive `ApiLicenseGuard` helper (not wired to product endpoints)
- LIC-FINAL fields: ApplicationId, StoreId, AllowedLanTerminals, SupportExpiresAt, UpdatesUntil
- Backup UI copy: license outside DB package
- Architecture study + implementation plan docs

### Must change (this branch)
- Add `LicenseType` Host/Terminal and related fields
- Require MachineBinding (reject empty)
- Require ApplicationId=`Salryn.POS`, StoreId, type-specific fields
- Stronger machine code / fingerprint
- Terminal must not unlock host-only features
- Manual activation UX: machine code + salryn.info@gmail.com
- Expose LicenseType on status/entitlements
- Host LAN copy: separate terminal licenses
- Schema version **2** for beta documents
- CoreSmoke `lic-beta-manual-host-terminal-activation`

### Must not touch
- Private signing key / license generator implementation in POS
- Cloud / Vercel / payment / 16-digit activation codes
- Installer lane / protected refs
- Full LAN pairing count enforcement (next branch)
- Mass product endpoint license hard-lock (report gaps only)
- RMO/BIR/eSales/hardware
- Surface4 hardware ban

---

## Files changed

### Core / infrastructure
- `src/Salryn.Core/Services/Licensing/LicenseContracts.cs`
- `src/Salryn.Core/Services/Licensing/LocalLicenseContractPolicy.cs`
- `src/Salryn.Infrastructure/Licensing/LocalLicenseValidator.cs`
- `src/Salryn.Infrastructure/Licensing/LocalLicenseService.cs`
- `src/Salryn.Infrastructure/Licensing/LocalLicenseSignatureVerifier.cs`
- `src/Salryn.Infrastructure/Licensing/LocalMachineFingerprint.cs`
- `src/Salryn.Infrastructure/Licensing/LocalLicenseEnforcementService.cs`
- `src/Salryn.Api.Local/LocalLicenseEndpoints.cs`

### Frontend
- `src/Salryn.App.Web/src/api/license.ts`
- `src/Salryn.App.Web/src/pages/LicensePage.tsx`
- `src/Salryn.App.Web/src/pages/HostLanControlPage.tsx`

### Tests / smoke
- `tests/Salryn.Tests/LocalLicensingM1FoundationChecks.cs`
- `tests/Salryn.Tests/LocalLicensingM2SignedInstallImportChecks.cs`
- `tests/Salryn.Tests/LocalLicensingM3LocalEnforcementBoundaryChecks.cs`
- `tests/Salryn.Tests/LocalLicensingBetaManualHostTerminalActivationChecks.cs` (new)
- `tools/Salryn.Tools.CoreSmoke/Program.cs`

### Docs
- `00-HANDOFF/LIC_OPS_PRIVATE_GENERATOR_BOUNDARY.md` (new)
- `00-HANDOFF/LIC_BETA_MANUAL_HOST_TERMINAL_ACTIVATION_REPORT.md` (this file)

---

## Implementation

### Schema / types
- `LicenseType`: Host | Terminal
- Schema version **2** (`LocalLicenseContractPolicy.BetaLicenseSchemaVersion`)
- Document fields: LicenseType, HostLicenseId, TerminalId (+ existing commercial fields)
- Host requires `AllowedLanTerminals` (≥ 0); forbids HostLicenseId/TerminalId
- Terminal requires HostLicenseId + TerminalId; forbids AllowedLanTerminals
- ApplicationId must equal `Salryn.POS`; StoreId required; MachineBinding required non-empty

### Validation / enforcement
- Empty MachineBinding → Invalid
- Terminal feature filter: only `LanTerminals` enabled
- Enforcement: Terminal + host-only feature → `WrongLicenseType`
- Host with AllowedLanTerminals ≤ 0 or without LanTerminals feature: LanTerminals feature denied
- Development mode remains explicit-only
- Signing payload includes LicenseType, HostLicenseId, TerminalId

### Machine code
- Material: Windows MachineGuid (when available) + persistent installation id + machine name + platform
- Hashed SHA-256; displayed as `SALRYN-MC-XXXX-XXXX-XXXX-XXXX`
- Installation id stored under licensing root (`installation-id.txt`)
- Raw identifiers never shown in UI

### UI
- Activation required panel when missing/invalid/expired/mismatch
- Machine code + email `salryn.info@gmail.com` + checklist of what to send
- Copy: “Salryn works offline after a valid signed license is installed.”
- Host/Terminal summary fields on status cards
- Host LAN Control warning: separate Terminal license; Host license not valid for terminals

### Generator boundary
- Spec only in POS: `LIC_OPS_PRIVATE_GENERATOR_BOUNDARY.md`
- No private key, no signing tool code in POS

---

## PASS 2 findings

| Check | Result |
|-------|--------|
| Private key absent | PASS (source scan + guards) |
| Empty MachineBinding rejected | PASS (validator + beta smoke) |
| Host/Terminal type required | PASS |
| Terminal cannot unlock host POS | PASS (filter + WrongLicenseType) |
| Host not treated as terminal proof | Model prepared; pairing enforcement deferred |
| salryn.info@gmail.com present | PASS |
| Machine code visible | PASS |
| No cloud / installer / BIR / eSales claim | PASS |
| No LAN production claim | PASS (explicit non-claim) |
| Backup license outside DB | PASS (unchanged copy + B1 green) |
| Surface4 hardware ban | Untouched |

---

## License behavior

| Case | Behavior |
|------|----------|
| **Host** | Requires type Host, StoreId, ApplicationId, MachineBinding, AllowedLanTerminals; can enable host features + optional LanTerminals |
| **Terminal** | Requires HostLicenseId, TerminalId; enables only LanTerminals; Pos/Reports/etc. stripped and enforcement-denied |
| **Missing** | Unlicensed; activation UX + machine code |
| **Invalid** | Fail closed (type/fields/signature/schema/app id) |
| **Machine mismatch** | Invalid; MachineMatched=false |
| **Empty binding** | Invalid |
| **Import** | Admin paste JSON; validate then replace; path fields rejected |
| **Machine code** | Grouped hashed code for email activation |

---

## Activation UX

1. Show machine code  
2. Email salryn.info@gmail.com with store name, contact, machine code, Host vs Terminal, terminal count for Host  
3. Labs signs externally  
4. Paste JSON → offline verify  

---

## Enforcement remaining gaps (honest)

1. **`ApiLicenseGuard` still not applied** to checkout, users, reports, backup, LAN APIs — product runs without hard license lock on those surfaces.
2. **LAN pairing/count enforcement** not implemented — next branch `KIRCH-LIC-LAN-TERMINAL-ENTITLEMENT-ENFORCEMENT`.
3. **Host capacity policy** still hardcoded; not min(license, hard cap).
4. **Terminal-presented proof at pair time** not implemented.
5. **R3 CoreSmoke** fails on this stack due to **pre-existing** Backup UI vs R3 AppShell copy (“Backup & Restore UI planned”) — **not introduced by this licensing branch** (AppShell backup deferral copy removed when Backup UI was added earlier on the park stack).

---

## Gates (actually run)

| Gate | Result |
|------|--------|
| `dotnet build Salryn.sln` | **PASS** |
| licensing-m1 | **PASS** |
| licensing-m2 | **PASS** |
| licensing-m3 | **PASS** |
| lic-beta-manual-host-terminal-activation | **PASS** |
| pos-b1-backup-restore-safety | **PASS** |
| user-m1-host-only-user-creation | **PASS** |
| pos-printer1-windows-printer-wiring | **PASS** |
| pos-manual1-r3-frontend-safety-ux-repair | **FAIL** (pre-existing Backup UI / R3 AppShell copy conflict) |
| pos-surface1-role-page-lock-store-nav | **PASS** |
| full CoreSmoke | **NOT RUN** (would hit R3 failure above) |
| `npm --prefix src/Salryn.App.Web run build` | **PASS** |
| `git diff --check` | **PASS** (clean tree) |
| `git status` | **CLEAN** after implementation commits (report stamp may leave one doc file dirty until committed) |
| Private key scan | **PASS** (no matches) |
| Protected refs | **UNCHANGED** |
| Branch pushed | **NO** |

---

## Final status

`CODE_COMPLETE_UNGATED_VERIFICATION_PARKED`

Licensing beta Host/Terminal activation is code-complete with focused gates green. Full CoreSmoke remains ungated because R3 fails on pre-existing AppShell backup copy drift (Backup UI already shipped; R3 still expects “Backup & Restore UI planned”). That failure is outside this licensing branch scope.

---

## Next branch

`KIRCH-LIC-LAN-TERMINAL-ENTITLEMENT-ENFORCEMENT`

- Host terminal count vs AllowedLanTerminals
- Terminal license pairing proof
- Reject wrong store/host/machine and host-as-terminal

Then (outside POS): LicensingOps private generator implementation.

Installer remains delayed.

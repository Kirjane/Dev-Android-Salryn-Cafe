# PRE-BETA C/D/F/G/H — Stack Review and Repair Report

## 1. Branch / base / final SHA

| Item | Value |
| --- | --- |
| Branch | `KIRCH-PRE-BETA-CDFGH-STACK-REVIEW-AND-REPAIR` |
| Base | `KIRCH-PRE-BETA-H-CAPABILITY_MATRIX_SEAL` @ `4f709a5305cf3632044395e5855d00a58be15a8c` |
| Final SHA | `3c62b129bb797e7c563fd9cbf411a08d098b946f` |
| Starting stack SHA (E tip) | `541b6e5f8be42bd70bc2f1a51e5a571115af4c28` |
| Protected SANDBOX | `1f74cd8bcaca1a5df487aa61677ee9829dd7fedb` (untouched) |
| Protected INSTALLER-M1 | `6b9c46676390b8a86a989eca600cb02e406ce865` (untouched) |

## 2. Decision

**`CDFGH_STACK_REVIEW_REPAIRED_AND_PASSED`**

- **Code / product alignment:** PASS (no source defects requiring repair)
- **Report metadata / traceability:** REPAIRED (stale or missing Final SHA stamps)
- **Claim discipline:** PASS
- **Hardware caveat honesty:** PASS

## 3. Remote branch verification

`git fetch origin` then `git rev-parse origin/<branch>`:

| Lane | Branch | Expected SHA | Remote SHA | Match |
| --- | --- | --- | --- | --- |
| C | `KIRCH-PRE-BETA-C-STORE-LOGO-BRANDING` | `ffdf968d5ef143a25f9ef6e9c543b16eabd5b2ad` | `ffdf968d5ef143a25f9ef6e9c543b16eabd5b2ad` | YES |
| D | `KIRCH-PRE-BETA-D-PORTABLE-BACKUP-IMPORT-EXPORT` | `6f40eaa1b3a03e7ab7632be6e38a917b870046d1` | `6f40eaa1b3a03e7ab7632be6e38a917b870046d1` | YES |
| F | `KIRCH-PRE-BETA-F-SCANNER-FIRST-CHECKOUT` | `6ae2255feb2c9a8e54b363aecc2e0a98c6949f42` | `6ae2255feb2c9a8e54b363aecc2e0a98c6949f42` | YES |
| G | `KIRCH-PRE-BETA-G-PRINTER_HARDENING` | `b1956500e230b64bbd31bb85127f92901d07d50a` | `b1956500e230b64bbd31bb85127f92901d07d50a` | YES |
| H | `KIRCH-PRE-BETA-H-CAPABILITY_MATRIX_SEAL` | `4f709a5305cf3632044395e5855d00a58be15a8c` | `4f709a5305cf3632044395e5855d00a58be15a8c` | YES |

Stack ancestry (`git merge-base --is-ancestor`):

| Edge | Result |
| --- | --- |
| E → C | ancestor OK |
| C → D | ancestor OK |
| D → F | ancestor OK |
| F → G | ancestor OK |
| G → H | ancestor OK |

## 4. Report metadata audit

| Report | Before | After repair |
| --- | --- | --- |
| PRE_BETA_C | Final SHA stale `7ea7dcd…` | `ffdf968d…` (branch tip); note feature commit `7ca4c3d…` |
| PRE_BETA_D | `*(branch tip after push)*` | `6f40eaa1…` |
| PRE_BETA_F | `*(branch tip after push)*` | `6ae2255f…` |
| PRE_BETA_G | No Final SHA | `b1956500…` |
| PRE_BETA_H | No Final SHA; vague gates | Final SHA `4f709a53…` + gates table |

These were documentation/traceability issues only — not code defects.

## 5. Source alignment audit by lane

### Lane C — Store logo / branding — ALIGNED

- Migration v20: `logo_content_type` + `logo_bytes` on `store_settings` (DB-backed, not source-tree files)
- `SupportedMigrationVersion = 20`
- Upload: Settings.Manage; PNG/JPEG/WebP; SVG rejected; max 512 KB
- Host branding read for authenticated session; logo bytes endpoint authenticated
- LAN remote branding via session auth + `logoDataUrl` (no Host cookie for img)
- AppShell / TerminalShell consume branding
- Backup D reads logo presence from DB columns when present

### Lane D — Portable backup — ALIGNED

- Package: `.salryn-backup.zip` with `manifest.json` + `salryn.db` only
- Unexpected entries / path traversal / hash mismatch / future schema rejected
- Max import 100 MB
- Import assigns new local package id (no overwrite collision)
- Export of verified packages only
- Restore uses existing safety archive path before live DB replace
- No LicensingOps secrets / private key bundling in package service
- UI: Download / Import / Verify / Restore + RESTORE confirmation + summary (store, schema, counts, logo)

### Lane F — Scanner-first checkout — ALIGNED

- Host: Scan / search, Enter + Tab, exact match, Fast Add unknown, focus return
- LAN: Scan / search primary; `POST .../cart/add-line-by-barcode`; unknown barcode safe fail (no silent create)
- Product GUID under Advanced (not normal cashier path)
- Keyboard-wedge only; no USB HID / WebHID / Bluetooth scanner API paths in checkout sources
- Stock still only mutates on committed sale (existing checkout path; scan only adds cart lines)

### Lane G — Printer hardening — ALIGNED

- Settings: Windows print queue only; exact name helper; Test print; No ESC/POS/drawer/cutter/thermal-profile
- Adapter message: queue submit does not prove paper
- Print failure messages: sale remains committed
- Reprint: persisted snapshot; non-mutation copy
- Automated: POS-PRINTER-1 + POS-M2 wrapped by PreBetaG

### Lane H — Capability matrix — ALIGNED

- `BETA_CAPABILITY_MATRIX.md` separates SUPPORTED / CODE-PROVEN / HARDWARE-PROOF-PENDING / NOT SUPPORTED / FORBIDDEN CLAIMS
- DEPLOYMENT_LOCK: single Host DB + licensed LAN terminals; old single-workstation language superseded
- V1_BUILD_ORDER: historical; ESC/POS step obsolete/forbidden
- HARDWARE_LOCK + FEATURE_LOCK cross-refs updated
- No claim of physical scanner/printer proof as done
- No public MSI-as-done / BIR production / cloud activation as supported

## 6. Claim discipline audit

| Claim class | Status |
| --- | --- |
| Hardware-proven scanner | Not claimed (HARDWARE-PROOF-PENDING / DO LATER) |
| Hardware-proven paper print | Not claimed (queue only; paper DO LATER) |
| Thermal profile / ESC/POS / drawer / cutter | Forbidden / not supported |
| Cloud activation | Forbidden / not supported |
| Public MSI done | Forbidden / not supported |
| BIR/eSales production | Forbidden / not supported |
| LAN terminal product truth | Supported + code-proven; second-device manual recheck DO LATER |

## 7. Hardware caveat audit

DO LATER remains correctly non-blocking:

- Physical second-device LAN shell recheck
- Physical keyboard-wedge scanner proof
- Actual Windows printer paper output
- Thermal-as-Windows-printer path (if used) paper review
- Receipt paper appearance review
- Client update rehearsal on real machine
- Logo visual polish

## 8. Repairs made (this branch)

**Documentation only:**

1. Stamp Final SHA on C/D/F/G/H lane reports to match remote tips
2. Clarify C tip vs feature commit
3. Complete H gates table
4. This review report

**Source repairs:** none required.

## 9. Tests / gates run (review branch)

| Gate | Result |
| --- | --- |
| Stack ancestry E→C→D→F→G→H | PASS |
| `dotnet build Salryn.sln` | PASS |
| full CoreSmoke | PASS |
| `npm --prefix src/Salryn.App.Web run build` | PASS |
| `git diff --check` | PASS (CRLF warnings only) |

## 10. Remaining do-later caveats

See section 7. Lane I (package refresh) not authorized.

## 11. Remaining blockers

None for automated stack acceptance.

## 12. Push

Branch: `KIRCH-PRE-BETA-CDFGH-STACK-REVIEW-AND-REPAIR`  
No promotion to protected refs.

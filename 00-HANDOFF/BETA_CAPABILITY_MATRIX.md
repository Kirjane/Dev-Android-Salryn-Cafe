# Salryn POS — Beta Capability Matrix (Sealed)

**Status:** PRE-BETA-H seal after stacked lanes C/D/F/G (plus prior A/B/E).  
**Authority:** This matrix is the client/dev-facing honesty document for beta claims.  
**Owner:** Kirch remains project lead.  
**Date stamp context:** Sealed on branch `KIRCH-PRE-BETA-H-CAPABILITY_MATRIX_SEAL` after automated C→D→F→G gates.

Categories:

| Tag | Meaning |
| --- | --- |
| **SUPPORTED** | Intended beta product capability; implemented in code for Host and/or LAN as stated |
| **CODE-PROVEN** | Automated CoreSmoke / focused checks cover the behavior |
| **HARDWARE-PROOF-PENDING** | Code path exists; physical device/paper not proven in this stack |
| **NOT SUPPORTED** | Out of beta scope; do not sell or promise |
| **FORBIDDEN CLAIMS** | Must never appear in client/marketing/support copy |

---

## 1. SUPPORTED (beta product)

| Capability | Notes |
| --- | --- |
| Offline Host POS | Local SQLite Host; no cloud required for sales |
| LAN Cashier/Manager terminal | Licensed terminal, TerminalShell, host-owned checkout |
| Per-terminal license | Terminal activation / pairing model (not cloud phone-home) |
| Store logo / branding | DB-backed logo; Host shell + LAN TerminalShell (Lane C) |
| Portable backup import/export | `.salryn-backup.zip` Download / Import (Lane D) |
| Keyboard-wedge barcode checkout | Host Scan/search + LAN add-by-barcode (Lane F) |
| Windows print queue internal receipts | Disabled / named Windows printer; test print; reprint from snapshot (Lane G) |
| Local backup create/verify/restore | Admin Backup & Restore; safety archive before restore |
| Role-based Host admin surfaces | Users, products, inventory, sales history, settings, backup (Host only) |

---

## 2. CODE-PROVEN (automated)

| Area | Proof vehicle |
| --- | --- |
| Store logo migration/upload/remove | `pre-beta-c-store-logo-branding` + full CoreSmoke |
| Portable backup zip export/import/corrupt/future reject/restore | `pre-beta-d-portable-backup-import-export` + full CoreSmoke |
| Scanner-first Host + LAN source/API path | `pre-beta-f-scanner-first-checkout` + POS-M3/M4 related |
| Printer disabled/missing/fail; reprint non-mutation | `pre-beta-g-printer-hardening` wraps POS-PRINTER-1 + POS-M2 |
| LAN shell parity (no Host admin on terminal) | `pre-beta-e-lan-client-shell-parity` |
| Full regression | Full CoreSmoke + web production build |

---

## 3. HARDWARE-PROOF-PENDING

| Item | Bound |
| --- | --- |
| Physical keyboard-wedge barcode device | Code uses keyboard input only; physical recheck DO LATER |
| Physical Windows printer paper output | Queue submission may succeed without proving paper |
| Thermal printer **as a Windows printer** | Only if OS exposes it as a normal Windows queue name — **not** a Salryn thermal profile |
| Second physical LAN client device shell recheck | Code-proven; multi-device visual recheck DO LATER |
| Client update rehearsal on real client machine | Packaging lane I — not claimed done |

---

## 4. NOT SUPPORTED (beta)

| Item |
| --- |
| Cloud activation / online license phone-home |
| Public MSI “installer done” as product truth |
| BIR / eSales **production** accreditation |
| Official receipt accreditation / BIR-approved receipts |
| ESC/POS raw protocols |
| Cash drawer / drawer kick |
| Receipt cutter / cut command |
| Hardware bundles / “POS kit certified” |
| Direct USB HID scanner API |
| Bluetooth scanner special handling |
| Universal scanner SDK / device enumeration |
| Multi-store / cloud DB / branch sync |
| Manager remote Host admin pages inventing Host capabilities |
| Thermal-specific formatting profiles inside Salryn |

---

## 5. FORBIDDEN CLAIMS

Never claim in beta materials:

- Cloud activation
- Public MSI-as-done
- BIR/eSales production readiness
- Official receipt accreditation
- ESC/POS supported
- Cash drawer supported
- Drawer kick
- Cutter supported
- Hardware bundle supported
- Hardware-proven scanner (unless physical test documented)
- Hardware-proven paper print (unless physical test documented)
- Thermal profile support (beyond optional Windows-queue name path, bounded above)
- Direct USB HID scanner API
- Bluetooth scanner SDK
- Universal scanner SDK

---

## 6. Deployment model clarification

**Single Host database, multi-terminal access:**

- **One** Windows Host PC runs Salryn Host + local SQLite.
- **Zero or more** licensed LAN Cashier/Manager terminals connect to that Host.
- Terminals do **not** hold the store database.
- This supersedes older “single cashier workstation only” wording that predated LAN terminal shell.

**Still true:**

- No multi-master sync, no cloud DB, no branch replication.
- Printer and scanner proofs remain host-local hardware concerns.

See also: `09-DEPLOYMENT/DEPLOYMENT_LOCK.md` (clarified), `06-HARDWARE/HARDWARE_LOCK.md`.

---

## 7. Stack lane map (this readiness series)

| Lane | Topic | Decision |
| --- | --- | --- |
| A | LicensingOps client-friendly issuance | PASSED (LicensingOps repo) |
| B | POS activation copy cleanup | FULLY_VERIFIED |
| E | LAN client shell parity | PASSED |
| C | Store logo / branding | REPAIRED_AND_PASSED |
| D | Portable backup import/export | PASSED |
| F | Scanner-first checkout | PASSED |
| G | Printer hardening | PASSED |
| H | Capability matrix seal | SEALED (this doc) |
| I | Package refresh | **Not authorized** in this stack |

---

## 8. DO LATER / MANUAL PROOF BACKLOG

- Physical second-device LAN shell recheck
- Actual keyboard-wedge scanner hardware proof
- Actual Windows printer paper proof
- Actual thermal-as-Windows-printer proof
- Receipt paper appearance review
- Client update rehearsal on real client machine
- Logo visual/aesthetic polish
- Package/I1 unless Kirch authorizes

---

*End of sealed beta capability matrix.*

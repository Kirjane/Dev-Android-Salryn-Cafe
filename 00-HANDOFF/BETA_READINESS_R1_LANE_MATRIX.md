# BETA READINESS R1 — Lane Matrix

Base studied:

- POS: `KIRCH-I1-PACKAGE-INSTALL-PROOF` @ `9483a45ecc933abd5869a09a46e840cc160d6bf6`
- LicensingOps: `KIRCH-LICOPS-ISSUANCE-CONTRACT-HARDENING` @ `10be2b3e03604ffb32f5e312f6569c38eacf486c`

Protected refs (unchanged):

- `KIRCH-SANDBOX` = `1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `KIRCH-INSTALLER-M1` = `6b9c46676390b8a86a989eca600cb02e406ce865`

Companion: `00-HANDOFF/BETA_READINESS_R1_MASTER_PLAN.md`

---

## Master lane table

| Lane | Goal | Repo(s) | Depends On | Blocks | Automated Proof | Manual Proof | Hardware Needed | Package Rerun | Decision Gate |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| **A** LicensingOps client-friendly issuance | Store records, auto StoreId, Terminal-from-Host paste/select, auto TerminalId, request copy helpers | **LicensingOps** (primary); POS secondary copy only if needed | LicOps issuance-hardened base | Clean multi-client issuance; Lane B templates | `npm test` / self-test; issue host→terminal derive assertions | Loopback admin walkthrough: create store → host → terminal | No | No (unless POS touched) | Store not invented; Terminal fields derived; flat JSON only; no key leak |
| **B** POS activation copy cleanup | Host/Terminal request copy; no HostLicenseId ask for terminal users; Admin surfaces IDs | **POS** | A preferred | Correct client/dev activation persona | CoreSmoke source guards on activation pages | Host License page + Terminal Activation + Terminal Management copy check | No | Optional | Terminal never requires HostLicenseId knowledge; contact remains salryn.info@gmail.com |
| **C** Store branding / logo | DB-backed logo upload; Host+LAN display; backup-aware | **POS** | Store settings stack; optional after E start | Branded shell completeness | Migration + API + CoreSmoke | Upload/preview/remove Host; LAN shows logo | No | Yes if packaged branding claimed | Logo persists restart; LAN fetch works; no BIR logo claim |
| **D** Portable backup import/export | Download Backup File; Import Backup File; verify; restore confirm | **POS** | Existing backup package safety | Portable update fallback story | Extend `pos-b1-backup-restore-safety` + related | Download→import→verify→type RESTORE→login | No | **Yes** | Import never auto-restores; pre-restore archive; licenses/secrets boundaries documented |
| **E** LAN client shell parity | Host-like shell; Cashier/Manager role nav; hide Remote Read-Only; terminal badge | **POS** | PRE-I1 LAN backend (done); B recommended | Beta LAN product feel; LAN scanner surface | `lan-m16-remote-pos`, PRE-I1 LAN targets, nav guards | Second-device shell: Cashier/Manager only; Admin blocked | Preferred LAN second device after UI change | Yes (web assets) | Feels like Salryn POS not diagnostic remote app |
| **F** Checkout scanner UX | Scanner-first Host polish + LAN parity; wedge only | **POS** | Host checkout (largely done); E for LAN UI | Scanner-first beta claim | POS-M3/M4 + new LAN scan checks | Keyboard-wedge sim Host (+ LAN) | **Yes** before marketing scanner | Optional | Enter/Tab; focus return; unknown path; no HID/BT claims |
| **G** Printer hardening | Windows queue honesty; test print; fail/reprint safety | **POS** | Printer1 wiring (done) | Paper receipt claim | Printer1 CoreSmoke | Test print; disabled/missing/fail; reprint | **Yes** before paper claim | Optional | Sale not rolled back; no duplicate stock/sale on reprint; no ESC/POS |
| **H** Beta capability matrix | Client/dev supported vs not-supported seal | **POS docs** (+ LicOps playbook link) | Product decisions A–G (draft now) | Marketing/support truth | Doc review only | Kirch acceptance of matrix | No | No | Matrix matches code + locks; forbidden claims listed |
| **I** Beta package refresh | Re-publish package after beta lanes; full proof | **POS** (+ LicOps for test licenses only) | Implemented beta lanes | “Current beta package” claim | Build, focused gates, full CoreSmoke, web build, secret scan | Packaged Host activation + LAN routes + new features | As needed for claims | **Is** package proof | `I1`-style decision gate re-run; protected refs untouched |

---

## Suggested execution order

1. **A** → 2. **B** → 3. **E** → 4. **C** → 5. **D** → 6. **F** → 7. **G** → 8. **H seal** → 9. **I**

Parallel docs:

- Draft **H** anytime.
- **Locks refresh** (docs-only: DEPLOYMENT_LOCK / V1_BUILD_ORDER / FEATURE_LOCK LAN + hardware alignment) as early as possible to prevent stale-claim risk.

---

## Minimum beta cutline (if Kirch needs a shorter path)

| Must ship before real beta clients | Can slip post-beta |
| --- | --- |
| A + B | Expanded Manager remote pages |
| E (LAN shell parity) | Public MSI / auto-updater |
| D (portable backup) if update story is sold | Logo polish (C) if branding not promised |
| F Host scanner (already largely present) + sim tests | LAN scanner parity if LAN is Host-checkout-only temporarily |
| G honesty paths; hardware only if paper claimed | ESC/POS, drawer, cutter (never in beta) |
| H matrix | Cloud activation, BIR production |
| I package refresh after the above | Installer promotion of protected refs |

---

## Already proven (do not rebuild)

| Item | Status |
| --- | --- |
| Host offline licensing | Proven |
| LicensingOps flat JSON contract | Proven |
| PRE-I1 LAN security/roles | Proven + Kirch physical LAN |
| I1 package launch/dist/secret scan | Proven @ `9483a45` |
| Host Terminal Management backend | Proven |
| Windows printer code path | Present (hardware optional) |
| Host wedge scan + Fast Add code | Present (hardware optional) |

---

## Forbidden claims (matrix reminder)

- Cloud activation
- Public installer as finished product
- BIR/RMO/eSales production accreditation
- ESC/POS / cutter / cash drawer / thermal profile / hardware bundle
- USB HID / Bluetooth scanner stacks
- That package output is MSI-equivalent
- That physical scanner/printer work without Kirch attestation

---

## Decision

Planning lane decision: **`BETA_READINESS_PLAN_COMPLETE_WITH_BLOCKERS`**

Recommended first coding lane (after Kirch authorizes): **Lane A — LicensingOps client-friendly issuance**.

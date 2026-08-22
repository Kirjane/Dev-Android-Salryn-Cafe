# BETA READINESS R1 — Master Plan

Status: **Planning / repo-truth lane only** (no product feature code in this branch).

Decision: **`BETA_READINESS_PLAN_COMPLETE_WITH_BLOCKERS`**

---

## 1. Executive summary

Salryn is past core offline Host licensing, LicensingOps flat-JSON issuance contract, PRE-I1 LAN terminal security/roles (code + Kirch physical second-device proof), and I1 package/install proof. It is **not** yet client-friendly beta-ready.

The remaining work is mostly **operator UX**, **LAN product shell parity**, **portable backup**, **branding**, and **honest hardware claims** — not a rebuild of the POS engine.

### What is solid

| Area | Status |
| --- | --- |
| Host offline signed licenses | Proven (code + package path) |
| LicensingOps flat signed JSON (no wrapper paste) | Proven |
| Fresh Host → bootstrap → login | Proven |
| License persistence | Proven |
| LAN terminal security/roles/Admin host-local | Code + CoreSmoke + Kirch physical proof |
| I1 package launches without Vite | Proven |
| Windows print queue path (code) | Present; physical printer proof incomplete |
| Host checkout keyboard-wedge scan + Fast Add | Code-present; physical scanner proof incomplete |

### What blocks “real beta” feel

1. **LicensingOps forces invented StoreId / HostLicenseId / TerminalId** — functional, not operator-friendly.
2. **Terminal activation copy asks end-users for Host License ID** — clients do not naturally know it.
3. **LAN client feels like a separate Remote Client app**, not Host-like role-restricted POS.
4. **No store logo** in settings model/API/UI/DB.
5. **Backup is host-local only** — no Download Backup File / Import Backup File.
6. **LAN checkout lacks Host-parity scan/search** (Remote POS has no barcode scan field).
7. **Stale planning locks** still describe single-workstation / ESC/POS-era assumptions that contradict current product truth.

### Recommended first implementation lane (after this plan is accepted)

**LANE A — LicensingOps client-friendly issuance**  
(Store records + auto StoreId + issue Terminal from Host license paste/select.)

Then **LANE B** (POS activation copy + Host Admin surfaces for IDs), then **LANE E** (LAN shell parity), then D/C/F/G as sequenced below, then **H** matrix seal + **I** package refresh.

---

## 2. Current branch / SHA truth

### POS

| Item | Value |
| --- | --- |
| Study base branch | `KIRCH-I1-PACKAGE-INSTALL-PROOF` |
| Study base SHA | `9483a45ecc933abd5869a09a46e840cc160d6bf6` |
| I1 decision | `I1_PACKAGE_INSTALL_PROOF_REPAIRED_AND_PASSED` |
| Lineage | PRE-I1 LAN terminal beta `7ea411bed3925780f17cae19ccaa0c03bbcb3837` |
| This planning branch | `KIRCH-BETA-READINESS-R1-MASTER-PLAN` |
| Protected `KIRCH-SANDBOX` | `1f74cd8bcaca1a5df487aa61677ee9829dd7fedb` (**must not change**) |
| Protected `KIRCH-INSTALLER-M1` | `6b9c46676390b8a86a989eca600cb02e406ce865` (**must not change**) |

### LicensingOps

| Item | Value |
| --- | --- |
| Branch | `KIRCH-LICOPS-ISSUANCE-CONTRACT-HARDENING` |
| SHA | `10be2b3e03604ffb32f5e312f6569c38eacf486c` |
| Status | `LICOPS_ISSUANCE_CONTRACT_HARDENED_AND_REVIEWED` |
| Product code changes in this planning lane | **None** |

---

## 3. Current accepted proof

### Fully proven (code + automated and/or package + Kirch attestation)

| Capability | Evidence class |
| --- | --- |
| Host offline license install/verify/persist | CoreSmoke + package I1 |
| LicensingOps Host/Terminal issue with flat `licenseJson` stdout/copy | LicOps self-test + playbook + I1 package install |
| POS rejects issuance wrappers | Closeout/issuance contract proof |
| Fresh Host activation first → bootstrap → Admin login | PRE-I1 + I1 package |
| Client Host surface cleanup (Activate Salryn, reduced nav noise) | PRE-I1 polish/cleanup |
| Terminal license required for LAN activation; wrong type/machine/store/host rejected | PRE-I1 LAN + CoreSmoke |
| Cashier/Manager only on Remote POS; Admin/Owner host-local | PRE-I1 report + CoreSmoke |
| Physical second-device LAN | **User-attested by Kirch** on PRE-I1 base |
| I1 package: no Vite, package `dist`, health, web-dist, Host flow, secret scan | I1 report @ `9483a45` |
| Host Terminal Management API surface | PRE-I1 + I1 `/api/lan-control/*` |

### Code-proven / smoke-proven; not hardware-proven

| Capability | Gap |
| --- | --- |
| Host keyboard-wedge scan + unknown barcode Fast Add | Physical scanner not recorded as Kirch hardware pass |
| Windows print queue internal receipt + reprint-from-snapshot | Physical printer pass incomplete / not I1 package-proven on paper |
| Tab-suffix scanner behavior | Host Enter handled; full Tab-suffix + focus-return not sealed as beta UX |
| Remote POS product search/cart | Exists as remote cart workflow; **no Host-parity scan field** |

### Design intent only (not product-ready)

| Intent | Reality |
| --- | --- |
| Store logo on Host + LAN | No logo fields in StoreSettings type/API/UI/DB (grep-clean) |
| Portable backup download/import | Frontend only list/create/verify/restore-by-id |
| LicensingOps Store record catalog | Ledger is append-only license rows; no first-class Store entity UI |
| Terminal-from-Host paste flow | Terminal form retypes Customer/StoreId/HostLicenseId/TerminalId |
| LAN “same shell as Host” | Separate `RemoteClientApp` + landing + remote pages |
| Public installer / MSI | Explicitly not I1; protected installer ref not product base |

---

## 4. Current non-beta-ready gaps

1. **Issuance UX** — manual StoreId invention; manual HostLicenseId; manual TerminalId.
2. **Activation copy** — terminal instructions require Host License ID from end-user.
3. **LAN product shell** — diagnostic/remote entry tone; Remote Read-Only still reachable from landing.
4. **LAN checkout scanner parity** — missing on `RemotePosClientPage`.
5. **Branding** — no logo upload/persistence/fetch.
6. **Portable backup** — no file download/import endpoints or UI.
7. **Capability matrix** — not client-facing sealed for beta support boundaries.
8. **Package refresh** — I1 is baseline; any of C/D/E/F/G need **I1 rerun** before calling package current.
9. **Planning doc drift** — see section 5.3.

---

## 5. Repo-truth evidence map

### 5.1 Machine codes (verified)

| Source | Behavior |
| --- | --- |
| Host | `LocalMachineFingerprint` → `SALRYN-MC-…` used as MachineBinding |
| Terminal | Browser origin localStorage via `getOrCreateTerminalMachineCode()` in `terminalLicenseStorage.ts` |
| LicensingOps | `machineCode.ts` **normalizes/validates only** — does **not** generate POS machine codes |

### 5.2 LicensingOps issuance (verified)

| Form | Fields today |
| --- | --- |
| Host | Customer, **StoreId (typed)**, Host machine code |
| Terminal | Customer, **StoreId (typed)**, **HostLicenseId (typed)**, **TerminalId (typed)**, Terminal machine code |
| Output | Flat signed license JSON only (issuance contract hardened) |
| Ledger | `data/ledger.json` append-only license rows — not a Store master record |
| Host commercial model | No terminal count on Host form; `AllowedLanTerminals` forced `0` for schema |

### 5.3 Store settings / branding (verified)

Present: store name, business name, address, contact, TIN, currency, receipt header/footer, receipt width, printer mode, Windows printer name.

**Absent:** logo / branding blob / logo content-type / logo URL endpoint.

### 5.4 Backup (verified)

Web API client (`api/backups.ts`):

- `GET/POST /api/backups`
- `POST /api/backups/{id}/verify`
- `POST /api/backups/{id}/restore`

**Absent:** download stream, multipart import/upload, portable “choose file” restore.

### 5.5 LAN UI (verified)

- Host: `ConsoleApp` + `AppShell` + full nav (permission-filtered).
- Remote: `isRemoteClientBrowserLocation()` → `RemoteClientApp` → Activation → Landing → Remote POS **or** Remote Read-Only.
- Terminal activation copy: *“Include your store name, **Host License ID**, and terminal name…”* (`TerminalActivationPage.tsx`).
- Host Terminal Management page does **not** surface Host License ID / Store ID as a first-class copy panel (gap for admin/dev request flow).

### 5.6 Scanner / checkout (verified)

- Host `CheckoutPage`: Scan/search field, Enter → exact barcode match or Fast Add dialog; keyboard-wedge honesty text.
- Remote POS client: **no scan/barcode handlers** (source search empty).
- Reports: `POS_M3_BARCODE_SCANNER_CASHIER_FLOW_REPORT.md`, `POS_M4_FAST_ADD_UNKNOWN_BARCODE_INVENTORY_REPORT.md` — code lanes; **physical scanner not sealed**.

### 5.7 Printer (verified)

- Windows print queue adapter + settings fields exist (`POS_PRINTER1_WINDOWS_PRINTER_WIRING_REPORT.md`).
- `HARDWARE_LOCK.md` bans ESC/POS, cutter, drawer, thermal profiles for beta.
- Physical printer proof still a manual/hardware gate.

### 5.8 Stale locks / contradictions (blockers for blind planning)

| Doc | Stale claim | Current product truth |
| --- | --- | --- |
| `09-DEPLOYMENT/DEPLOYMENT_LOCK.md` | “One cashier/operator workstation”; multi-terminal production sync out of scope framing | LAN multi-terminal **beta path exists** and is Kirch-proven |
| `00-HANDOFF/V1_BUILD_ORDER.md` | Step 22 “ESC/POS printer”; “planning lock, no app code yet” | App is far past skeleton; **ESC/POS banned** by Hardware Lock |
| `01-FEATURE-LOCK/FEATURE_LOCK.md` | Store profile without logo; no LAN terminal product model | LAN + per-terminal licenses are product reality |
| `05-UI-UX/UI_LOCK.md` | WinForms-era primary nav list | React Host shell is primary client surface |

**Action:** treat HARDWARE_LOCK + PRE-I1/I1 reports + this master plan as **beta authority**; schedule a later **locks refresh doc lane** (docs-only) so deployment/feature locks stop contradicting LAN beta.

---

## 6. LicensingOps issuance problem

### Operator pain (today)

1. Invents **StoreId** strings (no Store master).
2. Re-types **CustomerName** on Terminal form even when Host already issued.
3. Must know/paste **HostLicenseId** from Host license metadata (easy to mistype).
4. Must invent **TerminalId** (`POS-1` style) without guidance.
5. Terminal user is told to send Host License ID by email — wrong persona.

### What is already good

- Flat JSON only for POS paste.
- Host does not collect terminal count.
- Machine codes come from Salryn, not Ops inventing bindings.
- Private key stays out of POS and out of package.

---

## 7. Correct LicensingOps model (target)

### First-class Store record

- Create Store: display name, customer/owner contact notes.
- System generates **StoreId** (stable, unique, non-user-invented).
- Host licenses issue **from** a Store record.
- Ledger links Host licenses and Terminal licenses to StoreId.

### Host issuance

Inputs:

- Store (selected or newly created)
- Host machine code (from Salryn Host License page)
- Optional support/updates dates

Outputs:

- Flat Host license JSON
- Metadata: LicenseId, StoreId, CustomerName (display)

### Terminal issuance

Inputs:

- **Host license paste** or **select prior Host ledger row**
- Terminal machine code (from Salryn Terminal Activation)
- Optional TerminalId (auto-suggest `T1`, `T2`, … per store if free)

Derived (not retyped):

- CustomerName
- StoreId
- HostLicenseId

UI must state:

> Salryn POS generates machine codes. LicensingOps only signs licenses for those codes.

### Out of Ops scope

- Cloud activation
- Billing/subscription
- POS private verification key rotation UI (separate security lane)
- Generating Host machine codes

---

## 8. POS activation copy problem

### Host (`LicensePage`)

Has machine code copy + contact email + license status/ID when active.

Gaps for beta ops:

- No single **Copy Host License Request** block (store name fields + machine code + contact template).
- Active Host License ID is shown but not framed as the admin source of truth for Terminal issuance.

### Terminal (`TerminalActivationPage`)

Asks terminal operator to include **Host License ID** in the email.

**Wrong persona.** Terminal should send:

- Terminal machine code
- Store name / terminal label
- Contact

Host Admin / LicensingOps own HostLicenseId + StoreId.

### Host Terminal Management

Should expose (Admin-only):

- Host License ID
- Store ID
- Copy Terminal License Request template for a selected terminal row (optional later)

---

## 9. Store logo / branding plan

### Goal

Logo persists on Host and appears on LAN shells.

### Recommended design (repo-aligned)

| Concern | Recommendation |
| --- | --- |
| Storage | DB-backed blob or controlled path under ProgramData with DB metadata row (prefer DB or ProgramData+hash so backup packages capture it) |
| API | `GET /api/store/branding` (public-enough for Host+LAN after session/pairing rules), `PUT` multipart Admin-only upload, `DELETE` remove |
| Validation | image/png, image/jpeg, image/webp; size cap (e.g. ≤ 512KB); max dimensions |
| UI | Store Settings upload/preview/remove |
| Host shell | AppShell header uses logo when present |
| LAN shell | Same branding endpoint after terminal session/pairing |
| Backup | Include logo in package automatically if DB/path is inside backup roots |
| Claims | Decorative store branding only — **not** BIR/official logo accreditation |

### Needs

- DB migration (or schema extension)
- API + web UI
- CoreSmoke source/runtime guards
- Backup compatibility review (Lane D coordination)

### Depends on

Nothing hard — can follow A/B. Should complete before or with Lane E if shell parity includes logo.

---

## 10. Backup import/export plan

### Goal

Portable update fallback: download backup file; import backup file; verify; confirm; restore.

### Current base to extend

Local package create/list/verify/restore + migration compatibility checks already exist.

### Add

| Capability | Notes |
| --- | --- |
| Download Backup File | Stream known backup package by id (Admin, Backup.Manage) |
| Import Backup File | Upload into backup store; never auto-restore |
| Verify imported | Existing verify pipeline |
| Summary before restore | Show schema version, counts, restorable flags |
| Type RESTORE | Keep strong confirmation |
| Pre-restore archive | Existing archive-before-restore behavior must remain |
| License file | Host license stays outside DB path (`LocalApplicationData\…\license.json`) — document explicitly |
| Terminal browser licenses | **Must not** be expected inside Host backup packages |
| Secrets | Never include LicensingOps `.secrets` or private keys |

### Package rerun

Yes — Lane I after D lands.

---

## 11. LAN shell parity plan

### Goal

LAN workstation feels like **Salryn POS**, not a diagnostic remote tool.

### Target behavior

| Concern | Target |
| --- | --- |
| Visual language | Same tokens/components as Host (`AppShell` family) |
| After activation + pairing + login | Host-like shell with role-filtered nav |
| Cashier | Checkout-focused remote-safe surface (shift/cart/pay) |
| Manager | Same safe Remote POS surface unless a later remote-safe Manager page is explicitly approved |
| Admin/Owner | Blocked at login (already) |
| Hidden from beta | Remote Read-Only debug entry (remove or internal-flag only) |
| Badge | Clear “Terminal · Cashier/Manager” mode badge |
| Branding | Store name + logo when available |

### Backend

Remote POS authorization matrix already exists — **do not widen** Host admin APIs to LAN.

### Depends on

- PRE-I1 LAN backend (done)
- Optional Lane C for logo
- Lane B for cleaner activation entry

### Risk

Rewriting navigation poorly could re-expose Host-only pages. Gate with CoreSmoke role/nav source checks + manual LAN proof.

---

## 12. Scanner support plan

### Allowed claim (HARDWARE_LOCK)

Keyboard-wedge only: focused field + Enter (and ideally Tab) suffix.

### Host status

Mostly implemented in `CheckoutPage` (scan/search, Enter, exact match, Fast Add).

### Gaps for beta

| Gap | Notes |
| --- | --- |
| Focus return after add | Confirm UX always returns caret to scan field |
| Tab suffix | Confirm/implement parity with Enter if scanners send Tab |
| LAN parity | Port scan/search + known barcode add to Remote POS; Fast Add may remain Host-only if product write is Host-only |
| Automated | Keyboard simulation in CoreSmoke/UI source guards |
| Hardware | Kirch keyboard-wedge pass on Host (and LAN if available) |

### Forbidden claims

USB HID scanner API, Bluetooth special handling, driver install, device enumeration UI.

---

## 13. Printer support plan

### Allowed claim

Windows print queue **internal** receipts only.

### Host status

Windows printer name + mode settings + queue adapter + print/reprint from snapshot exist in code.

### Beta hardening checklist

- Printer selector or exact-name helper honesty
- Test print
- Disabled / missing / failure paths (sale remains; snapshot remains)
- Reprint no duplicate sale/payment/stock
- Package still loads print settings
- Physical printer proof when hardware exists

### Forbidden claims

ESC/POS, cutter, cash drawer, thermal profile, hardware bundle, official/BIR output.

---

## 14. Hardware boundary (locked)

From `06-HARDWARE/HARDWARE_LOCK.md` (authority for beta hardware claims):

**In:**

- Windows print queue internal receipt printing
- Keyboard-wedge barcode scanner mode

**Out:**

- ESC/POS, thermal profiles, cutter, cash drawer/kick, hardware bundles
- Direct USB HID / Bluetooth scanner special stacks
- Official BIR/eSales paper claims

---

## 15. Beta capability matrix (draft — seal in Lane H)

### Supported (target beta claims)

- Host POS (local)
- Offline Host license activation
- Per-device Terminal license for LAN terminals
- LAN Cashier / Manager Remote POS (role-restricted)
- Local backup create/verify/restore (portable download/import after Lane D)
- Keyboard-wedge barcode scan on Host (LAN after Lane F)
- Windows print queue internal receipts (after physical proof if paper claim is made)
- Store identity text settings (logo after Lane C)

### Not supported / must not market

- Cloud activation / SaaS billing
- Public MSI auto-update (I1 package folder ≠ installer)
- ESC/POS / drawer / cutter / thermal bundle
- Universal scanner SDK / HID / Bluetooth device stack
- BIR/RMO/eSales production accreditation / official invoice
- Multi-store cloud sync / mobile POS / accounting suite

---

## 16. Full lane order (validated)

| Order | Lane | Rationale |
| --- | --- | --- |
| **1** | **A — LicensingOps client-friendly issuance** | Unblocks real multi-client issuance without invented IDs |
| **2** | **B — POS activation copy cleanup** | Aligns Host/Terminal copy with A; removes Host License ID from terminal persona |
| **3** | **E — LAN client shell parity** | Kirch product decision #1; backend ready; biggest “feels beta” jump |
| **4** | **C — Store logo/branding** | Completes shell brand; needs migration; feed E if not done earlier |
| **5** | **D — Portable backup import/export** | Ops/update fallback; not daily cashier path |
| **6** | **F — Checkout scanner UX parity** | Host largely done; LAN parity + polish + sim tests |
| **7** | **G — Printer hardening** | Code present; honesty + physical proof |
| **8** | **H — Capability matrix / support boundary** | Can draft early; seal after A–G decisions |
| **9** | **I — Beta package refresh** | Final gate after product lanes |

### Parallelism (safe)

- **H draft** can start immediately (docs-only).
- **Locks refresh** (docs-only) should run early to kill stale DEPLOYMENT/V1 ESC/POS language.
- **C** and **D** can parallel after A if staffing allows; both need careful package/backup interaction.
- **F** Host polish can parallel E; LAN scanner depends on E surfaces.

### Do **not** reorder to put I before product lanes

Package proof without A/B/E still leaves beta UX blocked.

---

## 17. Lane dependencies

```text
                    [PRE-I1 LAN + I1 package BASE]
                              |
              +---------------+----------------+
              |                                |
         [A LicOps]  -------------------->  [B POS copy]
              |                                |
              +---------------+----------------+
                              |
                         [E LAN shell]
                         /     |      \
                       [C]    [F]     [D]
                        \      |      /
                         \     |     /
                          [G printer]
                              |
                         [H matrix seal]
                              |
                         [I package refresh]
```

| Lane | Depends on | Blocks |
| --- | --- | --- |
| A | LicOps hardened base | Clean multi-store beta issuance; B request templates |
| B | A preferred (request field names) | Terminal user clarity; Host admin request flow |
| C | Store settings stack | Shell logo polish; optional E branding |
| D | Backup package stack | Portable update story; I package backup proof |
| E | PRE-I1 LAN | Beta LAN product feel; F LAN scanner surface |
| F | Host checkout (done); E for LAN | Scanner-first beta claim on LAN |
| G | Printer1 wiring (done) | Honest paper-output claim |
| H | Product decisions from A–G | Marketing/support truth |
| I | A–G (as implemented) | “Beta package” claim |

---

## 18. Per-lane repo ownership

| Lane | Repo ownership |
| --- | --- |
| A | **LicensingOps primary**; POS only if activation request JSON shape needs tiny copy alignment |
| B | **POS primary**; LicOps docs/playbook secondary |
| C | **POS only** |
| D | **POS only** |
| E | **POS only** |
| F | **POS only** |
| G | **POS only** |
| H | **POS docs** (+ LicOps playbook cross-link) |
| I | **POS** (package script); LicOps for test license issuance only |

---

## 19. Per-lane proof requirements

| Lane | Automated | Manual | Hardware | Package rerun |
| --- | --- | --- | --- | --- |
| A | LicOps `npm test` / self-test; issue host→terminal derive fields | Ops UI walkthrough 127.0.0.1:8787 | No | No (unless POS touched) |
| B | CoreSmoke source guards for copy/activation | Host+Terminal activation screenshots | No | Optional |
| C | Migration + API + CoreSmoke | Upload/preview Host+LAN | No | Yes if claiming packaged branding |
| D | Backup safety CoreSmoke expanded | Download→import→verify→restore | No | **Yes** |
| E | LAN-M16 + PRE-I1 targets + nav guards | Second device shell feel | Preferred (LAN device) | Yes for shell assets |
| F | POS-M3/M4 + new LAN scan guards | Keyboard-wedge sim | **Yes** for marketing scan claim | Optional |
| G | Printer1 CoreSmoke | Test print / fail paths | **Yes** for paper claim | Optional |
| H | Doc-only | Kirch review | No | No |
| I | Full CoreSmoke + web build + secret scan | Packaged Host+LAN routes | As needed | **Is** the package proof |

---

## 20. Risks / blockers

### Planning / truth blockers

1. **Stale DEPLOYMENT_LOCK / V1_BUILD_ORDER** still imply single-workstation / ESC/POS paths — risk of wrong beta claims if someone reads old locks only.
2. **Hardware proofs incomplete** for scanner and printer — code ≠ paper/scan marketing.

### Product blockers before “real beta”

3. **LicensingOps issuance UX** (Lane A) — highest operator friction for every new client.
4. **Terminal Host License ID persona bug** (Lane B).
5. **LAN shell not productized** (Lane E).
6. **No portable backup** (Lane D) — weak update fallback story.
7. **No logo** (Lane C) — polish, not security.

### Sequencing risks

8. Doing Lane E without B leaves confusing activation still attached to pretty shell.
9. Doing Lane I too early freezes package evidence before UX lanes land.
10. Logo in DB without backup inclusion creates restore holes.
11. Expanding Manager remote pages without remote-safe API review reopens Host admin risk.

### Explicit non-blockers (do not derail beta)

- Public MSI installer (post-beta / separate protected lane).
- Cloud activation.
- BIR/RMO/eSales production accreditation.
- ESC/POS / drawer / cutter.
- Universal scanner SDK.

---

## 21. What not to claim

Never claim for this beta cycle:

- Public installer / auto-updater shipped
- Cloud activation / online license phone-home
- BIR-accredited / official eSales production
- ESC/POS thermal, cutter, cash drawer, hardware bundle support
- Universal HID/Bluetooth scanner support
- Multi-store cloud sync / mobile POS
- That DEPLOYMENT_LOCK “single workstation only” is still product truth (LAN is real)
- That I1 package is a consumer installer
- That physical scanner/printer are proven without Kirch hardware attestation

---

## 22. Recommended next first lane

### **LANE A — LicensingOps Client-Friendly Issuance**

**Why first**

- Unblocks every multi-terminal client setup.
- Removes invented StoreId / HostLicenseId typing errors.
- Aligns with Kirch decisions #3 and #4.
- Isolated mostly to LicensingOps (lower POS regression risk).
- POS Lane B can then wire request copy to the real Ops model.

**Exit gate (A)**

- Create Store → auto StoreId
- Issue Host from Store + machine code
- Issue Terminal from Host paste/select → derived StoreId/Customer/HostLicenseId
- Auto TerminalId suggestion
- Copy Host/Terminal activation request helpers
- Explicit UI: Salryn generates machine codes; Ops signs
- `npm test` green; no private key commit; ledger remains gitignored

---

## 23. Final recommendation

1. **Accept this plan** as beta readiness authority over stale V1 build-order / single-workstation deployment language.
2. **Implement Lane A next** in LicensingOps (new branch from `10be2b3`).
3. Then **B → E**, with **C** as soon as shell branding is needed, **D** before any “portable update” marketing, **F/G** before scanner/printer claims, **H** seal, **I** package refresh.
4. Keep **protected refs frozen**.
5. Keep **I1 package method** (`Publish-I1Package.ps1` + package-aware web dist) as the package baseline for Lane I.
6. Treat Kirch physical LAN proof as accepted; re-run second-device only when Lane E changes client shell meaningfully.

### Final decision

**`BETA_READINESS_PLAN_COMPLETE_WITH_BLOCKERS`**

Blockers are **sequencing / UX / hardware-proof / stale-lock** blockers — not “cannot plan.” Implementation is ready to start at Lane A after Kirch authorizes coding.

---

## Appendix A — Reports studied

### POS (present)

- `I1_PACKAGE_INSTALL_PROOF_REPORT.md`
- `PRE_I1_LAN_TERMINAL_ACTIVATION_MANAGEMENT_BETA_REPORT.md`
- `PRE_I1_CLIENT_SURFACE_LAN_LICENSE_CLEANUP_REPORT.md`
- `PRE_I1_LICENSE_PAGE_PRODUCT_UX_POLISH_REPORT.md`
- `PRE_I1_FRESH_LICENSE_FIRST_FLOW_REPORT.md`
- `POS_M6_STOCK_IN_STOCK_ADJUSTMENT_REPORT.md`
- `POS_PRINTER1_WINDOWS_PRINTER_WIRING_REPORT.md` (name differs from prompt)
- `POS_M3_BARCODE_SCANNER_CASHIER_FLOW_REPORT.md`
- `POS_M4_FAST_ADD_UNKNOWN_BARCODE_INVENTORY_REPORT.md`
- Manual1 R3 reports under `POS_MANUAL1_R3_*` (prompt’s `MANUAL1_R3_FINAL_REPORT.md` missing as exact name)

### POS locks

- `06-HARDWARE/HARDWARE_LOCK.md`
- `05-UI-UX/UI_LOCK.md`
- `01-FEATURE-LOCK/FEATURE_LOCK.md`
- `09-DEPLOYMENT/DEPLOYMENT_LOCK.md`
- `00-HANDOFF/V1_BUILD_ORDER.md`
- `00-HANDOFF/FINAL_SCOPE_DECISIONS.md`

### LicensingOps

- `docs/ISSUANCE_CONTRACT_HARDENING_REPORT.md`
- `docs/ISSUANCE_CONTRACT_HARDENING_REVIEW_REPORT.md`
- `docs/HOST_TERMINAL_MODEL_UX_ALIGNMENT_REPORT.md`
- `docs/BETA_LICENSE_ISSUANCE_PLAYBOOK.md`
- `README.md`

## Appendix B — Answers to required questions

| # | Answer |
| --- | --- |
| 1 Fully proven | Host licensing, flat Ops issuance, PRE-I1 LAN security/roles, Kirch physical LAN, I1 package |
| 2 Code not hardware | Host scan/Fast Add; Windows print path; package without physical printer |
| 3 User-attested physical | Second-device LAN (Kirch) |
| 4 Design intent only | Store logo; portable backup; LicOps store master; Host-like LAN shell |
| 5 Confusing to client/dev | Invented StoreId; HostLicenseId on terminal; remote landing tone; manual TerminalId |
| 6 Must fix before real beta | A, B, E (minimum product feel); D before portable update claim; F/G before device claims |
| 7 Can wait after beta | Public MSI, ESC/POS, drawer, cloud activation, expanded Manager remote pages, BIR production |
| 8 Hide from beta | Remote Read-Only entry (default), Report Artifacts (already internal-flagged), Advanced LAN diagnostics expanded by default |
| 9 Implement first | **A** |
| 10 Blockers for others | A blocks clean B; E blocks LAN F; D/C/E/F/G block honest I |
| 11 Need DB migration | **C** (logo); possibly D if backup format version bumps |
| 12 Both repos | A (+ B copy contract); H cross-links |
| 13 Hardware proof | F scanner; G printer; E preferred second device after shell change |
| 14 Package rerun | D, E (assets), C if branded package claim, **I always** |
| 15 Safe marketing | Offline Host POS; LAN Cashier/Manager; per-terminal license; local backup; wedge scanner; Windows queue receipts (if proven) |
| 16 Forbidden claims | Cloud activation; BIR production; ESC/POS/drawer/cutter; HID/BT scanners; public installer as done |

## Appendix C — Companion matrix

See `00-HANDOFF/BETA_READINESS_R1_LANE_MATRIX.md`.

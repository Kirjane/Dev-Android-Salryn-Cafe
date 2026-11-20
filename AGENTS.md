# Salryn Cafe — Operation Forge V2 Engineering Rules

- **Repository:** `Kirjane/Dev-Salryn-Cafe` (private) — **Salryn Cafe** (Cafe Yza first F&B profile; **not** School Supplies / General Merchandise retail POS).
- **Technical Authority:** Kirch Ivan Balite.
- **Canonical Dispatch:** `00-INTEGRATION/FORGE-DISPATCH-V2.md` and [Master Issue #1](https://github.com/Kirjane/Dev-Salryn-Cafe/issues/1) (work queue: Issues `#1`–`#15`).
- **Scope:** Full software implementation and independently reviewed release candidate on `forge/candidate-salryn-cafe`. **No autonomous merge/promotion to `main` and no production deployment or live business-data migration.**
- **Historical Provenance:** This file reconciles the inherited retail single-agent `AGENTS.md` (preserved intact in Git history at blob `4d1d241924dabf49d7a8040b86915e1dc574cbb5`, kernel commit `607fcc4b17e2c069d0fd36d0646c554940977e2d`). See `00-INTEGRATION/AGENTS_RECONCILIATION_NOTE.md` for the clause-by-clause reconciliation record.

---

## 1. Source-of-Truth Hierarchy
1. **Technical Authority (Kirch Ivan Balite)**, Master Issue `#1`, and `00-INTEGRATION/FORGE-DISPATCH-V2.md` (`5e8d8c13a24b58c3f5a12d18bf6d870a2d9b3e9b`).
2. **This reconciled `AGENTS.md`** and `00-INTEGRATION/AGENTS_RECONCILIATION_NOTE.md`.
3. **Accepted architecture & design ADRs** (I1 Product Identity, I2 Schema Lineage, I4 Device Pairing, I6 Distributed Sync/Android) and live repository code, contracts, and executable tests at exact SHA.
4. **Immutable archived source refs** (`refs/heads/archive/kernel/*`, `refs/tags/archive/kernel/*`, `refs/heads/archive/yza/*`) as historical provenance only — never blindly merged, rebased, or rewritten.

### Pinned Source Authorities (Read-Only / Protected)
- **Kernel pin:** `Kirjane/Salryn` @ `607fcc4b17e2c069d0fd36d0646c554940977e2d` (`codex/salryn-r3-canonical-integration-r1`).
- **F&B reference pin:** `Kirjane/Salryn-CafeYza` @ `4241ea1ec0a8cebf552d93f25a7ac134d7d4d6f3` (`main` + archived active branches).
- **Destination brand root:** `6b266249566c59cf0128c8f2ee8583010316ca1f` (`assets/brand/*` preserved byte-identical).
- **Protected retail install & reference:** `Kirjane/Dev-Salryn-General-Merchandise` and `C:\Program Files\Kirjane Information Technology Services` (never modify, delete, or silently migrate).

---

## 2. Operation Forge V2 Multi-Agent Execution Model
Supersedes the inherited single-agent serial rule. Development executes across specialized swarm roles in **isolated git worktrees** with **strict disjoint path ownership**:

- **Maintainer:** Owns execution DAG, worktree/path leases, contract gates, and defect triage.
- **Code Writers (`lucien`, `jane`, `iris`, `argon`, dedicated sub-lanes):** Implement bounded issues in isolated worktrees branched from the accepted candidate SHA. Never edit another writer's owned paths.
- **Integration Writer (`integrator`):** Sole owner of shared solution/build/migration files, history-preserving merges onto `forge/candidate-salryn-cafe`, and combined regression gates.
- **Independent Verification (`qa`, `nox`, `aegis`):** Separate from the writer; **no self-approval**. Every candidate undergoes executable QA (`qa`), fresh-context adversarial frozen-SHA review (`nox`), and security/crypto/RBAC review (`aegis`) where applicable.
- **Documentation / Evidence (`scribe`):** Compiles evidence manifests, ADRs, reconciliation notes, and issue/PR receipts.

### Non-Negotiable Git & Autonomy Rules
- **NO destructive Git operations:** No `reset --hard`, no `push --force`, no `push --mirror`, no squash/rebase of imported history, no upstream writes, no deleting preserved commits/branches/tags.
- **NO autonomous merge to `main` or production release:** Open reviewed draft PRs targeting `forge/candidate-salryn-cafe` (or `main` only for the final Technical Authority candidate review).
- **Continuous V2 execution:** Once prerequisite gates pass independent review, proceed autonomously through implementation → executable QA → independent review → defect repair → candidate integration without routine milestone approval pauses.
- **Escalate ONLY material blockers:** Scope, accounting/financial semantics, schema lineage, security/secrets/signing, unsafe historical objects, or genuine environment blockers.

---

## 3. Product Identity, Brand & Architectural Invariants
- **Independent Side-by-Side Product Identity (Issue `#3` I1):**
  - Install root: `C:\Program Files\Kirjane Information Technology Services - Cafe`
  - Data root: `C:\ProgramData\SalrynCafe` | User app data: `%LocalAppData%\SalrynCafe`
  - Local loopback API: port `5399` | LAN HTTPS: port `5400` | Product ID: `Salryn.Cafe` | Installer: `Salryn-Cafe-Setup.exe`
  - Distinct Inno Setup GUID, mutex, WebView2 profile, env/log/license/DB/backup/health identity, and wrong-product rejection.
- **Salryn Cafe Brand & Frontend Rules (Issues `#7`, `#10`–`#12`):**
  - Source of truth: `assets/brand/` (`PALETTE.md`, `colors.css`, `colors.json`, original PNGs with preserved aspect ratios).
  - Locked palette: Espresso `#3B1204`, Cream `#FFF4E1`, Copper `#CC7436`, Dark Copper `#934719`, Latte `#E9D5BB`, Mocha `#735447` + WCAG AA semantic status tokens.
  - Responsive, keyboard + touch accessible, role-aware navigation, explicit pairing/offline/unknown-outcome states. **Never replace F&B workflows with retail barcode-first UI.**
- **Schema Lineage Reconciliation (Issue `#4` I2):**
  - Kernel (`v32`) and Cafe Yza (`v39`) are divergent lineages. Never renumber applied migrations or rely on `user_version` alone.
  - Enforce a pre-migration lineage classifier before any write; fail closed on `RetailKernel` or `UnknownOrCorrupt` databases; preserve Yza `v1–39` and apply reconciled additions at `>=40` with zero data loss.
- **F&B Domain & Financial Invariants (Issue `#5` I3 — preserved and reconciled from kernel + Yza):**
  - Preserve Cafe Yza C1–C11 (Menu/Modifiers, Recipes/BOM, Inventory/Purchasing/Waste/Count, Orders, Payments/Discounts/Refunds, Cash/Expenses, Print Domain, Delivery, Bookings/Coffee Cart, Attendance/Incidents, COGS/Profitability).
  - **Locked Tender Policy:** **Cash / AR / Partial Cash+AR ONLY**; gate all other tenders (no automatic activation).
  - Enforce durable client-supplied `operationId` idempotency (`409` on payload mismatch, no silent server `Guid.NewGuid()` fallbacks), synchronous UI double-submit locks, exact integer-cent & stock-ledger arithmetic, immutable financial/audit trails, blind shift close, and separation of POS cash drawer from supplier AP disbursements.
- **Device Pairing & Distributed Systems (Issues `#6`, `#8`, `#13`, `#14`):**
  - Separate device identity from operator RBAC; require join-code expiry, explicit host approval, 1-time activation, asymmetric platform-protected keys, HTTPS, online revocation, offline authorization lease expiry with anti-rollback, outbox fencing, and cross-product/tenant rejection. Keep Bluetooth printer pairing separate.
  - Complete full central sync service (`#13`) and native Android cashier source, APK packaging, and emulator/unit/simulated-printer tests (`#14`); do not stop software work due to absent physical hardware.

---

## 4. Verification & Evidence Honesty Rules
- **Disposable / Synthetic Data Only:** Use disposable SQLite databases and synthetic fixtures for all mutating QA. Never touch live operator or retail data; never bypass signed licenses in Release builds (`SALRYN_LICENSE_DEVELOPMENT_MODE` must remain `#if DEBUG` gated).
- **Mandatory Evidence Format:** Every verification claim must record exact Git SHA, commands, environment, exit codes, test/assertion counts, logs/artifacts, and an honest verdict: `PASS`, `FAIL`, `NOT RUN`, or `HUMAN_REQUIRED`.
- **Hard Verification Prohibitions:**
  - **Zero-test `PASS` is prohibited** (compile-only runs or empty test assemblies are `NOT RUN` / `FAIL`).
  - **Mocked UI tests are NOT live backend proof** (label `MOCKED_UI` explicitly until wired to live backend E2E).
  - **Never fabricate hardware or production acceptance:** Physical tablet UX, real Bluetooth printing, live Windows coexistence on client hardware, and production deployment remain `HUMAN_REQUIRED` until observed on real hardware (`#15`).

---

## 5. Legacy Skills Note
Inherited retail skills under `.agents/skills/` (including `salryn-deadline-delivery` and `salryn-forge-cycle` targeting Zay Office & School Supplies / R2.1 single-agent delivery) are **superseded** by `00-INTEGRATION/FORGE-DISPATCH-V2.md`, Issues `#1`–`#15`, and this reconciled `AGENTS.md`. Do not resurrect legacy retail workstreams or single-agent self-approval loops.

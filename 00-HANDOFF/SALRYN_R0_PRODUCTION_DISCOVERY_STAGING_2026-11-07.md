# SALRYN POS — R0 Production Discovery, Scope Freeze, and Staging Authority

**Date:** 2026-11-07 (PHT)  
**Project:** Salryn POS (retail/school-supplies product; **not** Salryn Cafe Yza)  
**Technical authority:** Kirch Ivan Balite  
**Company/product identity:** Salryn — a product of **Kirjane Information Technology Services**  
**Status:** `R0 / STAGING PROPOSAL — NOT MERGED, NOT TESTED, NOT PRODUCTION APPROVED`

## 1. Immutable source checkpoint and reconciliation

| Source | Pinned commit | Disposition |
| --- | --- | --- |
| `Kirjane/Salryn:main` | `dc5a6dcf713da7d4e9d0f47b47a685027eae45b9` | Candidate base for **single-Host, offline-first Windows POS** production-hardening work |
| `Kirjane/Salryn:KIRCH-U1-ACCOUNTS-RECEIVABLE-UTANG-LEDGER` | `f85e910902622f950230095a3dcf00e43f09b46b` | Diverged paid-addon candidate; **not in main**; separately evaluate before product inclusion |
| `KIRCH-SALRYN-R0-PRODUCTION-DISCOVERY-20261107` | Initially pinned to `main` checkpoint | Discovery/staging only. Preserve main and U1 intact. |

Git comparison checked 2026-11-07: U1 branch is **20 commits ahead / 6 behind main**, changes 66 files. Do not conflate U1 features with the shipping baseline and do not merge U1 without a formal integration/financial gate.

Historical documents such as `00-HANDOFF/FINAL_ACCEPTANCE.md` (Phase 1 skeleton) and `CURRENT_STABLE_STATE_AND_NEXT_WORK.md` (earlier LAN development) are **not** current production acceptance decisions. Current beta evidence includes `BETA_RC1_INSTALLER_APP_SHELL_REPORT.md`, `BETA_RC1_KNOWN_LIMITATIONS.md`, `BETA_RC1_LAN_TERMINAL_HARDENING_REPORT.md`, and installed QA reports. Use exact source and live build/test evidence over stale status wording.

## 2. Proposed production V1 scope freeze

**IN SCOPE (subject to final QA):** one Windows Host PC, local SQLite, license activation, first-run setup, local login and cashier shifts, Host checkout, product catalog, inventory movements, persisted receipts/reprints, sales history/void according to permission policy, dashboard/reporting after correctness repair, audit, local backup/verification/restore, printer queue where actually tested, a single-store operator manual, installer and upgrade rehearsal.

**NOT IN V1 / FUTURE:** LAN terminals, remote POS, remote read-only network clients; cloud/multi-store synchronization; U1 Utang as a paid addon until explicitly integrated and accepted; unsupported fiscal/BIR production claims; automated updates; speculative hardware and printer protocols. Do not remove historical implementation code merely to enforce the V1 product boundary.

**Future-feature description — LAN Terminals:**

> LAN Terminals are a planned extension allowing separately licensed cashier or manager devices on the same local network to access a Host-managed POS. The Host remains authoritative for sales, inventory, receipts, and data. This is **not included in the first production release**. Pairing and device/session reconnection, Host restart behavior, browser storage and end-to-end persistence/recovery require field acceptance before enablement.

Keep this description in product copy and roadmap. For a *true* V1 disable gate, verify Host listener startup, persisted LAN settings, remote URL/route access, licensing claims, and privileged APIs—not only navigation text. Existing Host records must not be destroyed; preserve administrative recovery/disable capability. Changes to rollout gating require separate verification.

## 3. Early source-confirmed release risks

### R0-P1 — Dashboard financial metric correctness (**ship blocker**)

`src/Salryn.Api.Local/Services/DashboardSummaryService.cs` (on current main) uses `SaleHistoryFilter(Status: null, PageRequest.FirstPage(200))`; it counts all matching rows but sums only returned first-page totals. Voided-status inclusion is not explicitly excluded in this query. Repair with an authoritative aggregate/query that includes only financially applicable sales and full matching range; add voided, 201+/large-day and time-boundary proof.

### R0-P1 — LAN persistence and reconnect (**deferred, not misdiagnosed**)

Evidence on main:
- `LanRemoteTransportConfigService.cs` persists LAN configuration in SQLite.
- `SqliteTerminalRegistryRepository.cs` persists terminal registration in SQLite.
- `RemotePosOperatorSessionService.cs`: operator sessions are deliberately **in memory**, expire after 30 minutes, and reset on Host restart.
- `RemotePosPairingGateService.cs`: ephemeral pending join requests and join codes are held in concurrent dictionary/memory.
- `useRemotePosClient.ts`: ambiguous checkout state is saved to **sessionStorage**, unlike durable server-side request replay; server idempotency itself has an SQLite ledger after Beta RC1 hardening.
- `terminalLicenseStorage.ts`: device machine identity/license is tied to browser localStorage and may be lost if browser data is cleared or a different browser/profile is used.

These are multiple different persistence semantics. Do **not** claim terminal registrations are not persisted; do **not** silently auto-resume short-lived security sessions. Diagnose separately: stored configuration, registered identity, approval/trust, operator login session, pending checkout, and actual network listener after restart.

### R0-P1 — Client installation and recoverability (**ship blocker**)

Latest Beta RC1 reports document successful installed shell/installer proof but no public-production signoff. Existing limitations: unsigned installer; no auto-update; framework-dependent .NET 10 runtime; WebView2 may need network during install; LAN not automatically configured; real hardware tests outstanding; upgrade recovery needs a dedicated rehearsal. Prepare clean Windows 10/11 machine, unlicensed first run, licence, shift, checkout, receipt, restart, backup, restore, upgrade, uninstall/data preservation, and offline use tests.

### R0-P2 — Brand / client scope clarity

`LicensePage.tsx` currently embeds a `Licensing & Credits` section listing individuals. `ProductCreditsCard.tsx` also contains a staff org-chart. Remove those **product-facing** credits and display a clear company product identity. Preserve licensing Terms and support details; legal changes require review. Avoid advertising LAN as available.

### R0-P2 — U1 debt and integration risk

U1 is on a diverged branch, not shipping main. Earlier source audit identified dashboard aggregation, AR overdue after collection reversal, same-day UTC cutoff, missing adjustment idempotency and edge-case coverage. If U1 is ever proposed for V1, resolve these and perform accepted merge/rebase review and installed U1 QA; otherwise keep explicitly **excluded** from V1 docs, demo and client offer.

## 4. Fresh phases / exact acceptance exits

### R0 — DISCOVER / FREEZE (current; no code promotion)
- Capture main/U1 branch SHAs and uncommitted local work; inventory client hardware, license, invoice/report legal expectations.
- Confirm minimum sellable Host workflow and client-specific requirements.
- Record known deferreds, blockers, exact tests, and responsible owners.
- **Exit:** signed V1 scope and frozen main candidate + traceable evidence table.

### R1 — STAGE PRODUCT BOUNDARIES / BRANDING
- Stage product-level Kirjane identity; replace individual staff credits with company attribution.
- Label LAN Terminal as **Future Feature** on Host navigation/management; do not permit accidental production rollout; retain internal LAN code and safe disable controls.
- Keep U1 and BIR/official capabilities out of V1 marketing unless separately accepted.
- **Exit:** UI snapshot review, permissions/route review, frontend build, no hidden live LAN exposure.

### R2 — FIX FINANCIAL / DATABASE CORRECTNESS
- Repair dashboard aggregates (voids, >200 sales, date boundaries).
- Prove checkout duplicate-submit safety, receipt/reprint/void stock consistency, cashier close, persisted state after restart, backups, verification and controlled restore.
- Test database corruption/full disk/locked database and expected failure messages on disposable data.
- **Exit:** deterministic automated proof + fresh SQL test fixtures; no unexplained discrepancies.

### R3 — HOST RUNTIME / INSTALLER HARDENING
- Rebuild React dist and .NET release; package shell/API/native WebView2 loader; document all runtime dependencies and installer SHA-256.
- Test clean install and license issuance, signed-license re-install, upgrade in place, shortcut launch, no dev-server dependency; ensure real store DB is never bundled.
- **Exit:** reproducible release-candidate installer; two clean-host install reports.

### R4 — PHYSICAL POS ACCEPTANCE
- On Windows Host with client-class keyboard-wedge scanner and Windows printer: run full cashier open→scan→pay→receipt→reprint→void→close→report.
- Printer unavailable/disabled must not duplicate or undo committed sales; test hardware failure separately from queue accepted.
- Test no Internet at checkout; Host service restart and power-interruption simulations against QA data.
- **Exit:** signed QA matrix/screenshots; unresolved P0/P1 = zero.

### R5 — OPERATIONS / SUPPORT READINESS
- Screenshot-verified final operator manual, recovery runbook, daily backup rota, offline operation notes, installer/support contact.
- Prepare rollback/restore rehearsal, license issuance SOP, version record, client feedback and formal UAT checklist.
- **Exit:** support team can recover independently; client can operate basic shift without developer intervention.

### R6 — CLIENT PILOT / GOVERNANCE
- Single-store pilot using actual hardware, separated QA and production data.
- Audit opening/closing sales, daily totals, unexpected shutdowns, restore, and end-of-day reconciliation.
- **Exit:** client operational signoff + Maintainer exact-commit release approval.

### F1 — FUTURE LAN TERMINAL STABILIZATION (outside V1)
- Write a six-layer persistence/reconnect specification: (1) Host LAN configuration, (2) terminal registration, (3) pairing request/trust, (4) operator session, (5) pending checkout status/replay, (6) restart/network outage listener recovery.
- Prove clean restart, stale IP and browser cache, same/different browser, host crash, Wi-Fi dropout, revocation, retry, >1 terminal with SQLite idempotency.
- Make explicit tradeoff: security sessions **should not** survive restart if that violates fail-closed policy; users can re-authenticate without losing committed business records.
- **Exit:** physical two-device test and bounded approval before V2 marketing.

## 5. Execution discipline and stop rules

- Scope is **Salryn POS**, not `Salryn-CafeYza`.
- Preserve `main` and all historical branches. All changes live in dedicated staging/writer branches; use PR and exact SHA acceptance before promotion.
- Re-run `dotnet build Salryn.sln -c Release`, `dotnet test Salryn.sln -c Release`, CoreSmoke, `npm --prefix src/Salryn.App.Web ci`, `npm --prefix src/Salryn.App.Web run build`, then packaged install / manual proof.
- Never report tests PASS based on docs or source inspection; report `NOT RUN` when not executed.
- Stop on mismatched SHA, dirty/unknown worktree, destructive DB operation without backup, U1/main implicit merge, silent LAN enablement, or BIR/compliance claims without acceptance.
- No production promise of “will never fail”; the verifiable objective is transactional integrity, fault containment, recovery, and supported operational continuity.

## 6. Source map

- [Main pinned checkpoint](https://github.com/Kirjane/Salryn/tree/dc5a6dcf713da7d4e9d0f47b47a685027eae45b9)
- [U1 pinned checkpoint](https://github.com/Kirjane/Salryn/tree/f85e910902622f950230095a3dcf00e43f09b46b)
- `00-HANDOFF/BETA_RC1_INSTALLER_APP_SHELL_REPORT.md`
- `00-HANDOFF/BETA_RC1_KNOWN_LIMITATIONS.md`
- `00-HANDOFF/BETA_RC1_LAN_TERMINAL_HARDENING_REPORT.md`
- `00-HANDOFF/I1_PACKAGE_INSTALL_PROOF_REPORT.md`
- `00-HANDOFF/PRE_I_FINAL_CAPABILITY_MATRIX.md`
- `00-HANDOFF/U1_INSTALLED_MANUAL_QA_CHECKLIST.md` (U1 branch only)
- `src/Salryn.Api.Local/Services/DashboardSummaryService.cs`
- `src/Salryn.Api.Local/RemotePos/RemotePosOperatorSessionService.cs`
- `src/Salryn.Api.Local/RemotePos/RemotePosPairingGateService.cs`
- `src/Salryn.Infrastructure/Data/Services/LanRemoteTransportConfigService.cs`
- `src/Salryn.Infrastructure/Data/Repositories/System/SqliteTerminalRegistryRepository.cs`
- `src/Salryn.App.Web/src/hooks/useRemotePosClient.ts`
- `src/Salryn.App.Web/src/utils/terminalLicenseStorage.ts`

**Next authorized code work:** R1 customer-facing branding and LAN future-status copy on this staging branch; separate R2 dashboard repair branch. Do not merge to main until the required build and QA gates are executed.
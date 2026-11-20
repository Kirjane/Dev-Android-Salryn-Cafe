# AGENTS.md Reconciliation Note (Operation Forge V2)

- **Destination Repository:** `Kirjane/Dev-Salryn-Cafe`
- **Candidate Base SHA:** `a391d164269537f3b715889f62bcfb776936ebb7` (`forge/candidate-salryn-cafe`)
- **Reconciling Authority:** `00-INTEGRATION/FORGE-DISPATCH-V2.md` (blob `5e8d8c13a24b58c3f5a12d18bf6d870a2d9b3e9b`) and Master Issue `#1`
- **Preserved Historical `AGENTS.md`:** Blob `4d1d241924dabf49d7a8040b86915e1dc574cbb5` (inherited verbatim from Kernel pin `607fcc4b17e2c069d0fd36d0646c554940977e2d` and preserved intact in Git history)

## Purpose
`00-INTEGRATION/FORGE-DISPATCH-V2.md` (line 7) mandates:
> *"Do not replace imported historical AGENTS.md without reviewing and reconciling it."*

During I0 (`#2`), the inherited `AGENTS.md` from `Kirjane/Salryn` (`607fcc4b`) was preserved byte-identical (`4d1d2419`) on the candidate baseline. This note records the explicit clause-by-clause review and reconciliation performed so active `AGENTS.md` governs **Salryn Cafe (Operation Forge V2)** accurately while preserving full historical traceability.

## Clause-by-Clause Reconciliation Matrix
| Inherited `AGENTS.md` Clause (`4d1d2419`) | Conflict with Forge V2 / Issue `#1` | Disposition |
|---|---|---|
| `Repository: Kirjane/Salryn (base School Supplies / Retail POS, not Café Yza)` | Wrong repository and product; conflicts with `Kirjane/Dev-Salryn-Cafe` (Salryn Cafe / Cafe Yza F&B profile) and I1 side-by-side isolation (`#3`). | **Replaced** with `Kirjane/Dev-Salryn-Cafe`, Salryn Cafe identity (`Salryn.Cafe`, port `5399`/`5400`, `C:\ProgramData\SalrynCafe`), and explicit protection of `C:\Program Files\Kirjane Information Technology Services`. |
| `Executor: Antigravity, one agent acting as Implementer, Reviewer, QA and Release Engineer... No delegation, no new agent threads` + `adversarial self-review` | Directly contradicts Forge V2 multi-agent swarm, isolated worktrees, disjoint path ownership, and strict separation of writer vs. independent reviewer (`no self-approval`). | **Replaced** with Forge V2 multi-agent execution model (Maintainer, Writers, Integrator, Independent QA/Nox/Aegis, Scribe) and explicit prohibition on self-approval and autonomous `main` promotion. |
| `Business objective (CEO delivery)` — Zay Office & School Supplies, R2.1 baseline `d07f73a2`, PR #32, 4-hour/7-hour urgency window | Historical retail delivery milestone; conflicts with Issues `#1`–`#15` and pinned sources (`607fcc4b` kernel + `4241ea1e` Yza + `6b266249` brand root). | **Replaced** with Salryn Cafe V2 scope (Issues `#1`–`#15`), pinned source authorities, immutable archival refs (`archive/kernel/*`, `archive/yza/*`), and `assets/brand/*` palette lock. |
| `Source-of-truth order` pointing to `.agents/skills/salryn-deadline-delivery/SKILL.md` and `salryn-forge-cycle/SKILL.md` | Points contributors to superseded retail CEO matrix and single-agent self-review state machine. | **Replaced** with V2 authority hierarchy (`FORGE-DISPATCH-V2.md` + Issue `#1` → reconciled `AGENTS.md` → accepted ADRs/live code → archived refs) and explicit legacy-skills supersession note. |
| Financial & accounting invariants (idempotency, continuous numbering, immutable audit traces, exact arithmetic, drawer vs. AP separation, no fake payment/tax/cost/approval claims) | Fully aligned with Issue `#5` (I3) and Forge V2 governance. | **Preserved & Extended** with Cafe Yza C1–C11 preservation, locked tender policy (Cash / AR / Partial Cash+AR only), client `operationId` enforcement, and schema lineage classification (`#4` I2). |
| Disposable DB for mutating QA, read-only real data, no license bypass, WAL backup/restore, honest PASS/FAIL/NOT COMPLETED reporting | Fully aligned with Issue `#9` (I7) and Issue `#15` (I8). | **Preserved & Extended** with `#if DEBUG` license-bypass prohibition, explicit `PASS`/`FAIL`/`NOT RUN`/`HUMAN_REQUIRED` taxonomy, zero-test `PASS` ban, and `MOCKED_UI` vs. live E2E distinction. |
| Hardware / distributed scope absent from retail `AGENTS.md` | Issue `#6` (I4), `#8` (I6), `#13` (I6A), `#14` (I6B), `#15` (I8) require P0 device pairing, central sync, native Android APK/emulator, and hardware `HUMAN_REQUIRED` boundaries. | **Added** explicit Device Pairing P0, Central Sync, Native Android software-completion, and hardware-honesty rules. |

## Verification
- Historical `AGENTS.md` remains reachable and unaltered at `git show 607fcc4b17e2c069d0fd36d0646c554940977e2d:AGENTS.md` (blob `4d1d241924dabf49d7a8040b86915e1dc574cbb5`).
- Only `AGENTS.md` and `00-INTEGRATION/AGENTS_RECONCILIATION_NOTE.md` are modified on `forge/reconcile-agents-md`.

# OPERATION FORGE — SALRYN CAFE / V2 DISPATCH

Technical Authority: Kirch Ivan Balite  
Destination: Kirjane/Dev-Salryn-Cafe (private)  
Scope: full software implementation and independently reviewed candidate, not production deployment.

Read [master issue #1](https://github.com/Kirjane/Dev-Salryn-Cafe/issues/1) and [all issues](https://github.com/Kirjane/Dev-Salryn-Cafe/issues). This V2 dispatch supersedes V1's empty-repo and stop-after-I0 instructions. Do not replace imported historical AGENTS.md without reviewing and reconciling it.

## Source and import
- Kernel: Kirjane/Salryn, codex/salryn-r3-canonical-integration-r1, 607fcc4b17e2c069d0fd36d0646c554940977e2d.
- F&B: Kirjane/Salryn-CafeYza, main, 4241ea1ec0a8cebf552d93f25a7ac134d7d4d6f3; review active branches.
- Destination: preserve brand root 6b266249566c59cf0128c8f2ee8583010316ca1f plus every subsequent destination commit.
- All advertised source branches/tags and all reachable commits retain original SHAs, parent graphs and authorship. Full fetch; namespaced archival refs; compare reachable SHA lists and refs; no squash/rebase/force push or upstream writes. Review secrets, sensitive history and workflow triggers before transfer; security blockers require escalation, not dropping/rewrite.
- Candidate begins on exact kernel pin; explicitly integrate existing destination history with both parent lineages intact (unrelated-history merge only if necessary), preserve assets/brand; verify original commit trees, reachability, LFS/submodules, git fsck. Yza history is archived and its behavior adapted, not blindly merged. Main promotion requires Technical Authority approval; development may proceed on reviewed candidate branches.

## Concurrent dispatch
| Issue | Owner | Scope |
|---|---|---|
| #2 | Argon / Integrator | Full history import / provenance |
| #3 | Lucien / Aegis | Independent product identity and installer |
| #4 | Lucien / Integrator | Migration lineage reconciliation |
| #5 | Jane / Lucien | C1–C11 F&B and financial integrity |
| #6 | Aegis / Lucien / Jane | Device pairing and authorization (P0) |
| #7 | Iris / Jane | Live frontend integration |
| #8 | Lucien / Jane / Aegis | Distributed architecture coordination |
| #9 | QA / Nox | Continuous executable DAT |
| #10 | Iris UX-A | Design system, assets and shared components |
| #11 | Iris UX-B | Cashier, order and kitchen workflow |
| #12 | Iris UX-C | Back office, owner and device management |
| #13 | Central lane | Deployable central transport / sync |
| #14 | Android lane | Native cashier / printer code / APK & emulator |
| #15 | Maintainer / QA / Nox | Human-test-ready candidate and handoff |

Start UX design, typed fixture-backed components, architecture, device trust design, QA and import concurrently in isolated worktrees. Allocate explicit path ownership; one integrator owns shared files/migrations. Contract-dependent live wiring waits for internally reviewed contracts. Independent review, test and repair loops continue through the software completion gate; no routine human approval per intermediate milestone. Escalate material scope, accounting, data/safety decisions, secrets, signing or genuine environment blockers. No autonomous main promotion or deployment.

## Brand and product requirements
Use assets/brand/PALETTE.md, colors.css, colors.json and original PNGs (preserve aspect ratio). Espresso #3B1204, Cream #FFF4E1, Copper #CC7436, Dark copper #934719, Latte #E9D5BB, Mocha #735447. WCAG AA, semantic feedback colors, responsive, keyboard and touch. No replacement retail UX. 
General Merchandise install C:\Program Files\Kirjane Information Technology Services is protected. Proposed independent Cafe install C:\Program Files\Kirjane Information Technology Services - Cafe, C:\ProgramData\SalrynCafe, %LocalAppData%\SalrynCafe, port 5399, new installer GUID. Verify collisions, isolation and wrong-product guards.
Preserve Cafe Yza requirements, C1–C11, Cash/AR/partial Cash+AR, recipe/BOM, kitchen and sync semantics. Kernel v32 and Yza v39 are divergent lineages; require migration ADR and executable upgrade/fresh tests, no data reset. Pairing P0: device identity and operator roles distinct; online revocation, offline authorization expiry, replacement and outbox fencing, cross-product rejection, real printer pairing separate.

## Definition of done
All feasible software implemented and regression-tested on a single candidate SHA, with exact source provenance, per-requirement test traceability and no unresolved software release blockers. Produce Windows installer, central deployment assets, Android APK (test signed if appropriate), build logs, SHA-256 manifests and handoff instructions. Compile and emulate Android where possible; hardware unavailability is not a reason to leave application code unfinished. Mock results are not live proof. Record commands, environment, exit codes, assertions/counts and PASS/FAIL/NOT RUN/HUMAN_REQUIRED; zero-test PASS prohibited. Actual hardware and business acceptance remain pending until human evidence. Never claim physical tablet, real Bluetooth prints, live deployment or production approval without evidence. Use synthetic data only.

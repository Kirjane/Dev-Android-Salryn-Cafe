# M-Series Completion Criteria and LAN Transition Gate

## Source Stamp

- Repo: `Kirch-Nairu/Salryn`
- Stable branch: `KIRCH-SANDBOX`
- Sandbox SHA: `0ea9790d5c86bba350c9db800c101195fa658c0c`
- Candidate branch: `KIRCH-FREEHAND`
- Freehand SHA before M8: `a048a1d847cfa1f435da23b2cb128cd7f360719f`
- Ahead/behind before M8: `26 ahead / 0 behind` from `KIRCH-SANDBOX`
- Promotion status: parked until full M-series completion

## Stable Sandbox Stack

Stable Sandbox stack only:

- `LAN-M13` - remote read-only LAN usability
- `ROLE-M1` - role-aware operator UX boundary
- `ROLE-M2` - backend permission enforcement and confidence baseline
- `ROLE-M3` - HTTP-level Api.Local permission runtime harness

## Current Verified Candidate Stack

Verified candidate stack on `KIRCH-FREEHAND`, not stable until promotion:

- `ROLE-M4` - permission boundary regression matrix and operator role scenario coverage
- `M5` - current-state drift guard and verified candidate stack ledger
- `M6` - candidate stack verification replay and promotion-readiness dry run
- `M7` - candidate stack limit guard and promotion debt meter

## Current M8 Candidate

- M8 is current candidate work and unverified until Codex verifies it.
- M9 is not started.
- LAN-M14 is not started.
- LAN completion series is not started.

## M-Series Completion Criteria

The M-series is complete only when all of the following are true:

1. ROLE-M4 is verified candidate.
2. M5 is verified candidate.
3. M6 is verified candidate.
4. M7 is verified candidate.
5. M8 is verified candidate.
6. No unverified candidate remains stacked above verified work.
7. Full build passes.
8. Full test suite passes.
9. CoreSmoke passes.
10. All required InfrastructureSmoke commands pass.
11. Web npm install/build pass.
12. Worktree is clean.
13. Freehand is ahead-only and not behind Sandbox.
14. No schema/migration changes were introduced.
15. No seed data changes were introduced.
16. No production behavior expansion was introduced.
17. No LAN-M14 or LAN completion implementation was started.
18. No role editor/custom permission manager was introduced.
19. No POS checkout implementation was introduced.
20. No Settings/Backup/Official Output/Reports implementation was introduced.
21. No RMO/printer/PDF/Crystal/ESC-POS behavior was introduced.
22. No certification/accreditation/legal compliance claim was introduced.
23. Final M-series closure review is completed.
24. Kirch explicitly declares M-series complete.
25. Kirch explicitly authorizes final promotion.

## M-Series Closure Rule

- M8 defines the criteria but does not itself promote.
- After Codex verifies M8, the next safe action is either:
  - M-series closure review, or
  - final M-series promotion planning only if Kirch says the M-series is complete.
- No LAN completion work may begin before M-series promotion.
- Promotion remains parked until full M-series completion and explicit Kirch authorization.
- M-series complete state must not be inferred from stale chat text or historical handoff reports.

## LAN Transition Gate

LAN completion series may begin only after all of these are true:

1. M-series is complete.
2. M-series candidate stack is Codex-verified.
3. Kirch explicitly says promote.
4. M-series is promoted into `KIRCH-SANDBOX`.
5. `KIRCH-FREEHAND` is reset/aligned to the promoted Sandbox baseline.
6. Sandbox and Freehand are identical after promotion.
7. A fresh LAN completion source stamp is taken.
8. A new LAN completion task is created from current GitHub truth.
9. LAN-M14 or LAN completion scope is explicitly defined.
10. No stale M-series docs are used as LAN authority.

M8 boundary statements:

- LAN completion series is not started by M8.
- LAN-M14 is not started by M8.
- No LAN bind activation happens in M8.
- No live remote transport dispatch happens in M8.
- No LAN mutation happens in M8.
- No remote cashier/admin behavior starts in M8.
- No QR, discovery, mobile, or cloud behavior starts in M8.

## HIGH Promotion Debt Boundary

- Current Freehand promotion debt is HIGH after M7 / before or during M8 due to the ahead count.
- HIGH is acceptable only because Kirch explicitly parked promotion until full M-series completion.
- HIGH blocks feature work and LAN work.
- HIGH permits only narrow M-series completion/closure discipline work until final M-series decision.
- HIGH does not authorize promotion by itself.
- Promotion requires explicit Kirch completion declaration and explicit Kirch promotion authorization.

## Deferred Unsafe Work

The following remain deferred unless Kirch explicitly starts a narrow post-promotion sprint from current GitHub truth:

- M9
- LAN-M14
- LAN completion series
- LAN bind activation
- live remote transport dispatch
- LAN mutation
- remote cashier/admin behavior
- QR, discovery, mobile, or cloud behavior
- schema or migrations
- seed data changes
- role editor
- custom permission manager
- POS checkout implementation
- Settings, Backup, Official Output, or Reports implementation
- RMO, official output, PDF, Crystal, ESC/POS, printer, receipt-printing, or certification wording

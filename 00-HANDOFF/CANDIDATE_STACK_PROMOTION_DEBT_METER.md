# Candidate Stack Promotion Debt Meter

## Source Stamp

- Repo: `Kirch-Nairu/Salryn`
- Stable branch: `KIRCH-SANDBOX`
- Sandbox SHA: `0ea9790d5c86bba350c9db800c101195fa658c0c`
- Candidate branch: `KIRCH-FREEHAND`
- Freehand SHA before M7: `daccf2970c4773c8be6314febeb246bc41ccb67a`
- Freehand SHA before M8: `a048a1d847cfa1f435da23b2cb128cd7f360719f`
- Ahead/behind before M8: `26 ahead / 0 behind` from `KIRCH-SANDBOX`
- Promotion status: parked until full M-series completion

## Current Stable Stack

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

## Promotion Debt Meter

### LOW

- Freehand ahead <= 10 commits
- Changed files <= 8
- Verified candidate items <= 2
- No unverified candidate after Codex verification
- No risky file categories touched

### MODERATE

- Freehand ahead 11-20 commits
- Changed files <= 12
- Verified candidate items <= 4
- No risky file categories touched
- Still acceptable for one more narrow discipline sprint if Codex verifies

### HIGH

- Freehand ahead 21-35 commits
- Changed files <= 20
- Verified candidate items <= 6
- Promotion planning should be considered before more stacking
- Still no unsafe categories allowed

### STOP / REVIEW

- Freehand behind Sandbox
- Risky file categories touched
- Schema or seed change
- Production behavior change
- Failed build/test/smoke/npm
- Unverified candidate stacked above another unverified candidate
- Ahead count > 35
- Changed files > 20
- Candidate scope no longer narrow

## Current Classification

Current stack after M7 / before or during M8: `HIGH`

Reason:

- Freehand is `26 ahead / 0 behind`.
- Verified candidate stack has `ROLE-M4`, `M5`, `M6`, and `M7`.
- Changed-file footprint remains controlled.
- No unsafe categories are reported.
- Promotion remains parked until full M-series completion by Kirch instruction.

## HIGH Boundary Rule

- HIGH does not force promotion because Kirch explicitly parked promotion until M-series completion.
- HIGH blocks feature work and LAN work.
- HIGH permits only narrow M-series completion/closure discipline work until final M-series decision.
- HIGH does not authorize promotion by itself.
- Promotion requires explicit Kirch command after M-series completion criteria are met.
- M8 must not force promotion.
- M8 must only make M-series completion and LAN transition readiness visible.

## Risky File Categories

Any M8 change in these categories is unsafe and must fail guard review:

- migrations
- seeding
- checkout
- settings
- backup
- official output
- reports implementation
- RMO
- PDF
- Crystal
- ESC/POS
- printer
- receipt
- bind activation
- live transport dispatch
- discovery
- mobile
- cloud
- role editor
- permission editor
- Api.Local production behavior
- App.Web production behavior
- LAN-M14 implementation
- LAN completion implementation

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

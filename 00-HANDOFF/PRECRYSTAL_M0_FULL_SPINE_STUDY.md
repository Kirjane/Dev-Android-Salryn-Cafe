# PRE-CRYSTAL M0 Full Reporting Spine Study

**Branch:** `KIRCH-REPORTS-PRECRYSTAL-M0-FULL-REPORTING-SPINE`  
**Base:** `KIRCH-RMO-BIR-TRAD-M5-RC2-XZ-EJ-ESALES-COMPLETION` / `176d429f14b4cd241401b9a4c9cddab6f6711db1`  
**Protected refs:** `KIRCH-SANDBOX` and `KIRCH-INSTALLER-M1` are not modified.

## Decision

**GO — PRE-CRYSTAL M0 FULL REPORTING SPINE.**

This milestone creates the internal report-ready architecture only. It does not integrate SAP Crystal Reports runtime, add `.rpt` files, add Crystal binaries, add printer logic, add installer changes, or claim PDF/rendering support.

## Source-of-truth boundary

The existing M5-RC2 reporting projection remains the canonical source for official/tax totals:

```text
M5 canonical projection / safe domain service
  -> versioned report dataset builder
  -> renderer abstraction
  -> Crystal adapter / future isolated worker socket
  -> artifact metadata
```

The forbidden direction remains rejected:

```text
.rpt template
  -> direct mutable production database
  -> duplicated VAT/refund/void/tax formulas
```

## What this milestone adds

- P0/P1/P2 report catalog.
- Report-kind allow-list and permission mapping.
- Versioned dataset envelope with scope, generated timestamp, control totals, rows, source hashes, safety flags, and deterministic dataset SHA-256.
- Dataset builders for official report families using the existing M5 canonical projection.
- Explicit unavailable placeholders for report families whose source boundary is not safe yet.
- Renderer abstraction and status/capability model.
- Crystal renderer adapter skeleton that always fails closed until Sol wires a real licensed runtime or isolated worker.
- Future isolated Crystal worker DTOs.
- Safe artifact metadata contract.
- Host-only production report endpoints.
- Production Reports React page.
- Focused CoreSmoke gate `reports-precrystal-m0-full-reporting-spine`.

## Not implemented

- SAP package references.
- Crystal runtime integration.
- `.rpt` templates.
- Crystal binaries or runtime installers.
- Printer output.
- PDF output.
- Installer changes.
- BIR approval or accreditation claims.
- Production official posting activation.
- eSales transmission.

## Compatibility stance

This milestone intentionally avoids a Crystal runtime compatibility claim. Sol must later verify SAP support, target framework, process bitness, redistribution terms, and runtime installation before replacing the unavailable adapter.

## Security posture

- Host-local endpoints only.
- Authenticated session required.
- Actor identity is taken from the server session, not request bodies.
- Report kinds are allow-listed.
- Required permissions are derived from the catalog.
- Requests do not accept file paths, SQL text, template uploads, or arbitrary renderer names.
- Renderer unavailable state returns a controlled failure.

## Reliability posture

- Dataset generation is deterministic for equivalent canonical projection input and clock.
- Dataset SHA-256 covers the serialized report envelope with the hash field blanked before hashing.
- Official report datasets reference the M5 canonical projection hash.
- Unsafe report families return explicit unavailable datasets rather than direct database reads.
- Renderer failures do not crash the host.

## Freeze boundary

RMO feature coding remains frozen. This milestone consumes the M5 projection; it does not rewrite or replace M5 tax/accounting/reporting calculations.

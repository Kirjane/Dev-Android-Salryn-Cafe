# Crystal M1 Runtime Decision Study

Prepared: 2026-07-11

Repository: `Kirch-Nairu/Salryn`
Base branch: `KIRCH-REPORTS-PRECRYSTAL-M0-QA1-HARDENING`
Base SHA: `bcd6f576bde72db1111c1bc1a6a8f19b9b7d4dcd`
Candidate branch: `KIRCH-REPORTS-CRYSTAL-M1-RUNTIME-DECISION-AND-MINIMAL-PROOF`

## Decision

**GO — WORKER SOCKET ONLY.**

Do not implement direct in-process Crystal runtime inside Salryn's `net10.0` host/app.

The safe M1 result is to preserve the PRE-CRYSTAL server-owned dataset spine, strengthen the Crystal worker protocol and output-kind allow-list, and keep the renderer unavailable/fail-closed until Codex/Sol can test on a correctly licensed Windows machine with SAP Crystal Reports for Visual Studio runtime installed.

## SAP-primary evidence summary

Primary-source material reviewed:

1. SAP Crystal Reports for Visual Studio topic page
   - URL: `https://pages.community.sap.com/topics/crystal-reports/visual-studio`
   - Identifies SAP Crystal Reports for Visual Studio as the .NET developer integration/download lane.
   - States runtime licensing and redistribution are developer responsibility.

2. SAP Crystal Reports product page
   - URL: `https://www.sap.com/products/data-cloud/crystal-reports.html`
   - Current products include SAP Crystal Reports 2025/2020 64-bit and 2016 32-bit.
   - Visual Studio edition is a separate free download lane.

3. SAP Crystal Reports, developer version for Microsoft Visual Studio Supported Platforms PAM
   - URL: `https://www.sap.com/docs/download/2016/06/f871031e-757c-0010-82c7-eda71af511fa.pdf`
   - Updated: 2026-05-29.
   - Applies up to Support Package 40.
   - Supported OS is Windows-only for design/runtime scenarios.
   - 32-bit CR .NET runtime is discontinued after 2025-12-31.
   - Supported Visual Studio versions include VS 2022 and VS 2026 depending on support package.
   - Supported .NET Framework versions are .NET Framework 4.5.2, 4.6, 4.7, and 4.8.

4. SAP Crystal FAQ
   - URL: `https://pages.community.sap.com/topics/crystal-reports/faq`
   - States Crystal Reports for Visual Studio depends on .NET Framework-specific features and that there are no plans beyond Framework 4.8.
   - States not to use .NET Core / .NET 5+ if continuing to use SAP Crystal Reports.
   - States the embedded report engine has a maximum of three simultaneous user requests.

## Compatibility matrix

| Item | Finding |
|---|---|
| Salryn host target | `net10.0` / modern .NET line |
| CR4VS supported runtime target | .NET Framework 4.5.2, 4.6, 4.7, 4.8 |
| Direct in-process into Salryn host | **Rejected** — no SAP-primary support for `net10.0` in-process hosting |
| Windows requirement | Crystal path is Windows-only for the runtime/design lane studied |
| Bitness | 64-bit runtime required going forward; 32-bit CR .NET runtime discontinued after 2025-12-31 |
| Licensing/redistribution | Must be confirmed by application developer before distribution; not assumed by this branch |
| Real PDF proof | Not implemented or claimed here |

## Architecture selection

### Option A — Direct Crystal adapter

Rejected.

Reason: SAP supported-platform material lists .NET Framework versions only, and the FAQ explicitly warns against .NET Core / .NET 5+ for Crystal Reports. Salryn's host is not a .NET Framework application.

### Option B — Isolated Crystal worker

Architecturally preferred for a future runtime lane.

Required shape:

```text
Salryn net10 host
  -> server-owned dataset package
  -> isolated Windows worker targeting SAP-supported .NET Framework/runtime
  -> allow-listed template key and PDF-only output
  -> controlled artifact metadata
  -> artifact hash and source dataset hash
```

### Option C — Study-only stop

Not selected because the repo can safely improve worker contracts without SAP binaries or runtime claims.

### Option D — Worker socket only

Selected.

This branch strengthens the worker protocol and fail-closed output-kind boundary while leaving actual runtime wiring to a later Codex/Sol lane.

## Non-negotiable constraints preserved

- No SAP packages.
- No Crystal binaries.
- No `.rpt` templates.
- No arbitrary template upload.
- No arbitrary SQL.
- No caller filesystem paths.
- No installer changes.
- No printer logic.
- No PDF success claim.
- No production official posting activation.
- No eSales transmission enablement.
- No M5/QA1 reporting/accounting rewrite.

## Runtime proof status

No runtime proof exists in this branch.

The renderer remains unavailable and returns fail-closed errors. Any actual Crystal PDF proof must be executed later by Codex/Sol or a human on a licensed Windows machine with a permitted runtime and an allow-listed template owned by Kirch/Salryn.

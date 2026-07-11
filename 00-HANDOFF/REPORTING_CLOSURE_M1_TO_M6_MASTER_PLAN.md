# Reporting Closure M1 to M6 — Master Plan

Prepared: 2026-07-11

Repository: `Kirch-Nairu/Salryn`

Base branch: `KIRCH-REPORTS-CRYSTAL-M2-RUNTIME-ENVIRONMENT-CHECKLIST`

Candidate branch: `KIRCH-REPORTING-CLOSURE-M1-STRATEGY-AND-PHASE-LOCK`

## Overview

Reporting is the final product-output lane before licensing. The objective is to close reporting with a free, native, Salryn-owned PDF/reporting engine while preserving the existing canonical dataset spine.

Crystal Reports remains optional isolated compatibility only. It is not on the primary release path.

## Global closure rules

These rules apply to M1 through M6:

- Preserve M5 canonical projection logic.
- Preserve QA1 dataset hardening.
- Preserve server-owned dataset generation.
- Preserve permission checks.
- Preserve range and row caps.
- Preserve content/envelope hash semantics.
- Preserve fail-closed renderer behavior until proof exists.
- Preserve Crystal as optional compatibility worker only.
- Do not permit arbitrary templates.
- Do not permit arbitrary SQL.
- Do not permit caller filesystem paths.
- Do not add printer logic.
- Do not touch installer packaging.
- Do not claim PDF proof unless a gate produces and verifies a PDF.

## M1 — Strategy revision and engine lock

### Goal

Lock the reporting strategy before licensing begins.

### Allowed scope

- Create strategy documentation.
- Create M1-M6 phase plan.
- Create done definition.
- Record Crystal as optional isolated compatibility only.
- Record free/native/PDF-first engine as the primary path.
- Name PDFsharp/MigraDoc or equivalent as preferred first candidate pending probe.

### Forbidden scope

- No NuGet packages.
- No renderer code.
- No PDF generation.
- No Crystal implementation.
- No SAP packages.
- No `.rpt` files.
- No templates.
- No printer logic.
- No installer changes.
- No M5/RMO accounting changes.

### Expected files/components

- `00-HANDOFF/REPORTING_CLOSURE_M1_STRATEGY_AND_PHASE_LOCK.md`
- `00-HANDOFF/REPORTING_CLOSURE_M1_TO_M6_MASTER_PLAN.md`
- `00-HANDOFF/REPORTING_ENGINE_DONE_DEFINITION.md`

### Gates

- Docs-only diff.
- Build remains green.
- Existing CoreSmoke remains green.
- Web build remains green.
- No source/package/project file changes.

### Stop conditions

Stop if:

- branch base is not exact;
- protected refs drift;
- docs require implementation decisions not yet approved;
- any package/code/template/installer file is changed.

### Must be true before moving to M2

- Strategy is locked.
- M1-M6 plan exists.
- Done definition exists.
- Crystal is explicitly parked as optional.
- Native PDF engine direction is accepted.

## M2 — Free native PDF package probe

### Goal

Determine whether PDFsharp/MigraDoc or an equivalent free .NET-native PDF engine is acceptable for Salryn's primary reporting engine.

### Allowed scope

- Study candidate package license.
- Study .NET target compatibility.
- Add package reference only if package/license/build compatibility is acceptable.
- Run restore/build probe.
- Produce package probe report.
- Keep any code minimal and compile-only if needed.

### Forbidden scope

- No full renderer implementation.
- No report layout expansion.
- No production PDF claim.
- No Crystal code.
- No SAP packages.
- No templates.
- No printer logic.
- No installer changes.
- No output folder implementation beyond documentation or compile-only shape.
- No M5/RMO accounting changes.

### Expected files/components

Possible files if GO:

- project file package reference for selected native PDF candidate;
- `00-HANDOFF/REPORTING_CLOSURE_M2_FREE_PDF_PACKAGE_PROBE_REPORT.md`;
- optional minimal compile probe class if needed;
- optional focused CoreSmoke check for package presence/license note/build boundary.

### Gates

- `dotnet restore`
- `dotnet build Salryn.sln`
- existing PRE-CRYSTAL/CoreSmoke gates
- package license documented
- no runtime PDF proof claimed

### Stop conditions

Stop if:

- package license is unclear or incompatible;
- package restore fails in a way that is not a local environment problem;
- package does not support Salryn's target/runtime safely;
- package requires paid runtime, native external installer, or hidden dependency burden;
- package pushes the project toward printer/installer work prematurely.

### Must be true before moving to M3

- Primary native PDF candidate is selected.
- License risk is acceptable enough for internal development.
- Restore/build works.
- No unsupported runtime burden appears.
- Kirch/project owner accepts the candidate for skeleton implementation.

## M3 — Native renderer core

### Goal

Implement the minimal native PDF renderer core behind existing renderer contracts without expanding report scope.

### Allowed scope

- Add native PDF renderer abstraction/implementation.
- Keep server-owned dataset input only.
- Add PDF-only output kind.
- Add controlled artifact metadata generation.
- Add controlled output-root abstraction if required.
- Add fail-closed behavior for unsupported reports or missing output policy.
- Add focused CoreSmoke gate for native renderer skeleton.

### Forbidden scope

- No Crystal runtime implementation.
- No `.rpt` files.
- No arbitrary templates.
- No arbitrary SQL.
- No caller filesystem paths.
- No printer logic.
- No installer changes.
- No report family expansion beyond skeleton support.
- No production official posting.
- No eSales transmission.
- No M5/RMO accounting rewrite.

### Expected files/components

Likely files:

- `src/Salryn.Core/Services/Reports/*NativePdf*Contracts.cs` if needed;
- `src/Salryn.Infrastructure/Reports/NativePdfReportRenderer.cs`;
- `src/Salryn.Infrastructure/Reports/NativePdfArtifactWriter.cs` if needed;
- `tests/Salryn.Tests/ReportingClosureM3NativeRendererCoreChecks.cs`;
- CoreSmoke gate entry.

### Gates

- `dotnet build Salryn.sln`
- focused M3 CoreSmoke gate
- existing PRE-CRYSTAL M0 gate
- existing M5-RC2 reporting gate
- full CoreSmoke
- web build if API/UI touched

### Stop conditions

Stop if:

- renderer needs caller-controlled paths;
- renderer requires template execution or SQL;
- package cannot write deterministic PDF from dataset;
- output metadata cannot be computed reliably;
- implementation starts touching M5 accounting/projection logic.

### Must be true before moving to M4

- Native renderer compiles.
- Renderer remains dataset-driven.
- Artifact metadata shape is implemented or stubbed fail-closed.
- No PDF proof is claimed until M4.

## M4 — Core report set proof

### Goal

Generate controlled proof outputs for the core report set from server-owned canonical datasets.

### Allowed scope

Generate and verify controlled proof artifacts for:

- Traditional X reading;
- Traditional Z reading preview;
- EJ export package or PDF/text proof;
- Sales/refund/void summary;
- Tax bucket summary;
- Internal eSales draft, clearly marked internal/unconfirmed.

### Forbidden scope

- No Crystal runtime proof required.
- No printer integration.
- No installer changes.
- No BIR accreditation claim.
- No production eSales transmission.
- No production official posting activation.
- No arbitrary templates.
- No arbitrary SQL.
- No caller output paths.
- No final visual polish requirement for every future report.

### Expected files/components

Likely files:

- renderer layout components for core report families;
- artifact metadata tests;
- proof harness or CoreSmoke-compatible proof command;
- `00-HANDOFF/REPORTING_CLOSURE_M4_CORE_REPORT_SET_PROOF_REPORT.md`;
- sample proof metadata only if safe and non-sensitive.

### Gates

- focused M4 CoreSmoke gate;
- deterministic artifact metadata check;
- output SHA-256 generated;
- dataset content/envelope hashes recorded;
- no DB/template/path/SQL input;
- existing M5/Pre-Crystal gates remain green;
- web build remains green if UI/API touched.

### Stop conditions

Stop if:

- PDF output cannot be generated deterministically enough for artifact metadata;
- any proof requires direct database access from renderer;
- any proof requires uncontrolled filesystem path;
- report totals diverge from canonical projection;
- internal eSales draft begins to look like official transmission.

### Must be true before moving to M5

- Core report outputs exist and are verified.
- Each proof maps to server-owned datasets.
- Artifact metadata includes required hashes.
- Output remains PDF/text proof only, not printer/install packaging.

## M5 — Artifact/export/preview hardening

### Goal

Make reporting artifacts safe for operator use through controlled storage, preview, export, and retention boundaries.

### Allowed scope

- Controlled output root policy.
- Artifact naming policy.
- Artifact metadata ledger/read model.
- Preview/download endpoint hardening.
- Retention/cleanup policy.
- Permission checks for preview/export.
- UI wiring for preview/export if needed.
- Failure-mode tests.

### Forbidden scope

- No caller file paths.
- No arbitrary template upload.
- No arbitrary SQL.
- No printer integration.
- No installer packaging.
- No Crystal runtime requirement.
- No eSales transmission.
- No production posting activation.
- No unbounded artifact storage.

### Expected files/components

Likely files:

- artifact storage service contract/implementation;
- local API artifact endpoints;
- UI preview/export surface;
- tests for permission/path/retention boundaries;
- `00-HANDOFF/REPORTING_CLOSURE_M5_ARTIFACT_EXPORT_PREVIEW_HARDENING_REPORT.md`.

### Gates

- focused M5 CoreSmoke gate;
- path traversal rejection;
- permission rejection;
- metadata integrity check;
- retention/cleanup check;
- full CoreSmoke;
- web build.

### Stop conditions

Stop if:

- output path cannot be controlled;
- previews bypass permissions;
- artifact metadata can be spoofed by caller;
- UI introduces report generation from unsafely supplied inputs;
- storage/retention policy remains ambiguous.

### Must be true before moving to M6

- Artifacts are controlled, discoverable, previewable/exportable as allowed.
- Permission gates remain intact.
- Output root policy is locked.
- Cleanup/retention policy is documented and implemented or explicitly bounded.

## M6 — Final QA and reporting freeze

### Goal

Close reporting as a licensing-ready product surface.

### Allowed scope

- Full regression verification.
- Final reporting freeze report.
- Documentation cleanup.
- Gate matrix.
- Known non-goals list.
- Final support boundary.
- Optional Crystal parking statement.

### Forbidden scope

- No new report families.
- No redesign.
- No Crystal runtime work unless explicitly separated.
- No printer integration.
- No installer packaging.
- No BIR accreditation claim.
- No eSales production transmission.
- No M5/RMO accounting rewrite.
- No visual redesign sprint.

### Expected files/components

Expected files:

- `00-HANDOFF/REPORTING_CLOSURE_M6_FINAL_QA_AND_FREEZE_REPORT.md`;
- final gate matrix;
- final known limitations list;
- release/license handoff note.

### Gates

- `dotnet build Salryn.sln`;
- all reporting-focused CoreSmoke gates;
- existing M5-RC2 reporting gate;
- existing PRE-CRYSTAL gate;
- full CoreSmoke;
- web build;
- repo diff review;
- no protected ref changes;
- no installer branch changes.

### Stop conditions

Stop if:

- any core report family remains unproven;
- artifact policy remains ambiguous;
- PDF generation is not reproducible enough;
- permissions/caps fail;
- UI/build/CoreSmoke fails;
- reporting support claims exceed actual proof.

### Must be true before moving to licensing

- Reporting done definition is satisfied or exception-approved by Kirch.
- Final reporting freeze report exists.
- No partial architecture remains on the primary path.
- Crystal is parked as optional compatibility only.
- Licensing scope can clearly state what reporting includes.

## Licensing handoff rule

Do not start licensing until M6 is complete unless Kirch explicitly accepts a documented exception. If an exception is accepted, it must state:

- which reporting item is deferred;
- why it does not affect licensing truth;
- who owns the follow-up;
- what product claim is forbidden until resolved.

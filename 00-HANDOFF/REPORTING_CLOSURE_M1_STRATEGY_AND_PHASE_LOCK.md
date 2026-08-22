# Reporting Closure M1 — Strategy and Phase Lock

Prepared: 2026-07-11

Repository: `Kirch-Nairu/Salryn`

Base branch: `KIRCH-REPORTS-CRYSTAL-M2-RUNTIME-ENVIRONMENT-CHECKLIST`

Base SHA: `3b8761588e59b73e0b93b9b40e908e6d0fad59da`

Candidate branch: `KIRCH-REPORTING-CLOSURE-M1-STRATEGY-AND-PHASE-LOCK`

## 1. Decision

Reporting must be fully closed before licensing work starts.

No partial reporting architecture should remain when Salryn enters licensing, Salryn Cloud licensing, release packaging, or public release preparation.

The reporting strategy is locked as follows:

1. Crystal Reports is not Salryn's primary reporting engine.
2. Salryn's primary reporting engine is a free, native, Salryn-owned PDF/reporting output engine.
3. The preferred first candidate is PDFsharp/MigraDoc or an equivalent free .NET-native PDF engine, pending a package restore/build/license probe.
4. Crystal remains optional isolated compatibility worker only, disabled by default.
5. Crystal is used only if a client, accountant, government process, or external integration explicitly requires Crystal or `.rpt` compatibility.

## 2. Why reporting closes before licensing

Licensing depends on a stable product surface. Reporting is part of that surface because it affects:

- operator workflow;
- official reading and summary outputs;
- downloadable/exportable artifacts;
- permission gates;
- data integrity evidence;
- release claims;
- support scope;
- future paid-license value.

If reporting remains ambiguous while licensing begins, the licensing phase inherits unresolved technical risk. That would create uncertainty around what the licensed product actually includes.

Reporting must therefore be closed before licensing begins.

## 3. Current repo truth to preserve

The following work remains valid and must not be discarded:

- PRE-CRYSTAL dataset spine;
- QA1 hardening;
- server-owned dataset generation;
- report kind allow-list;
- permission mapping;
- range cap;
- row cap;
- content hash and envelope hash separation;
- artifact metadata boundary;
- output-kind allow-list;
- fail-closed renderer contracts;
- Crystal worker protocol as an optional compatibility lane;
- Crystal-M2 runtime checklist as parking documentation;
- M5 canonical reporting projection;
- M5 X/Z/EJ/eSales lifecycle-aware reporting projection;
- RMO accounting freeze.

The stable asset is not Crystal. The stable asset is the canonical report dataset spine.

## 4. What changes in strategy

Before this lock, Crystal was studied as a potential renderer path. M1 and M2 established that direct Crystal integration is not the correct primary path for Salryn because it creates unnecessary runtime, licensing, installer, platform, and maintenance risk.

After this lock:

```text
Canonical M5 projection / reporting spine
  -> server-owned report dataset
  -> free native PDF/reporting engine
  -> controlled artifact metadata
  -> preview/export/download path
```

Crystal remains parked here:

```text
Canonical M5 projection / reporting spine
  -> server-owned report dataset package
  -> optional isolated Windows Crystal worker
  -> allow-listed template
  -> PDF artifact only if explicitly required and legally/runtime approved
```

Crystal is not on the critical path for licensing.

## 5. Why this is the correct timing

This pivot is low-cost now because none of the high-risk Crystal work has started.

Current state:

- no SAP binaries committed;
- no Crystal runtime package committed;
- no `.rpt` files committed;
- no Crystal installer work started;
- no printer integration started;
- no real Crystal PDF proof claimed;
- no Crystal template policy accepted as production;
- no release claim depends on Crystal;
- report datasets are renderer-neutral;
- artifact policy is still open enough for native PDF.

This is the cleanest point to make the primary-engine decision.

## 6. Why previous Crystal work is not wasted

Previous Crystal work remains useful because it produced:

- evidence that direct in-process Crystal should not be primary;
- an isolated worker boundary that can be reused only when compatibility is required;
- runtime checklist material for future client-specific Crystal needs;
- a fail-closed renderer posture;
- output-kind policy thinking;
- template/input risk documentation.

That work becomes compatibility documentation, not the main product path.

## 7. Primary reporting engine requirements

The primary engine must be:

- free or permissively usable for Salryn's intended deployment model;
- native to modern .NET or otherwise cleanly compatible with the Salryn host;
- controllable by Salryn code without runtime installers if possible;
- able to produce deterministic PDFs from server-owned datasets;
- suitable for offline/local use;
- compatible with future licensing without third-party runtime activation burden;
- maintainable without `.rpt` designer dependency;
- compatible with Salryn artifact metadata and output-root policy;
- safe against caller-provided paths, templates, SQL, or filesystem access.

Preferred first candidate:

```text
PDFsharp/MigraDoc or equivalent free .NET-native PDF engine
```

This is not yet accepted as final until M2 package probe passes.

## 8. Hard boundaries for all reporting closure phases

The following must remain true unless Kirch/project owner explicitly opens a later, separate lane:

- do not touch `KIRCH-SANDBOX`;
- do not touch `KIRCH-INSTALLER-M1`;
- do not rewrite frozen RMO/M5 accounting;
- do not rewrite canonical reporting projection logic;
- do not enable production official posting;
- do not enable eSales transmission;
- do not claim BIR accreditation;
- do not add Crystal as primary;
- do not add SAP packages;
- do not add Crystal binaries;
- do not add `.rpt` files;
- do not add printer logic;
- do not touch installer packaging;
- do not accept caller filesystem paths;
- do not allow arbitrary templates;
- do not allow arbitrary SQL;
- do not claim PDF proof until a gate actually produces and verifies a PDF.

## 9. Reporting closure phases

Reporting closes through these phases:

1. M1 — Strategy revision and engine lock.
2. M2 — Free native PDF package probe.
3. M3 — Native renderer core.
4. M4 — Core report set proof.
5. M5 — Artifact/export/preview hardening.
6. M6 — Final QA and reporting freeze.

Licensing begins only after M6 is complete or Kirch explicitly accepts a documented exception.

## 10. M1 acceptance criteria

M1 is complete when:

- strategy decision is recorded;
- Crystal primary path is explicitly retired;
- native free PDF engine is declared primary direction;
- PDFsharp/MigraDoc or equivalent is identified as first candidate pending probe;
- M1-M6 closure plan exists;
- reporting done definition exists;
- docs-only branch is verified;
- no packages or implementation code are added.

## 11. M1 non-goals

M1 does not:

- add NuGet packages;
- install PDFsharp/MigraDoc;
- add renderer code;
- generate PDF;
- add templates;
- modify APIs;
- modify UI;
- modify installer;
- change M5/RMO accounting;
- change Crystal worker code;
- claim any runtime proof.

## 12. Recommended next branch

Next branch after this lock:

```text
KIRCH-REPORTING-CLOSURE-M2-FREE-PDF-PACKAGE-PROBE
```

Expected M2 work:

- study PDFsharp/MigraDoc package/license/build compatibility;
- add package only if license and restore/build are acceptable;
- no renderer implementation beyond compile/reference proof;
- produce package probe report;
- stop if package licensing or build compatibility is unclear.

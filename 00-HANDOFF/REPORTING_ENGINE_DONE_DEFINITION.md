# Reporting Engine Done Definition

Prepared: 2026-07-11

Repository: `Kirch-Nairu/Salryn`

Candidate branch: `KIRCH-REPORTING-CLOSURE-M1-STRATEGY-AND-PHASE-LOCK`

## Purpose

This document defines what must be true for Salryn reporting to be considered closed before licensing begins.

This is a product-readiness definition, not a BIR accreditation definition and not a printer/installer definition.

## Primary done statement

Reporting is done when Salryn can generate controlled, verified, permission-gated report outputs from server-owned canonical datasets using a free native PDF/reporting engine, with artifact metadata and output policy locked, while Crystal remains parked as optional isolated compatibility only.

## Required strategic state

The following must be true:

- primary free/native PDF engine selected and proven;
- Crystal parked as optional compatibility worker only;
- Crystal disabled by default;
- native PDF renderer implemented;
- controlled PDFs generated from server-owned canonical datasets;
- no direct database rendering path;
- no report-template formulas replacing M5 canonical totals;
- no caller-controlled file paths;
- no arbitrary templates;
- no arbitrary SQL;
- permission checks preserved;
- range caps preserved;
- row caps preserved;
- content/envelope hash semantics preserved;
- artifact metadata locked;
- output root policy locked;
- CoreSmoke gates added;
- web build green;
- final reporting freeze report created.

## Core report families that must be proven

The following report outputs must be proven before reporting is closed:

### 1. Traditional X reading

Must prove:

- generated from canonical reporting projection;
- read-only preview semantics;
- no Z/finalization mutation;
- totals match dataset control totals;
- artifact metadata records dataset hashes;
- permission checks apply;
- range/row caps apply.

### 2. Traditional Z reading preview

Must prove:

- generated from canonical reporting projection;
- preview only;
- no production official posting activation;
- no final Z persistence unless explicitly in the already-frozen M5 path;
- totals match dataset control totals;
- artifact metadata records dataset hashes;
- permission checks apply.

### 3. EJ export package or PDF/text proof

Must prove:

- examiner-readable output path exists;
- original/reprint/void/refund event visibility is preserved from dataset;
- no direct database report query;
- artifact/source hash recorded;
- output can be retrieved/exported by allowed operator role.

PDF is preferred, but text/package proof is acceptable if the EJ route is intentionally not a visual PDF document in this release.

### 4. Sales/refund/void summary

Must prove:

- summary uses canonical M5 projection totals;
- refunds and voids are not recomputed by renderer;
- totals match dataset control totals;
- artifact metadata is recorded;
- output is permission-gated.

### 5. Tax bucket summary

Must prove:

- VAT/non-VAT/tax buckets come from canonical projection totals;
- renderer does not duplicate tax formulas;
- totals match dataset control totals;
- artifact metadata is recorded;
- output is permission-gated.

### 6. Internal eSales draft

Must prove:

- output is clearly marked internal/unconfirmed;
- BIR authoritative schema is not claimed;
- eSales transmission remains disabled;
- artifact metadata is recorded;
- output cannot be mistaken for a submitted/transmitted government filing;
- permission checks apply.

## Artifact metadata done definition

Every generated artifact must record:

- report kind;
- report title;
- dataset content hash;
- dataset envelope hash;
- source hash list where available;
- renderer name;
- renderer version;
- output kind;
- artifact content type;
- output file name or artifact ID;
- byte length;
- output SHA-256;
- created-at timestamp;
- actor/operator identity where safe and available;
- storage/output root policy reference;
- report safety flags or equivalent boundary note.

## Output policy done definition

Output policy is done when:

- output root is controlled by server/host configuration;
- caller cannot provide arbitrary output path;
- caller cannot provide arbitrary file name outside safe naming policy;
- output naming is deterministic enough for audit but collision-safe;
- path traversal is rejected;
- preview/download endpoints enforce permissions;
- retention/cleanup policy is defined;
- failure to write artifact fails closed;
- generated artifact hashes are computed after write;
- artifact metadata cannot be supplied or spoofed by caller.

## Security done definition

Reporting security is done when:

- report kind allow-list is enforced;
- output kind allow-list is enforced;
- permission checks are enforced per report family;
- server-owned dataset generation is preserved;
- no client-supplied full dataset is trusted for rendering;
- no arbitrary template upload exists;
- no arbitrary SQL is accepted;
- no caller filesystem path is accepted;
- range cap is enforced;
- row cap is enforced;
- unsupported reports return safe unavailable responses;
- renderer failures do not create partial success claims;
- all official/BIR/eSales safety flags remain honest.

## Required gates before reporting freeze

Required gates:

```text
dotnet build Salryn.sln
focused native PDF package/renderer gates
focused core report proof gate
focused artifact/export/preview hardening gate
reports-precrystal-m0-full-reporting-spine
rmo-bir-trad-m5-rc2-xz-ej-esales-completion
full CoreSmoke
npm --prefix src/Salryn.App.Web run build
```

Additional required evidence:

- diff review confirms no protected refs touched;
- diff review confirms no installer packaging touched;
- diff review confirms no SAP/Crystal runtime was made primary;
- final reporting freeze report exists;
- known limitations are listed.

## Final reporting freeze report must include

The final M6 freeze report must include:

- final branch and SHA;
- base branch and SHA;
- changed files;
- selected PDF engine and version;
- package license summary;
- supported report families;
- artifact policy summary;
- output root policy summary;
- gates run and results;
- known limitations;
- explicit non-goals;
- licensing handoff statement;
- statement that Crystal remains optional compatibility only.

## Not required before licensing

The following are not required before licensing:

- Crystal runtime proof;
- Crystal worker implementation;
- `.rpt` compatibility proof;
- printer integration;
- installer packaging;
- BIR accreditation;
- production eSales transmission;
- production official posting activation;
- final visual polish for every report;
- every future analytics report;
- inventory/stock movement reports unless specifically promoted into release scope;
- accountant-specific Crystal templates;
- government submission automation.

These can be later lanes if needed. They must not block licensing unless Kirch explicitly reclassifies one as release-critical.

## Licensing handoff readiness

Reporting is licensing-ready when the product can honestly say:

```text
Salryn includes native, controlled PDF/reporting outputs for the core local reporting set, generated from server-owned canonical datasets with permission gates, artifact metadata, and safe output policy. Crystal Reports compatibility is optional and disabled by default.
```

Reporting is not licensing-ready if any of these statements would be false.

## Explicit exclusions from product claims

Do not claim:

- BIR accreditation;
- eSales production transmission;
- Crystal compatibility;
- printer support;
- installer-integrated report runtime;
- every analytics report family;
- accountant-approved official filing outputs;
- legal/tax compliance approval.

Only claim what gates and human proof actually validate.

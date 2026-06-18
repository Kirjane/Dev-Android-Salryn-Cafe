# J21-1 - RMO 24-2023 Compliance Matrix and Guardrails

Status: compliance planning guardrail only.

## Scope

This commit introduces the repo-level RMO 24-2023 compliance matrix for Salryn.

No schema change.
No migration.
No app run.
No UI change.
No production compliance claim.
No stable QA branch change.
No multi-tenant work.

## Why This Comes Before Multi-Tenant

Multi-tenant work affects how store, branch, taxpayer identity, machine profile, receipt sequence, journals, reports, and PTU/accreditation metadata are owned.

Building host/client multi-tenant before these boundaries would risk redesigning compliance-critical data.

## Source of Truth

Compliance matrix:

00-COMPLIANCE/RMO_24_2023_COMPLIANCE_MATRIX.md

## Guardrail Added

Core smoke now checks that the compliance matrix exists and contains the required RMO control sections before future implementation work.

## Current Result

Salryn remains a QA-ready local/offline POS MVP.

Salryn is not yet BIR/RMO 24-2023 accreditation-ready.

J21-2 must begin with machine/POS registration profile planning and schema, not UI polish.
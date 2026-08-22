# J21-3B - RMO Official Receipt/Invoice Sequence Allocation Service

Status: allocation service only.

## Scope

This commit adds the official sequence allocation service for RMO-24-2023-C02.

The service advances bir_official_document_sequences.current_number transactionally and returns deterministic padded sequence values.

## RMO Control

- RMO-24-2023-C02 Official receipt/invoice sequence

## Added

- New service: OfficialDocumentSequenceAllocationService
- New request/response contracts for allocation
- Machine/profile readiness gate before allocation
- Transactional current_number advancement
- Sequence exhaustion handling
- Source/runtime guard checks
- CoreSmoke registration for J21-3B
- Compliance matrix J21-3B section
- J21-3B handoff

## Allocation Gate

Allocation requires:

- bir_machine_profiles.id matches the requested profile
- bir_machine_profiles.is_active = 1
- bir_machine_profiles.compliance_mode = ACCREDITED
- bir_machine_profiles.official_output_enabled = 1
- bir_official_document_sequences.is_active = 1
- bir_official_document_sequences.is_exhausted = 0

This prevents dev/QA or not-accredited profiles from consuming official sequence ranges.

## Transaction Behavior

The service:

- reads the active sequence row
- computes nextNumber = current_number + 1
- blocks overflow beyond end_number
- formats through ReceiptNumber.Create for deterministic padding
- updates current_number inside the same SQLite transaction
- guards the update with the previous current_number
- marks the sequence exhausted/inactive when the last number is consumed

## Not Added

- No checkout flow wiring.
- No UI.
- No receipt printing.
- No receipt snapshot generation.
- No official receipt/invoice rendering.
- No electronic journal.
- No X-reading/Z-reading.
- No multi-tenant work.
- No accreditation readiness claim.

## Next

J21-3C or J21-4 must decide how allocated official sequence values become part of receipt snapshots.

Do not wire allocation into checkout until the receipt snapshot generator, machine/profile header fields, electronic journal plan, and official-output gate are ready together.
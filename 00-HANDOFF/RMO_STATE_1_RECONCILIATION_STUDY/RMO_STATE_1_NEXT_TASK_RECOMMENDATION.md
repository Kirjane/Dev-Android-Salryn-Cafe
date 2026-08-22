# RMO-STATE-1 Next-Task Recommendation

Recommendation only. This is not an implementation prompt. No work should begin until the study report and diagrams are reviewed.

## Recommended Next Task Title

**DATA-A1 / RMO-B1 — Official Checkout Data-Integrity Current-State Reconciliation Seal**

## Proposed Scope

A documentation-and-guard seal (modeled on the existing `RMO_A7_INTERNAL_OFFICIAL_OUTPUT_TEXT_FILE_WORKFLOW_SEAL.md`) that records the **already-implemented** single-machine official-checkout data spine as current internal engineering truth and reconciles it against the drifted compliance matrix. Specifically:

- A new seal document describing the implemented spine: fail-closed readiness → shared SQLite transaction → official sequence allocation → sale/items/payments/stock write → ORIGINAL electronic-journal append, with commit/rollback together.
- Explicit enumeration of the single-machine invariants that hold today (atomic commit/rollback; one active sequence per profile/kind/scope/year; append-only journal with single ORIGINAL; Admin-only `OfficialOutput.Run`; fail-closed official mode dormant without an accredited profile).
- A reconciliation note that the matrix lines stating "no checkout wiring / no X-Z / no reprint / no renderer" are superseded by current source, citing the relevant files and smokes.
- Source/smoke guard(s) asserting the seal's claims stay true (in the style of the existing `RmoA*`/`J21Rmo242023*` checks), with **no new runtime behavior**.

## Forbidden Scope

- No runtime behavior change to checkout, persistence, journal, or output.
- No database schema or migration change.
- No LAN / multi-terminal work.
- No printer / PDF / Crystal / ESC-POS / scanner.
- No Web/API official-output exposure.
- No UI exposure of reprint, void/refund/cancel, X-reading, or Z-reading.
- No certification / accreditation / legal claims.
- No branch promotion (`KIRCH-FREEHAND` must not be promoted to Sandbox).
- No launching `Salryn.App.WinForms`.

## Verification Commands

```
git status --short
git diff --check
dotnet build .\Salryn.sln --no-restore
dotnet test .\Salryn.sln --no-build
dotnet run --no-build --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- pos-phase-b-regression
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- official-output-text-file
```

## Stop Conditions

- Stop if the worktree is dirty for any reason other than the intended seal/guard artifacts.
- Stop if HEAD is not the reviewed stable SHA or if `KIRCH-FREEHAND` diverges from `KIRCH-SANDBOX` unexpectedly.
- Stop if any proposed change would alter runtime behavior, schema, or add any forbidden capability above.
- Stop if build, tests, or the listed smokes fail.
- Stop and request review before committing or promoting anything.

## Why This Is Not LAN Yet

LAN/multi-terminal is the correct **outer** layer, but it is unsafe to start before the single-machine spine is sealed. The current sequence allocation uses single-process optimistic concurrency and the readiness check uses a separate connection from the allocation transaction — correct for one machine, insufficient across terminals. LAN would additionally require terminal identity in journal/audit, a clock/order policy, and cross-process sequence ownership. Sealing the single-machine invariants first gives LAN a verified, documented foundation to build on and prevents a premature concurrency design. The reconciliation seal also removes the documentation drift that would otherwise make any LAN claim rest on inaccurate compliance text.

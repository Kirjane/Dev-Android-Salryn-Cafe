# KIRCH-SANDBOX Target Architecture

Date: 2026-06-24
Scope: target architecture planning only.

## Architecture Position

Salryn should remain a modular monolith for the first production-ready release, with a controlled Store Host boundary. That means one codebase, clear layers, and no premature cloud architecture.

The target system is:

- native Windows desktop
- local-first
- SQLite owned by the Store Host only
- WinForms for operator UI
- Core for business rules and contracts
- Infrastructure for persistence, printing, reports, backup, and adapters
- Host boundary for future LAN clients

## Layer Ownership

### Salryn.App.WinForms

Owns:

- screens
- presenters
- view models
- keyboard shortcuts
- module routing
- startup mode selection
- setup wizard UI
- operator feedback

Must not own:

- SQL
- receipt numbering truth
- sale totals truth
- stock movement truth
- official document truth
- backup file mutation rules
- direct LAN protocol rules

### Salryn.Core

Owns:

- business models
- value objects
- service contracts
- checkout rules
- receipt and official document contracts
- Host request/response contracts
- RMO validation rules
- permission names
- result/error model

### Salryn.Infrastructure

Owns:

- SQLite connection and migrations
- repositories
- transaction boundaries
- password hashing
- print adapters
- report exporters
- backup/restore implementation
- file paths
- device adapters
- electronic journal persistence adapters

### Salryn.Host.Local

Current role:

- in-process Store Host runtime
- request validation
- handler registry
- dispatch
- product lookup handler

Target role:

- shared Host runtime logic that can run in standalone mode or be wrapped by a real host process/transport.

Must not become:

- a direct UI layer
- a direct Infrastructure dependency from Core
- a networked database share

### Future Host Process

Needed for true Host/Receiver mode:

- local service or process owned by the Host machine
- opens SQLite only on the Host
- exposes authenticated LAN operations
- serializes write operations
- owns official receipt number allocation
- owns backups
- owns printer where configured
- returns clean contract errors to clients

## Target Deployment Modes

### Mode 1: Standalone Host

One PC owns:

- UI
- database
- product lookup
- checkout
- reports
- backups
- printer

This is the first production mode.

### Mode 2: Cashier PC As Host

Main cashier PC owns the database and Host runtime. Other terminals connect over LAN.

Required before release:

- terminal registry
- pairing
- host status UI
- manual IP fallback
- host unavailable messages

### Mode 3: Admin PC As Host

Back-office/admin PC owns database and Host runtime. Cashier terminals connect to it.

Extra risk:

- cashier cannot operate if admin PC is off.
- needs strong setup guidance.

### Mode 4: Dedicated Store Host

Mini PC or server box owns database and Host runtime.

This is later, not needed for the first production release.

## Runtime Flow

Startup should become:

1. Resolve launch mode.
2. Load persisted deployment profile.
3. If no profile exists, show setup wizard.
4. If Host mode, initialize DB and start local host runtime.
5. If Receiver mode, connect to Host and do not open local SQLite.
6. Authenticate user.
7. Route modules by role and machine capability.

Current issue:

- `SalrynAppServices` hard-codes standalone profile identities.
- Runtime starts in-process during app service composition.
- There is no persisted store/terminal identity yet.

Target correction:

- introduce `IDeploymentProfileRepository`
- introduce setup wizard output model
- introduce host/client launch modes
- stop hard-coded GUIDs from reaching production behavior

## Checkout Architecture

Current flow:

```text
CheckoutShellForm
CheckoutPresenter
ICheckoutProductSearchBoundary
LocalHostProductLookupGatewayAdapter
LocalHostRuntime.Dispatch
ProductLookupHandler
IProductReadRepository
```

Target sale completion flow:

```text
CheckoutShellForm
CheckoutPresenter
ICheckoutCompletionBoundary
PosCheckoutApplicationService
IProductReadRepository
IReceiptSequenceService
IReceiptSnapshotService
IPosCheckoutService
ISaleWriteRepository
IPrintJobRepository
IAuditLogWriter
```

Official mode later adds:

```text
IBirMachineProfileReadRepository
IOfficialDocumentSequenceAllocator
IOfficialDocumentSnapshotService
IElectronicJournalAppendService
IXZReadingService
```

## Host Operation Surface

Initial operations to finish:

- `auth.login`
- `products.lookup`
- `products.search`
- `cashierSession.open`
- `cashierSession.close`
- `checkout.completeSale`
- `reports.dailySalesSummary`
- `backup.health`

Do not add LAN transport until each operation has:

- Core request/response contract
- service handler
- permission validation
- smoke test
- failure mapping
- idempotency or duplicate-submit policy where needed

## Transaction Ownership

Critical transactions:

- checkout complete
- void sale
- official sequence allocation plus journal append
- stock-in
- stock adjustment
- backup restore metadata
- Z-reading close

Rule:

- UI never opens transactions.
- Host handler or application service starts the transaction.
- Infrastructure repositories participate through explicit transaction boundaries.

## Compliance-Sensitive Official Checkout Target

Official checkout must be one atomic unit:

1. Validate cashier session and permissions.
2. Re-read product snapshots.
3. Validate stock and discounts.
4. Build sale.
5. Allocate official document number from active sequence.
6. Build official snapshot.
7. Hash snapshot.
8. Insert sale and items.
9. Insert payments.
10. Insert stock movements.
11. Update product stock.
12. Link sale receipt snapshot.
13. Append electronic journal entry.
14. Append ORIGINAL event.
15. Insert print job.
16. Commit.
17. Print after commit.
18. Update print job status.

Rollback rule:

- official number and journal evidence must not drift from sale persistence.

## Security And Identity

Required identities:

- tenant id
- store id
- host id
- terminal id
- device key
- user id
- role id
- cashier session id where needed

Current state:

- request context provider can carry these identities.
- production app still uses hard-coded standalone store/terminal values.

Target state:

- identities are persisted during setup and never invented by adapters.
- receiver terminals are paired and revocable.
- Host rejects commands without registered terminal identity.

## Architecture Exit Criteria

The architecture is production-ready only when:

- standalone mode has no hard-coded production identities.
- client mode cannot directly open SQLite.
- checkout completion is service-owned and tested against tampering.
- receipt sequence allocation is repository-backed.
- backup and restore are implemented through Infrastructure.
- RMO official mode is gated and cannot be accidentally enabled.
- all real workflows have smoke coverage, not only compile checks.

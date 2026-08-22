# Phase 61 - Adaptive Store Host Architecture Proposal

Status: Proposal only.

Project: SALRYN POS by Kirjane Labs / Kirch-Nairu

Branch: KIRCH-APP-SHELL-PERMISSION-ROUTING

Previous sealed checkpoint: Phase 60B-1 - Product Detail and Edit Logic Foundation

Field node note:
This document was prepared from a field node clone, not the primary workstation. Local database contents, previous app data, restored tools, and primary workstation state must not be assumed.

Build note:
The field node only has .NET SDK 8.0.422 installed. The repository targets .NET 10, so build verification is blocked by SDK mismatch on this machine. This is treated as an environment blocker, not proof of code failure.

---

## Purpose

Phase 61 defines the proposed Adaptive Store Host Architecture for SALRYN.

This is architecture and documentation only.

This phase does not continue frontend work, does not continue Product Edit UI work, does not implement Salryn.Host, and does not create new projects.

---

## Why SALRYN Needs Adaptive Store Host Architecture

SALRYN is planned as an offline-first MSME POS platform.

It must start affordably on one Windows PC, then later scale to LAN multi-terminal operation without forcing a dedicated server PC.

The architecture must support real client setups:

- a single small-store computer
- a cashier PC acting as the host
- an admin or back-office PC acting as the host
- an optional dedicated mini PC or server machine for larger clients

The main rule is simple:

Any capable Windows PC can become the Store Host depending on the client setup.

The Store Host owns the database.

All other devices talk to the Store Host through LAN/API contracts.

---

## Store Host Rule

Only the Store Host process may open the SQLite database file.

Clients must never directly open salryn.db.

Clients must never write SQLite directly.

Clients must send commands and queries to the Store Host.

The Store Host owns truth for sales, inventory, cashier sessions, users, roles, receipts, reports, backups, and audit logs.

---

## Why Clients Must Not Share or Open SQLite Directly

SQLite is strong for local embedded storage.

The unsafe design is placing one SQLite database file in a shared LAN folder and letting multiple computers open it directly.

That can create problems with file locking, network latency, interrupted writes, Windows share behavior, and corruption risk.

The safer design is:

- the SQLite database stays local to one Store Host machine
- one Host process opens SQLite
- all other clients communicate with that Host process
- critical writes are controlled by the Host
- receipt numbers are generated server-side
- audit logs are written centrally

This keeps SQLite local while still allowing future LAN clients.

---

## Real-World MSME Deployment Modes

### 1. Standalone Host

One PC only.

The same PC is cashier, admin, Store Host, database holder, and backup machine.

Best for very small stores.

### 2. Cashier-PC-as-Host

The main cashier PC acts as the Store Host.

Other cashier or admin PCs connect through LAN.

Best when the cashier PC is always powered on during store operations.

### 3. Admin-PC-as-Host

The owner, admin, or back-office PC acts as the Store Host.

Cashier PCs connect through LAN.

Best when the owner wants the main database away from the cashier counter.

### 4. Dedicated Store Host

Optional only.

A mini PC, back-office PC, or small server machine owns the database.

Cashier and admin clients connect through LAN.

Best for larger clients, but this must not be required for basic clients.

---

## Real-World Affordability Rule

SALRYN must not force clients to buy a dedicated server PC.

Dedicated Store Host mode is optional.

The system must adapt to available client hardware.

This can become a unique selling point for Kirjane Labs because the product can grow with the business instead of forcing expensive infrastructure on day one.

---

## Machine Roles

- Standalone Host
- Cashier Host
- Admin Host
- Dedicated Host
- Cashier Client
- Admin Client
- Future Mobile Dashboard Client

---

## Store Host Responsibilities

The Store Host:

- owns the SQLite database
- runs the local SALRYN Host service or process
- authenticates users
- enforces roles and permissions
- manages cashier sessions
- generates receipt numbers server-side
- handles sales writes
- handles inventory writes
- handles product and catalog writes
- handles reports
- handles backups
- writes audit logs
- registers terminals and devices
- later exposes read-only mobile dashboard snapshots

---

## Client Responsibilities

Clients are UI devices.

Clients:

- display screens
- capture input
- send commands and queries to the Store Host
- include authenticated user identity in requests
- include cashier session identity in cashier actions
- include terminal identity in requests
- handle disconnected Host state cleanly
- never directly open the database
- never write SQLite directly
- never generate final receipt numbers locally in LAN mode

---

## Offline-First Behavior

Internet must not be required for:

- selling
- checkout
- product lookup
- inventory
- users
- roles
- cashier sessions
- receipt records
- reports
- local backup

Internet is optional for:

- encrypted cloud backup
- update checks
- license or support validation
- maintainer sync
- future remote dashboard or cloud mirror

---

## LAN Behavior

LAN mode means clients connect to the Store Host over the local network.

LAN mode should support:

- Host IP or hostname configuration
- manual IP fallback
- optional LAN discovery later
- terminal pairing
- device keys
- clear disconnected state
- warning if the Host PC is off
- firewall diagnostics

LAN mode must not require internet.

---

## Internet-Optional Behavior

SALRYN must remain usable without internet.

Online features must not block local selling.

If cloud backup, support validation, update checks, or remote dashboard features are unavailable, local POS operations must continue.

---

## Terminal Identity Model

Future Host-aware identity should include:

- tenant_id
- store_id
- host_id
- terminal_id
- device_key
- created_at
- updated_at

The terminal_id identifies the registered machine.

The device_key helps prove that the machine was paired with the Store Host.

The host_id identifies which machine currently owns the Store Host role.

---

## User, Session, and Audit Identity Model

Future Host-aware requests should carry:

- tenant_id
- store_id
- user_id
- role_id
- cashier_session_id
- terminal_id
- request_time

This allows the Host to enforce permissions, trace cashier actions, prevent receipt conflicts, and write meaningful audit logs.

---

## Backup Strategy

Primary backup should be an encrypted SQLite database backup package.

JSON should be secondary only for summaries, reports, migration helpers, support diagnostics, extractor utilities, and manifests.

Raw readable JSON uploads must not be the main backup strategy.

A backup package should contain:

- encrypted database snapshot
- manifest JSON
- tenant_id
- store_id
- backup_id
- backup_time
- app_version
- schema_version
- checksum
- backup_type
- machine or host identity
- backup health result

Any Kirjane Labs cloud backup must be:

- encrypted
- tenant-separated
- versioned
- restorable
- client-consented
- checksum-verified

---

## Future Mobile Readiness

Do not build the mobile app now.

Do not make mobile the center of Phase 61.

The architecture should only anticipate a future native owner dashboard app.

Mobile V1 must be read-only.

Inside the same LAN, mobile can fetch current summary data from the Store Host.

Outside LAN, mobile should show the last fetched read-only snapshot with a clear last updated timestamp.

Do not promise live remote monitoring unless VPN, cloud mirror, or another approved remote channel exists later.

Mobile V1 may show:

- daily sales summary
- cashier totals
- low-stock alerts
- top products
- gross and net sales summary
- inventory alerts
- backup health
- last fetch timestamp

Mobile V1 must not perform writes.

---

## Future Project Shape

Potential future solution shape:

- Salryn.Core
- Salryn.Infrastructure
- Salryn.App.WinForms
- Salryn.Host.Local
- Salryn.Shared.Contracts
- Salryn.Backup
- Salryn.Sync
- Salryn.Mobile.Owner

Do not create these projects in Phase 61.

---

## Risks and Mitigations

| Risk | Mitigation |
| --- | --- |
| SQLite corruption from shared database file access | Store Host owns SQLite and clients use Host contracts |
| Windows firewall and LAN setup pain | Add diagnostics screen, manual IP fallback, and optional discovery later |
| Host PC shutdown | Show clear disconnected state and document Host machine responsibility |
| Receipt number conflicts | Generate receipt numbers server-side on the Host |
| Backup privacy and liability | Use encrypted, tenant-separated, checksum-verified backups |
| Support burden | Keep standalone stable before LAN mode |
| Scope creep | Do not build mobile, cloud SaaS, sync, or Host projects yet |
| Fake live remote monitoring promise | Mobile V1 is read-only and uses last fetched snapshots outside LAN |

---

## Phased Architecture Roadmap

### Phase 61A - Architecture Proposal Doc

Create this document.

No implementation.

### Phase 61B - Machine Role and Deployment Mode Model

Define machine role concepts and deployment modes.

No Host process yet.

### Phase 61C - Host and Client Contract Boundary Plan

Plan command and query contracts that future clients will use.

Do not move existing code yet.

### Phase 61D - Backup and Mobile Snapshot Readiness Plan

Plan encrypted backup package metadata and read-only dashboard snapshots.

No backup engine yet.

---

## Future Implementation Roadmap

- Phase 62 - Host Boundary Preparation
- Phase 63 - Local Store Host MVP
- Phase 64 - WinForms connects to local Host over loopback
- Phase 65 - Terminal Registry and Pairing
- Phase 66 - Backup Engine
- Phase 67 - LAN Client Mode
- Phase 68 - Mobile Snapshot Readiness

---

## Complexity Rating

| Scope | Complexity |
| --- | --- |
| Basic standalone POS | 4/10 |
| Standalone POS with inventory, users, and reports | 5.5/10 |
| LAN multi-cashier adaptive host | 7.5/10 |
| Backups, terminal registry, and audit logs | 8/10 |
| Future mobile snapshot dashboard | 8.5/10 |
| National multi-client and multi-tenant support | 9/10 |

---

## What Not To Build Yet

Do not build Salryn.Host.Local yet.

Do not build mobile yet.

Do not build sync yet.

Do not build cloud SaaS yet.

Do not create new projects yet.

Do not convert the current WinForms app to LAN mode yet.

Do not share SQLite over LAN.

Do not promise live remote monitoring without VPN or cloud mirror.

---

## Immediate Next Development Recommendation

After this document is reviewed, continue with Phase 61B.

Phase 61B should define machine roles and deployment modes as planning or lightweight Core concepts only.

Do not implement Host networking yet.

Do not resume Product Edit UI until the architecture pause is accepted or explicitly skipped.

---

## Phase 61A Seal Criteria

Phase 61A is sealed when:

- this proposal exists in 00-HANDOFF
- no frontend code was changed
- no Host project was created
- no database content was assumed
- field node SDK mismatch is documented
- git status only shows this markdown file before commit
- the document is committed and pushed after review

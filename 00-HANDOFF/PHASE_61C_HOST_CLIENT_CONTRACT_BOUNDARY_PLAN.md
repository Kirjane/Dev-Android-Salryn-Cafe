# Phase 61C - Host and Client Contract Boundary Plan

Status: Implementation checkpoint.

Purpose:
Add lightweight Core contract shapes for future Host/client communication without creating a Host project, transport layer, networking layer, or frontend changes.

Implemented:
- Added HostOperationKind
- Added HostRequestContext
- Added HostRequestEnvelope<TPayload>
- Added HostContractError
- Added HostResponseEnvelope<TPayload>
- Added HostClientContractRules
- Added HostOperationNames
- Added compile check for Host/client contract boundary

Architecture rule preserved:
- Core owns contract models and rules
- Infrastructure still owns SQLite
- WinForms UI was not changed
- No Host project was created
- No HTTP, socket, LAN discovery, or service transport was implemented
- No Product Edit UI work was resumed

Contract direction:
- Clients will send commands or queries through HostRequestEnvelope<TPayload>
- The Host will return HostResponseEnvelope<TPayload>
- Requests will carry tenant, store, user, role, cashier session, terminal, and device identity
- Commands require authenticated user and terminal identity
- Cashier-bound operations require cashier session identity

Important:
This phase defines the boundary shape only. It does not change runtime behavior.

Next:
Phase 61D - Backup and Mobile Snapshot Readiness Plan

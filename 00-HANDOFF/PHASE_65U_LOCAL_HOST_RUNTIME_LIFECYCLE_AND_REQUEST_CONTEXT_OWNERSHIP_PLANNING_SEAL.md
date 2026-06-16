# Phase 65U - Local Host Runtime Lifecycle And Request Context Ownership Planning Seal

Status: Planning seal only.

## Scope

Seal the ownership decision for future Local Host runtime lifecycle activation and HostRequestContext creation before any runtime start is added.

This phase is intentionally tests, handoff, and CoreSmoke registration only.

No production source was changed.

## Decision

Runtime activation must not belong to the adapter.

The adapter is a request bridge. It may dispatch through the held runtime bridge, but it must not start, stop, restart, recover, or own the runtime lifecycle.

Runtime activation must not belong to the passive holder.

The passive holder owns a validated stopped Local Host composition result. It must remain a passive holder for guarded composition, not a lifecycle controller.

A separate lifecycle holder or lifecycle service should own Start and Stop later.

That future lifecycle object should live under App.WinForms Composition and be owned by SalrynAppServices, but it must remain separate from the adapter and passive holder.

A separate request context provider should own HostRequestContext creation later.

That future context provider should be responsible for TenantId, StoreId, TerminalId, DeviceKey, UserId, RoleId, CashierSessionId, and RequestedAt. The adapter must not keep inventing identity values.

## Findings sealed

- LocalHostRuntime.Start exists, but production code does not own it yet.
- LocalHostRuntime.DispatchAsync remains gated by Running state.
- LocalHostBootstrapCompositionRules requires the composition snapshot to be stopped.
- SalrynLocalHostCompositionHolder validates stopped composition and remains passive.
- ProductLookup requires store identity, terminal identity, and authenticated user identity.
- Current AppServices standalone profile has store and host identity, but Terminal is null.
- Current adapter placeholder context remains a temporary stopped-runtime bridge detail only.

## Rejected

- No runtime start was added.
- No runtime stop was added.
- No runtime lifecycle object was added.
- No request context provider was added.
- No adapter lifecycle behavior was added.
- No passive holder lifecycle behavior was added.
- No AppServices lifecycle activation behavior was added.
- No Program startup behavior was changed.
- No Shell routing behavior was changed.
- No checkout UI behavior was changed.
- No product management UI behavior was changed.
- No transport was implemented.
- No LAN behavior was implemented.
- No two-machine behavior was implemented.
- No direct SQLite access was added outside existing infrastructure paths.
- No production source was changed.

## Future order

Phase 65V may add a Local Host lifecycle holder or lifecycle service shell under App.WinForms Composition, but it must not start runtime yet.

Phase 65W may add a Host request context provider shell under App.WinForms Composition, but it must not wire fake identity into production behavior.

Phase 65X may wire lifecycle and context objects into AppServices as passive dependencies.

A later explicit phase may activate runtime only after lifecycle ownership and request context ownership are both sealed.
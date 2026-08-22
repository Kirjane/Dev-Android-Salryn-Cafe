# Phase 65X - Passive Local Host Providers AppServices Wiring

Status: Combined passive wiring only.

## Scope

Add the passive Host request context provider and wire both passive Local Host ownership providers into AppServices.

This phase intentionally combines the provider shell and AppServices passive wiring to avoid another tiny shell-only marathon.

## Added

- Added SalrynLocalHostRequestContextProvider under App.WinForms Composition.
- The provider accepts StoreDeploymentProfile.
- The provider exposes store identity readiness.
- The provider exposes terminal identity readiness.
- The provider can create HostRequestContext only through a guarded TryCreateAuthenticatedContext method.
- The provider requires supplied user id and role id.
- The provider uses deployment terminal identity only when present.
- The provider does not invent terminal, user, role, or cashier session identity.
- AppServices now passively owns SalrynLocalHostRuntimeLifecycleHolder.
- AppServices now passively owns SalrynLocalHostRequestContextProvider.

## Preserved boundaries

- No runtime activation was added.
- No runtime stop was added.
- No runtime restart was added.
- No recovery behavior was added.
- No adapter consumption of the request context provider was added.
- No ProductLookup UI behavior was changed.
- No checkout behavior was changed.
- No product management behavior was changed.
- No Program or Shell behavior was changed.
- No transport was added.
- No LAN behavior was added.
- No two-machine behavior was added.
- No direct SQLite access was added outside Infrastructure.

## Current limitation

The current standalone AppServices profile still has Terminal: null.

CurrentAppSession also carries UserId and RoleName, but not RoleId.

Because ProductLookup requires store identity, terminal identity, and authenticated user identity, the provider must not be consumed by the adapter until real terminal and role identity sources are explicitly wired.

## Future order

A later explicit phase may add terminal identity source wiring.

A later explicit phase may add RoleId into the app session boundary.

A later explicit phase may replace the adapter placeholder context with the provider.

Runtime activation must remain blocked until lifecycle ownership and request context ownership are both usable with real identity.
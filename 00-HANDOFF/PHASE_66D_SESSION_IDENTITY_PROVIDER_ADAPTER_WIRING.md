# Phase 66D - Session Identity Provider Adapter Wiring

Status: Session identity provider wiring only.

## Scope

Wire the existing CurrentAppSession into Local Host ProductLookup request context creation through a passive session identity provider.

This phase does not activate runtime.

## Added

- Added SalrynLocalHostSessionIdentityProvider.
- The session identity provider can receive the current shell session passively.
- The session identity provider exposes fail-closed authenticated identity retrieval.
- AppServices now owns SalrynLocalHostSessionIdentityProvider.
- AppServices now passes the session identity provider into the ProductLookup adapter.
- SalrynShellForm now publishes the existing CurrentAppSession into the passive identity provider.
- The adapter now uses session UserId and RoleId when asking SalrynLocalHostRequestContextProvider to create HostRequestContext.
- The adapter now fails closed when no active session identity is available.

## Preserved boundaries

- No runtime activation was added.
- No runtime stop was added.
- No runtime restart was added.
- No Program behavior was changed.
- No Shell routing behavior was changed.
- Shell only publishes the existing CurrentAppSession into the passive identity provider.
- No ProductLookup UI behavior was changed.
- No checkout behavior was changed.
- No product management behavior was changed.
- No transport was added.
- No LAN behavior was added.
- No two-machine behavior was added.
- No direct SQLite access was added outside Infrastructure.

## Current state after this phase

The ProductLookup adapter no longer uses null user and role identity.

The ProductLookup adapter now asks the passive session identity provider for UserId and RoleId.

If the active app session is missing, inactive, or has Guid.Empty identity, ProductLookup fails closed as LocalHostUnavailable.

Runtime remains stopped until an explicit runtime lifecycle activation phase.
# Phase 65Z - Identity Boundary Wiring

Status: Identity boundary wiring only.

## Scope

Wire the missing identity pieces needed before the Local Host request context provider can become usable.

This phase does not activate runtime and does not make the adapter consume the provider yet.

## Added

- Added RoleId to UserPermissionReadModel.
- Preserved compatibility constructor with Guid.Empty only as explicit missing identity compatibility.
- Updated SqliteUserPermissionReadRepository to select users.role_id.
- Updated SqliteUserPermissionReadRepository to map RoleId into UserPermissionReadModel.
- Added RoleId to CurrentAppSession.
- Updated AppSessionBootstrapper to carry profile.RoleId into CurrentAppSession.
- Added deterministic standalone TerminalIdentity inside AppServices standalone profile.
- Kept terminal identity local to the standalone profile only.

## Preserved boundaries

- No runtime activation was added.
- No runtime stop was added.
- No runtime restart was added.
- No adapter consumption of the request context provider was added.
- No ProductLookup UI behavior was changed.
- No checkout behavior was changed.
- No product management behavior was changed.
- No Program or Shell behavior was changed.
- No transport was added.
- No LAN behavior was added.
- No two-machine behavior was added.
- No direct SQLite access was added outside Infrastructure.

## Current state after this phase

AppServices now has a StoreDeploymentProfile with store identity and standalone terminal identity.

CurrentAppSession can now carry UserId and RoleId.

SalrynLocalHostRequestContextProvider can now create an authenticated HostRequestContext when supplied UserId and RoleId.

The adapter still uses the old placeholder context and must not consume the provider until an explicit adapter-context consumption phase.

Runtime activation remains blocked until adapter context replacement is explicitly approved.
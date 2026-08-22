# Phase 66G0 - Authenticated RoleId Propagation

Status: Authenticated RoleId propagation only.

## Scope

Fix the authentication-to-shell identity path so the Local Host session identity provider can receive a real RoleId from normal login and first-run admin setup.

This phase does not activate runtime.

## Added / changed

- AuthenticatedUserReadModel now carries RoleId.
- AuthenticationService.LoginAsync now propagates RoleId from UserPermissionReadModel.
- SqliteAuthenticationRepository.CreateFirstAdminAsync now returns the seeded Admin RoleId in the authenticated result.
- AuthStartupForm now passes RoleId into CurrentAppSession.
- The Local Host session identity provider can now receive non-empty RoleId from normal auth flow.
- Added Phase66G0AuthenticatedRoleIdPropagationChecks.
- CoreSmoke now runs the 66G0 guard after 66D.

## Preserved boundaries

- No runtime activation was added.
- No runtime stop was added.
- No runtime restart was added.
- No Program behavior was changed.
- No Shell routing behavior was changed.
- No ProductLookup UI behavior was changed.
- No transport was added.
- No LAN behavior was added.
- No two-machine behavior was added.
- No direct SQLite access was added outside Infrastructure.

## Current state after this phase

The normal authentication path can now produce a CurrentAppSession with both UserId and RoleId.

The Local Host ProductLookup adapter can now pass the session identity provider gate once a real active authenticated session exists.

Runtime remains stopped until an explicit runtime lifecycle activation phase.
# Phase 66B - Adapter Request Context Provider Consumption

Status: Adapter request context provider consumption only.

## Scope

Replace the ProductLookup adapter placeholder HostRequestContext with the passive SalrynLocalHostRequestContextProvider.

This phase wires the provider into the adapter through AppServices.

## Added

- Added a provider-aware CompositionLocalHostProductLookupGatewayAdapter constructor.
- Added passive provider storage inside the adapter.
- Updated AppServices to pass LocalHostRequestContextProvider into the ProductLookup adapter.
- Removed the adapter placeholder CreateStoppedRuntimeBridgeContext path.
- Removed fake TenantId, StoreId, UserId, RoleId, TerminalId, and DeviceKey context values from the adapter.
- Kept HostRequestContext creation owned by SalrynLocalHostRequestContextProvider.
- The adapter now fails closed when the provider cannot create context.

## Preserved boundaries

- No runtime activation was added.
- No runtime stop was added.
- No runtime restart was added.
- No Program or Shell behavior was changed.
- No ProductLookup UI behavior was changed.
- No checkout behavior was changed.
- No product management behavior was changed.
- No transport was added.
- No LAN behavior was added.
- No two-machine behavior was added.
- No direct SQLite access was added outside Infrastructure.

## Current state after this phase

The ProductLookup adapter no longer invents HostRequestContext identity.

The ProductLookup adapter now depends on the passive request context provider.

Because the adapter does not yet receive a real app/session identity source, provider context creation can still fail closed.

Runtime remains stopped until an explicit runtime lifecycle activation phase.
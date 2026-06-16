# Phase 65T - App Local Host ProductLookup Dispatch Bridge

Status: App Local Host ProductLookup dispatch bridge only.

## Scope

Introduce the adapter-to-holder ProductLookup dispatch bridge inside App.WinForms Composition only.

Production source change: adapter-to-holder dispatch bridge only.

## Implemented

- CompositionLocalHostProductLookupGatewayAdapter may now build a HostRequestEnvelope for ProductLookup.
- CompositionLocalHostProductLookupGatewayAdapter may now call the held in-process runtime dispatch path through the passive holder.
- Successful Host ProductLookup responses are mapped to AppLocalHostProductLookupResult.FoundProduct.
- Host not-found responses are mapped to AppLocalHostProductLookupResult.NotFound.
- Runtime-stopped, boundary, faulted, or unavailable responses are mapped to AppLocalHostProductLookupResult.LocalHostUnavailable.
- Runtime start is still not performed.
- Program, Shell, checkout UI, product management UI, transport, LAN, and two-machine behavior remain unchanged.

## Allowed

- Adapter may access the held Local Host composition only to reach the in-process runtime dispatch bridge.
- Adapter may call DispatchAsync for LocalHostProductLookupRequest and LocalHostProductLookupResponse.
- Adapter may map Host response envelopes into app gateway results.
- Adapter may return LocalHostUnavailable while runtime remains stopped.

## Rejected

- No Local Host runtime was started.
- No Program startup behavior was changed.
- No Shell routing behavior was changed.
- No checkout UI behavior was changed.
- No product management UI behavior was changed.
- No HTTP transport was implemented.
- No socket transport was implemented.
- No transport was implemented.
- No LAN discovery was implemented.
- No two-machine behavior was implemented.
- No direct SQLite access was added to the adapter.
- No Infrastructure reference was added to Host.Local.
- No App.WinForms reference was added to Host.Local.

## Runtime note

The held Local Host composition is still created in stopped state. Because runtime start is not approved in this phase, ProductLookup dispatch through the bridge is expected to return LocalHostUnavailable until a later runtime-start phase provides an approved lifecycle and real request identity context.

## Boundary result

The app composition adapter now has a real dispatch bridge to the held in-process Local Host ProductLookup path. The bridge remains blocked by stopped runtime state and is not consumed by Program, Shell, UI, checkout, or product management.

## Next phase

Phase 65U may plan Local Host runtime lifecycle activation or request identity context for the ProductLookup bridge, but it must remain separate and must not add transport, change Program, change Shell, or change checkout/product management behavior unless explicitly approved first.
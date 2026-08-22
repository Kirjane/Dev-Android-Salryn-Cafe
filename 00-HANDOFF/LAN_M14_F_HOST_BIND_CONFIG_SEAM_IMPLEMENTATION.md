# LAN-M14-F Host Bind Config Seam Implementation

## Source Stamp

- Repo: `Kirch-Nairu/Salryn`
- Stable branch: `KIRCH-SANDBOX`
- Candidate branch: `KIRCH-FREEHAND`
- Stable SHA: `e477699dca27a21954a5ad9d388307fdb9ed08fd`
- `KIRCH-FREEHAND` SHA before LAN-M14-F: `e477699dca27a21954a5ad9d388307fdb9ed08fd`
- Ahead/behind before LAN-M14-F: `0 ahead / 0 behind`
- Date/context: LAN-M14-E stable; this sprint implements the host bind config seam for hybrid bridge readiness.
- Status: LAN-M14-E stable; LAN-M14-F implements bind config seam only; hybrid bridge remains disabled by default.

## Stable Stack

Stable on Sandbox as of `e477699dca27a21954a5ad9d388307fdb9ed08fd`:

- `LAN-M13` - remote read-only LAN usability
- `ROLE-M1` through `ROLE-M4` - role and permission boundaries
- `M5` through `M8` - M-series completion and LAN transition gate
- `LAN-M14-A` - Post-Promotion LAN Source Stamp + Completion Scope Audit / First Cutline
- `LAN-M14-B` - Explicit Host Bind Configuration Readiness + Guarded Runtime Cutline
- `LAN-M14-C` - Remote Read-Only Client Session Bridge Cutline
- `LAN-M14-D` - Bridge Session Contract Study
- `LAN-M14-E` - Audit and Read-Attempt Policy Cutline

## LAN-M14-F Purpose

LAN-M14-F implements the host bind configuration seam that lets Api.Local resolve listen URLs from `ILanRemoteTransportConfigService` while keeping localhost as the safe default.

It does not enable LAN bind by default.
It does not make Api.Local listen on `0.0.0.0`.
It does not authorize wildcard bind.
It does not implement the hybrid bridge HTTP facade.
It does not implement pairing UX.
It does not implement mutation.

## What Was Implemented

- `LocalApiLanUrlResolver` resolves listen URLs from default `http://127.0.0.1:5299` plus an explicit LAN address only when remote transport is enabled with `ExplicitLanAddress` bind mode and config validation passes.
- `RemoteReadOnlyLanBindPolicyMapper` maps `LanRemoteTransportConfig` to bind policy responses and connection mode (`LocalOnlyReadMode` vs `HybridBridgeReady`).
- `LocalApiApplicationFactory` wires `LanRemoteTransportApiComposition` and `ILanRemoteTransportConfigService` before calling `LocalApiLanUrlResolver.ResolveListenUrls`.
- `RemoteReadOnlyApiService` reports bind policy through the mapper instead of hard-coded localhost-only strings.
- Default Api.Local URL remains `http://127.0.0.1:5299`.
- Wildcard bind (`0.0.0.0`) remains rejected through existing gate validation.

## Security Boundaries

- Api.Local defaults to `127.0.0.1:5299` only.
- LAN listen URL is added only after explicit operator configuration enables remote transport with a specific IP.
- `SalrynLanRemoteTransportGate.ValidateConfig` and `SalrynHostCommandLoopbackRules.IsForbiddenBindAddress` must reject invalid or wildcard addresses.
- `ILanRemoteTransportConfigService` remains the single configuration authority; Api.Local does not invent bind addresses.
- No discovery, no shared SQLite, no SQLite path exposure.

## Forbidden Scope

LAN-M14-F explicitly forbids:

- enabling remote transport by default
- wildcard or `0.0.0.0` bind
- network discovery
- hybrid bridge HTTP authorization
- pairing operator UX changes
- audit runtime beyond existing policy seam
- remote mutation or checkout
- official journal writes or sequence allocation
- shared SQLite
- QR/mobile/cloud behavior
- RMO/printer/PDF/Crystal/ESC-POS work

## Decision

- LAN-M14-F implements the host bind config seam only.
- Hybrid bridge runtime, pairing UX, and end-to-end closure remain separate LAN-M14-G/H/I work.
- Remote read-only LAN remains disabled by default; explicit operator enable is required before any LAN listen URL is added.
- Codex must verify LAN-M14-F before treating LAN-M14-G as approved.

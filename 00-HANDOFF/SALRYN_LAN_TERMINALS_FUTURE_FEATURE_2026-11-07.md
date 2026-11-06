# Salryn POS — Future Feature: LAN Terminals

**Status:** `FUTURE FEATURE — DEFERRED FROM INITIAL PRODUCTION RELEASE`  
**Decision date:** 2026-11-07 PHT  
**Scope:** Salryn POS single-Host Windows release, `Kirjane/Salryn`  
**Owner:** Kirjane Information Technology Services / Technical Authority

## Client-facing overview

**LAN Terminals (Future Feature):** In a later release, an additional licensed computer/tablet on the same local network may connect to the main Salryn Host PC as a cashier/manager terminal. The Host PC remains responsible for the store database, product prices, sales, inventory changes, permissions, receipts and transactional integrity. This is intended to support additional counters without sharing the database file.

**Current release statement:** Not included in the initial production release. Do not promise that LAN terminals are operational, tested or supported on the client's equipment.

## Why deferred

Production-quality LAN use must prove consistent behavior across separate failure classes:

| Layer | Current code evidence | Future acceptance question |
| --- | --- | --- |
| Host configuration | `LanRemoteTransportConfigService` persists configuration in SQLite | Does configured LAN status match the actual listening address after restarts/IP changes? |
| Terminal registry | `SqliteTerminalRegistryRepository` persists terminal rows | Does a legitimate previously paired device resume without unauthorized trust escalation? |
| Approval workflow | `RemotePosPairingGateService` holds pending requests and join codes in memory | Is expiring/requiring fresh approvals a safe and clear operator process? |
| Operator session | `RemotePosOperatorSessionService` keeps 30-minute sessions in memory; restart deliberately invalidates | Can operators sign back in predictably while preserving committed sales? Do not persist raw sessions just to make them survive restart. |
| Client browser | `terminalLicenseStorage.ts` uses localStorage for terminal machine code/license; `useRemotePosClient.ts` uses sessionStorage for in-flight checkout retry | What happens when browser storage clears, profile changes, tab closes or session recovery fails? |
| Sale replay | Beta RC1 introduced a durable Host checkout idempotency ledger | Do reconnect/retry and Host restart produce exactly one sale and stock decrement with correct receipt? |

**Classification:** Multiple temporary states are intentionally ephemeral and should remain fail-closed; this is **not yet a demonstrated bug in persistent terminal registration**. The user's field report of “persistence problem” must be captured with reproduction data before assigning a root cause.

## Immediate V1 boundary

- Main production workflow: **one Host POS computer**.
- Public Host UI: Future Feature overview with no enrollment/enable/start workflow in ordinary builds.
- If old LAN settings exist, show their status and allow admin to stop LAN access safely; retain database records and recovery ability.
- Internal testing of old LAN management UI remains behind `VITE_SALRYN_INTERNAL_SURFACES=true`, not a production promise.
- API/transport rollout gate remains a **required R1 security/acceptance task**. Hiding a React page alone cannot guarantee that an existing Host with saved enabled LAN configuration is not listening.
- No LAN Terminals mention as active functionality in the final client manual, pricing, advertisements or onboarding.

## Future acceptance matrix

1. Fresh Host with LAN disabled; non-local clients cannot access terminal POS.
2. Host config enabled only with explicit authorized action; no unsafe wildcard binds.
3. Device A pairing, approval, license, login, sale, sign-out.
4. Device A browser reload and device reboot; correct state/recovery and clear re-login.
5. Host process restart; intentionally invalidated sessions but intact terminal registration and sale data.
6. Host IP change/Wi-Fi loss and recovery; truthful connection status, no silent stale URL.
7. Lost HTTP response after sale commit; same request identity returns same sale, never double charges or decrements.
8. Two devices on LAN; limit/revocation/permission semantics, concurrent requests and double-submit.
9. Browser localStorage/sessionStorage cleared; clear reactivation path without reusing another device identity.
10. Disable/remove terminal, clear active sessions; blocked device remains blocked.
11. Backup/restore and migration preserve intended registry data and do not silently grant remote access.
12. Physical two-device acceptance with screenshots, Host log, terminal log and printed receipt evidence.

**Release exit:** Maintainer-reviewed threat model, regression tests, field hardware QA, security/permission review, written support procedures and explicit later-version release approval.

## Source references

- [Pinned main source](https://github.com/Kirjane/Salryn/tree/dc5a6dcf713da7d4e9d0f47b47a685027eae45b9)
- `00-HANDOFF/BETA_RC1_LAN_TERMINAL_HARDENING_REPORT.md`
- `00-HANDOFF/BETA_RC1_KNOWN_LIMITATIONS.md`
- `00-HANDOFF/PRE_I_FINAL_CAPABILITY_MATRIX.md`
- `src/Salryn.Infrastructure/Data/Services/LanRemoteTransportConfigService.cs`
- `src/Salryn.Infrastructure/Data/Repositories/System/SqliteTerminalRegistryRepository.cs`
- `src/Salryn.Api.Local/RemotePos/RemotePosPairingGateService.cs`
- `src/Salryn.Api.Local/RemotePos/RemotePosOperatorSessionService.cs`
- `src/Salryn.App.Web/src/utils/terminalLicenseStorage.ts`
- `src/Salryn.App.Web/src/hooks/useRemotePosClient.ts`

**Note:** This document is a staging-scope decision, not an end-to-end production safety test or a server-side feature disable certification.

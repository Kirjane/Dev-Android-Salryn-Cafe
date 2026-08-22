# V2-REMOTE-POS-HARDEN-M3 Study

**Date:** 2026-07-09  
**Branch:** `KIRCH-V2-REACT-SHELL`  
**Starting HEAD:** `5cdb7e3cd547cb8bb1b38ef21e5d703e74f08ac2`  
**Mode:** Study first; implement only if GO.

---

## Source stamp (Phase 0)

| Item | Value |
|------|--------|
| Branch | `KIRCH-V2-REACT-SHELL` |
| HEAD | `5cdb7e3c` (matches `origin/KIRCH-V2-REACT-SHELL`) |
| `origin/KIRCH-SANDBOX` | `9cdc212f` |
| Ahead/behind Sandbox…React | `0 15` (React ahead by 15) |
| Worktree | Clean of source; only local artifacts `.lan-bind-test-err.txt`, `.lan-bind-test-out.txt` |

Sandbox left untouched.

---

## A. LAN bind truth

**Verdict: solid — no blocker**

| Question | Finding |
|----------|---------|
| UI truth match actual listen URLs? | Yes. `LocalApiLanRuntimeStatusService` reads Kestrel `IServerAddressesFeature` and exposes `ActualListenUrls`. Host page shows “LAN server is actually listening on…”. |
| Copy Client URL disabled when not listening? | Yes. `clientUrl` is empty unless `runtime.clientUrlReady && runtime.clientUrl`; button `disabled={!clientUrl}`. |
| 0.0.0.0 / `*` / `+` rejected? | Yes. `SalrynHostCommandLoopbackRules.IsForbiddenBindAddress` rejects `0.0.0.0`, `*`, `::`, `[::]`. Startup validator fails closed. Dev script rejects same. `+` not used as bind. |
| Dev script fails when IP not assigned? | Yes. `Start-SalrynLanDev.ps1` throws if bind IP not on local NIC. |
| Firewall guidance informational only? | Yes. Returned as string on runtime status; Host UI shows Alert “Firewall guidance (manual only)”. No auto-rule. |
| Fake configured LAN URL? | No. `ClientUrl` only set when readiness is `Ready` (bind on host + actual listen match). Loopback-only / missing bind / wrong IP → no client URL. |

---

## B. Remote POS terminal pairing

**Verdict: solid — no blocker**

| Question | Finding |
|----------|---------|
| State machine explicit? | Yes. `RemotePosFlowState` S0–S6 + S_REVOKED / S_OFFLINE / S_RESTORING. |
| No-storage → S1_UNPAIRED? | Yes. `restorePairingState(null)` and `resolveRestoreFlowState(null, …)`. |
| Stale storage clears safely? | Yes. Unknown / expired / handshake 403 → clear pairing + notice “Pairing expired or reset…”. |
| Revoked clears pairing + operator token? | Yes. `clearLanClientPairing` + `clearRemotePosOperatorSessionToken`. |
| Pending avoids duplicate auto-submit? | Yes. `restoreAttemptedRef`; pending restore polls status only; no auto re-request. |
| Restore avoids fake trust? | Yes. Must pass `fetchRemotePosTerminalTrust` before S4; no local-only trust. |
| hostBaseUrl normalized? | Yes. `normalizeHostBaseUrl` / `normalizeLanHostBaseUrl` strip trailing slashes consistently. |

Note: `S_REVOKED` UI branch is largely defensive; live revoke path lands on `S1_UNPAIRED` with `lastPairingNotice`. Acceptable.

---

## C. Remote POS operator session

**Verdict: solid — no blocker**

| Question | Finding |
|----------|---------|
| Only token stored, never password? | Yes. Storage keys hold `sessionToken` only; tests assert no `password`. |
| Token backend-validated before S6? | Yes. `restoreOperatorSession` → `fetchRemotePosSession` + capabilities; invalid → clear token, stay paired, S5. |
| Sign out clears token, keeps pairing? | Yes. `logout` → `resetSession` (clears token) + flow S5 if paired. |
| Clear pairing clears both? | Yes. `clearPairing` → pairing + `resetSession`. |
| Terminal revoke rejects operator restore? | Yes. Trust path clears token before re-pair; backend `ValidateTrustAsync` rejects non-active terminals. |
| Admin/Owner forbidden remotely? | Yes. Backend `AllowedRemoteLoginRoles = Cashier, Manager`; UI + warnings state host-local Admin/Owner. |
| Session expiry shown? | Yes. Connected UI shows “Session expires” from `expiresAtUtc`. |
| Remember session explicit? | Yes. Checkbox “Keep this cashier signed in…” + local vs session storage + “Passwords are never stored…”. |

Backend: in-memory sessions, SHA-256 token hash only, 30-minute lifetime, origin binding, fail-closed on host restart.

---

## D. Remote client landing resume

**Verdict: solid — minor copy hardening allowed**

| Question | Finding |
|----------|---------|
| Resume only for current host? | Yes. `readLanClientPairing("remote-pos", currentBrowserOrigin())` + `hasPairedTerminalMaterial`. |
| No auto-join / auto-approve? | Yes. Manual click opens Remote POS mode only. |
| POS vs Read-Only clear? | Yes. Separate cards and pills. |
| Phone layout? | Resume is full-width button; grid stacks on small screens. Acceptable. |
| Backend validation called out? | Partially. Current: “restore the terminal and sign in again if needed.” Can clarify that a valid saved cashier session resumes automatically after backend validation. |

---

## E. Terminal lifecycle UX

**Verdict: solid — no blocker**

| Question | Finding |
|----------|---------|
| Revoked rows read-only? | Yes. No Disable/Revoke actions; explanation required. |
| Disabled honest about re-enable? | Yes. “Re-enable is not supported in this build.” |
| Active only for Disable; Active/Disabled for Revoke? | Yes. `canDisableTerminal` / `canRevokeTerminal`. |
| Host explains re-request? | Yes. Revoked explanation + pairing sequence text. |
| Re-pair new pending/terminal? | Domain: revoke → client clears → new join request → new activation. Registry has no `EnableTerminalAsync`. |

---

## F. Remote boundary safety

**Verdict: solid — no blocker**

| Question | Finding |
|----------|---------|
| Host Checkout impossible from LAN client? | Yes. `RemoteClientApp` never mounts `CheckoutPage`. Remote POS uses `/api/remote-pos/*` only. Source guards in multiple V2/LAN checks. |
| LAN control host-local only? | Yes. `/api/lan-control` + `LocalLanControlAuthorization` loopback + operator session. Remote POS endpoints do not map lan-control. |
| Admin/Owner remote login blocked? | Yes. Backend + UI + source tests. |
| Credentials never stored? | Yes. Token only. |
| Bridge leak? | Remote POS is separate surface; auth requires terminal + handshake + operator session; capabilities list host-local-only actions. Host `/api/checkout` goes through `LocalApiPermissionGuards` → loopback operator. |

---

## G. Checkout workstation regressions

**Verdict: no source-level regression from LAN/Remote POS**

- Host checkout still posts `/api/checkout` with shift gates and stock honesty checks in `V2ReactCheckoutSourceChecks`.
- Host checkout remains outside remote shell.
- Runtime host-checkout gate remains available via CoreSmoke `--only v2-host-checkout`.

---

## H. Full CoreSmoke / stale guards

**Finding: stale exact-copy guard is a promotion-suite issue, not a product security ambiguity**

Full CoreSmoke still includes:

- `LanM16HostClientIdentityUxChecks`
- `LanM16QaBRemotePosPairingGateChecks`
- `LanM16RcBEntryBoundaryChecks`

These require the exact page string **`Cashier / Manager login only`**.

Current intentional UI title:

> `This terminal is paired — Cashier / Manager login`

Meaning preserved: Cashier/Manager login stage; Admin/Owner host-local (still asserted separately).  
This is a **brittle obsolete string guard**, explicitly listed as allowed hardening in the task brief.

Also:

- Full CoreSmoke main path runs `V2ReactCheckoutSourceChecks` and `V2HostOperatorShiftApiChecks` but **does not** yet run `V2LanBindTruthChecks`, `V2LanClientPairingPersistenceChecks`, or `V2TerminalLifecycleUxChecks` (only via `--only`). Adding them to the main path is allowed hardening coverage.

InfrastructureSmoke copies of the same brittle string exist; fix for consistency if touched.

---

## Phase 2 decision

# GO IMPLEMENT

**Rationale:**

- No auth/session/schema ambiguity requiring redesign.
- No unsafe token handling (token-only; backend validation before S6; clear paths correct).
- No boundary leak of Host Checkout, lan-control, or remote Admin/Owner.
- No lifecycle contradiction with domain (revoke permanent; re-enable unsupported).
- Full CoreSmoke stale string is clearly obsolete wording, not a policy fork.
- Fixes remain narrow hardening only.

**STOP conditions not met.**

---

## Planned hardening (Phase 3)

1. Landing resume copy: clarify paired restore + automatic resume if saved cashier session still valid.
2. Remote POS login stage: clarify terminal paired; session only if no valid saved token; password never stored (tighten existing copy).
3. Stale CoreSmoke / LAN-M16 string guards: match meaning, not exact obsolete phrase.
4. Add V2 LAN source checks to full CoreSmoke main path.
5. Remove local `.lan-bind-test-*.txt` artifacts if present (no-op-safe cleanup).

**Out of scope (not implementing):** schema rewrite, auth redesign, features, printers/PDF/RMO, remote Admin/Owner, wildcard bind, Sandbox, promotion.

---

## Implementation result (post-GO)

**Commit:** `6cc89281` — *Harden Remote POS LAN client readiness*  
**Pushed:** `origin/KIRCH-V2-REACT-SHELL`  
**Full CoreSmoke:** passed after stale-guard updates  
**npm run build:** passed  
**Manual phone check:** not run in this session (operator session persistence remains source/logic verified; phone re-accept recommended before promotion)


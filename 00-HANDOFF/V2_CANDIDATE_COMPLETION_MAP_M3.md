# V2-CANDIDATE-COMPLETION-MAP-M3

**Date:** 2026-07-09  
**Branch:** `KIRCH-V2-REACT-SHELL`  
**Starting HEAD:** `2bb21f012c97c094e8db0b07cfa76c434ff1f5f1`  
**Stable:** `KIRCH-SANDBOX` @ `9cdc212f` (untouched)  
**Mode:** Study first; narrow hardening only if GO.

---

## Phase 0 — Source stamp

| Item | Value |
|------|--------|
| Branch | `KIRCH-V2-REACT-SHELL` |
| HEAD | `2bb21f01` (matches `origin/KIRCH-V2-REACT-SHELL`) |
| `origin/KIRCH-SANDBOX` | `9cdc212f` |
| Ahead/behind Sandbox…React | `0 17` |
| Worktree | Clean |

Recent V2 stack (newest first, React ahead of Sandbox):

1. Document M3 hardening implementation result  
2. Harden Remote POS LAN client readiness  
3. Persist Remote POS operator session across browser reopen  
4. Clarify remote terminal lifecycle actions  
5. Restore operator session on refresh  
6. Fix unpaired/revoked state flow  
7. Enforce explicit LAN bind readiness  
8. FLOW-RESET-M2 pairing state machine + dev DB seed  
9. Persist LAN client pairing  
10. React checkout workstation + host checkout/shift APIs  

---

## 1. What is done?

### Host shift backend
- Local host shift endpoints (`/api/shift`, open, close) with loopback operator session + permissions.
- Runtime gate: CoreSmoke `--only v2-host-shift` and full suite includes `V2HostOperatorShiftApiChecks`.

### Host checkout backend
- Authoritative host `POST /api/checkout` via `LocalHostCheckoutEndpoints` + host operator services.
- Enforced through `LocalApiPermissionGuards` (loopback + authenticated session + `Sales.Create`).
- Runtime gate: CoreSmoke `--only v2-host-checkout` (`V2HostOperatorCheckoutApiChecks`).

### React checkout workstation
- Host-only `CheckoutPage` + `api/checkout.ts`.
- Shift gate, stock honesty, readiness panel, non-official checkout receipt labeling.
- Source gate: `V2ReactCheckoutSourceChecks` (full suite + `--only v2-react-checkout`).
- Remote shell does **not** mount `CheckoutPage`.

### LAN bind truth
- Explicit `--lan-bind` / `SALRYN_LAN_BIND_IP` + port; NIC assignment required at startup.
- Wildcard / `0.0.0.0` / `*` / `::` forbidden in core rules, validator, and `Start-SalrynLanDev.ps1`.
- Runtime status reads **actual** Kestrel listen URLs; `ClientUrlReady` gates Copy client URL.
- Source gate: `V2LanBindTruthChecks` (full suite + `--only`).

### Remote client landing
- `/remote-client` entry via `isRemoteClientBrowserLocation()`.
- Resume card only when pairing material exists for **current browser origin**.
- Manual open only (no auto-join / auto-approve).
- Remote POS vs Remote Read-Only clearly separated.
- Copy clarifies host validates saved cashier session before auto-resume.

### Remote POS pairing
- Explicit flow states S0–S6 + S_RESTORING / S_OFFLINE / S_REVOKED.
- localStorage pairing record (no operator token in pairing record).
- Backend trust probe + handshake refresh; revoked/unknown clears storage safely.
- Host-local accept/reject + one-time pairing code.
- Source gate: `V2LanClientPairingPersistenceChecks`.

### Remote POS operator session
- Host-issued token only (never password); local or session storage by preference.
- Restore path always calls `GET /api/remote-pos/session` before S6.
- Sign out clears token, keeps terminal pairing; clear pairing clears both.
- Backend: Cashier/Manager only; Admin/Owner host-local; 30-minute in-memory sessions; origin-bound; SHA-256 token hash.

### Terminal lifecycle
- Active: Disable + Revoke for managed remote roles.
- Disabled: honest “re-enable not supported”; Revoke still available.
- Revoked: read-only; client must request access again (new pending terminal).
- Source gate: `V2TerminalLifecycleUxChecks`.

### Dev reset/seed
- `tools/dev/Reset-SalrynDevDb.ps1` + `FlowResetDevSeedChecks` for **dev DB only**.
- Known cashier seed for local flow reset; not production seed product data.

### CoreSmoke / verifiers
| Runner | Mode |
|--------|------|
| `v2-lan-bind-truth` | full + `--only` |
| `v2-lan-client-pairing` | full + `--only` |
| `v2-terminal-lifecycle` | full + `--only` |
| `v2-host-shift` | full + `--only` |
| `v2-react-checkout` | full + `--only` |
| `v2-host-checkout` | full + `--only` (wired into default path in this map task) |

LAN-M16 freehand guards updated in HARDEN-M3 for obsolete exact copy and over-broad `Seed` path token.

---

## 2. What is deferred?

| Item | Why deferred |
|------|----------------|
| Manual phone/browser acceptance | Operator field confirmation; not automated |
| Full solution build while API locks DLLs | Environment: stop `Salryn.Api.Local` or use `/p:BuildProjectReferences=false` |
| RMO / official receipt / BIR legal | Explicit non-scope for V2 React/LAN candidate |
| Printer / PDF / Crystal / ESC-POS | Explicit non-scope |
| Product / inventory mutation UI | Explicit non-scope |
| Remote Admin / Owner login | Intentionally forbidden |
| Host Checkout on LAN client | Intentionally forbidden |
| QR / mDNS / cloud / DNS discovery | Intentionally forbidden |
| WinForms feature expansion | Split-role: React lane only |
| (was) `v2-host-checkout` only via `--only` | **Resolved in this task** — now in full CoreSmoke main path |

---

## 3. What is missing for promotion?

**Promotion blockers (process / acceptance only — no code defect found):**

1. **Manual phone + host browser acceptance** (see `V2_CANDIDATE_FINAL_ACCEPTANCE_CHECKLIST_M3.md`).
2. **Maintainer decision to promote** React → Sandbox after green acceptance (not automated).
3. **Documented build lock workaround** when `Salryn.Api.Local` holds DLLs:
   - stop the process, or
   - build with `/p:BuildProjectReferences=false` for tests/CoreSmoke after references are already built.

**Not promotion blockers:**

- Full CoreSmoke stale guards from HARDEN-M3: already patched.
- Auth/session/schema redesign: not required.
- Boundary leaks: not found in source review.

---

## 4. What is risky? (classified)

| Risk | Level | Assessment |
|------|-------|------------|
| Persisted operator token | **Managed** | Token-only; host validates before S6; cleared on fail/revoke/sign-out |
| localStorage / sessionStorage clearing | **Managed** | Pairing vs token scopes separated; clear pairing clears both |
| Host URL normalization | **Managed** | Trailing-slash strip consistent on read/write |
| Stale storage after DB reset | **Managed** | Unknown/revoked/handshake failure → clear + re-pair notice |
| Revoke / disabled lifecycle | **Managed** | UI honest; backend rejects non-active terminals |
| LAN bind trust vs configured | **Managed** | Client URL only when actual listen matches configured bind |
| Remote endpoint exposure | **Managed** | Separate `/api/remote-pos`; lan-control + host checkout loopback |
| Checkout boundary | **Managed** | RemoteClientApp never mounts CheckoutPage; remote POS uses remote-pos APIs |
| Operator sessions in-memory only | **Known limit** | Host restart invalidates tokens (fail-safe, not a leak) |
| Origin binding | **Known limit** | IP/origin change can force re-login (intended) |

---

## 5. What should not be touched

- `KIRCH-SANDBOX` branch / force push / reset / promote without explicit instruction  
- Remote Admin/Owner login  
- Host Checkout UI or `/api/checkout` from LAN client  
- `/api/lan-control` remote exposure  
- Wildcard / `0.0.0.0` / `*` / `+` bind  
- QR / discovery / mDNS / cloud / DNS  
- Password storage on client  
- Fake terminal trust or fake operator session restore  
- Printer / PDF / Crystal / ESC-POS  
- RMO / BIR / official receipt claims  
- Product/inventory mutation UI (unless separately tasked)  
- WinForms feature expansion  
- Schema redesign / auth redesign / new product features  

---

## Phase 2 — Targeted code review summary

### LAN bind — solid
Factory parses bind, validates NIC, listens on loopback + explicit LAN only. Runtime status is truth-based. Dev script fails closed. Guards reject wildcard in factory/resolver/page.

### Remote POS pairing — solid
State machine readable; restore probes trust; no auto re-request; revoked/stale clear safely; hostBaseUrl normalized.

### Operator session — solid
Token only; backend session GET before S6; Admin/Owner blocked server-side; remember-session explicit.

### Terminal lifecycle — solid
Revoked read-only; disabled no re-enable; active only for disable; re-pair messaging present.

### Checkout — solid
Host-local page + API; stock/shift honesty; remote shell isolation guarded by multiple source checks.

### Boundary safety — solid
Loopback guards on host session/checkout/lan-control; remote POS role matrix host-local-only actions listed in capabilities.

### Full CoreSmoke / stale guards — residual risk low
HARDEN-M3 fixed brittle login copy and freehand Seed path. Remaining `Cashier / Manager login only` appears **in live UI copy** (intentional) and historical handoff docs — not as a failing exact-guard against obsolete UI.

---

## Phase 3 — Search classification (representative)

| Hit pattern | Classification |
|-------------|----------------|
| `0.0.0.0` in validator / dev script / tests as reject | **expected** |
| `0.0.0.0` forbidden in factory/resolver AssertNotContains | **expected** |
| `/api/checkout` on host checkout API + host client | **expected** |
| `/api/checkout` AssertNotContains on Remote POS page | **expected** |
| `/api/lan-control` host-only + AssertNotContains on remote POS | **expected** |
| `localStorage` / `sessionStorage` in `lanClientStorage` | **expected** (token/pairing only) |
| `password` in hasher/auth DB columns | **expected** (host-side, not client storage) |
| `password` AssertNotContains in lan client storage | **expected** |
| Admin/Owner host-local message in Remote POS | **expected** |
| `Cashier / Manager login only` in RemotePosClientPage copy | **expected** (current intentional wording) |
| `Cashier / Manager login only` in old handoff markdown | **expected** historical; not a smoke gate |
| `password_hash` seed SQL in RMO tests | **expected** (unrelated official-path tests) |

**No blocker hits requiring policy decision.**

---

## Phase 5 — Decision

# GO IMPLEMENT (narrow only)

Rationale: no auth/session/schema ambiguity, no boundary leak, no lifecycle contradiction, no full-smoke policy conflict. Remaining work is promotion packaging:

1. Completion map (this file).  
2. Final manual acceptance checklist.  
3. Optional: wire `V2HostOperatorCheckoutApiChecks` into full CoreSmoke main path for parity with shift.  
4. No product/auth redesign.

---

## After promotion (map only — do not implement now)

Ordered backlog for post-Sandbox V2 residual:

1. Field acceptance sign-off archive (manual results).  
2. Operator session durability policy if host restart UX is painful (still no password storage).  
3. Product/catalog browse UX improvements on Remote POS (IDs vs picker) if desired.  
4. Re-enable terminal (if product requires) — currently honestly unsupported.  
5. RMO / official output lane (separate program).  
6. Printer / PDF lane (separate program).  
7. Inventory/product mutation React UI (separate program).  
8. Production packaging / installers / service host (ops).  

---

## Verification status (filled during Phase 4/7)

| Gate | Result |
|------|--------|
| `git diff --check` | Pass |
| Test build (`BuildProjectReferences=false`) | Pass |
| CoreSmoke build (`BuildProjectReferences=false`) | Pass |
| Targeted V2 gates ×6 | Pass |
| Full CoreSmoke (includes host-checkout runtime) | Pass |
| `npm run build` | Pass |
| Manual phone acceptance | **not run** (only remaining promotion blocker) |

## Implementation in this task

- Wrote this completion map.
- Wrote `V2_CANDIDATE_FINAL_ACCEPTANCE_CHECKLIST_M3.md`.
- Wired `V2HostOperatorCheckoutApiChecks` into full CoreSmoke main path (parity with host shift).

# SALRYN V2 BACKEND RUNTIME AGENT HANDOFF

Status: Active handoff
Date: 2026-07-07
Branch: `KIRCH-V2-BACKEND-RUNTIME`
Audience:

- Grok coordinator / planner
- ChatGPT React shell owner
- Codex backend/runtime reviewer

Purpose: Force one shared repo-backed understanding of what is real, what is missing, and what must not be faked for Salryn V2 Stable Candidate work.

---

## Non-Negotiable Rules

1. No fake backend responses for React.
2. No placeholder success behavior.
3. If a workflow is not implemented, keep it disabled or explicitly unsupported.
4. WinForms auto-session behavior is legacy only and does not count as V2 auth evidence.
5. Chat claims do not count as repo truth.
6. A branch/file/task is not "done" unless it exists in git state or command output proves it.

---

## Repo Truth Summary

Two repo-backed baseline documents now exist:

- `00-HANDOFF/SALRYN_V2_BACKEND_RUNTIME_TRUTH_MAP.md`
- `00-HANDOFF/SALRYN_V2_BACKEND_RUNTIME_AGENT_HANDOFF.md`

The truth map conclusion is:

- React Host Console already has a real local API spine
- The main V2 backend problem is not missing auth/read foundations
- The real gaps are host-side mutation coverage, placeholder Local Host seams, and bounded runtime integrity

---

## What ChatGPT Can Safely Build Against Right Now

These local host surfaces are real and already mapped:

- `POST /api/auth/login`
- `POST /api/auth/logout`
- `GET /api/session`
- `GET /api/dashboard/summary`
- `GET /api/products`
- `GET /api/products/{id}`
- `GET /api/products/catalog/categories`
- `GET /api/products/catalog/brands`
- `GET /api/inventory/low-stock`
- `GET /api/inventory/movements`
- `GET /api/sales/history`
- `GET /api/sales/{id}`
- `GET /api/users`
- `GET /api/users/{id}`
- `GET /api/users/roles`
- `POST /api/users`
- `GET /api/audit/logs`
- `GET /api/audit/errors`
- host-local LAN control routes under `/api/lan-control`

This means ChatGPT should treat the following React Host Console pages as real-backed surfaces:

- Login
- Dashboard
- Products read
- Inventory read
- Sales history read
- Users read/create
- Audit/error log read
- Host LAN Control

---

## What ChatGPT Must Not Pretend Is Ready

These React nav items are currently disabled and should remain disabled until backed by real runtime/API work:

- POS Checkout
- Official Output Text File
- Settings
- Backup

ChatGPT should not:

- enable these just because the shell exists
- simulate success locally in the browser
- invent fake backend adapters
- treat WinForms workflows as proof the React path is complete

If a screen is explored ahead of backend support, it must be clearly marked disabled or unsupported.

---

## What Grok Must Stop Overstating

Grok planning is useful, but the following must be handled with strict verification:

### 1. File/branch/task completion claims

Do not say:

- "created"
- "initialized"
- "ready"
- "done"

unless command output or git state proves it.

### 2. Backend completeness claims

Do not summarize the backend as "largely done" without separating:

- real local API surfaces already usable by React
- placeholder Local Host seams
- bounded remote runtime behavior
- missing host mutation API coverage

### 3. Remote POS capability claims

Remote POS is real, but bounded.
Do not blur:

- real checkout/cart/shift/pairing behavior
- unsupported manager override
- process-scoped idempotency/session/cart state
- non-durable remote numbering concerns

### 4. React readiness claims

React is not blocked by missing login/read foundations.
React is still blocked by missing host mutation endpoints and disabled workflow pages.

---

## Backend Runtime Priorities For This Branch

These are the actual backend/runtime tasks that matter most for V2 Stable Candidate.

### Priority 1. Authoritative local host checkout path for React

Need:

- one real local React checkout submit path
- no dependency on placeholder Local Host behavior
- no fake success

Expected outcome:

- React checkout page can eventually be enabled honestly

### Priority 2. Local host shift operations for React

Need:

- real host-local shift open
- real host-local shift close
- clear auth/permission enforcement

Expected outcome:

- host checkout workflow can rely on a real shift state

### Priority 3. Product and inventory mutation scope decision

Need a deliberate answer:

- will V2 include host product/inventory mutation in React now
- or remain read-only for this candidate

Do not half-enable UI before this is resolved.

### Priority 4. Placeholder Local Host quarantine

For these placeholder-backed Local Host operations:

- login
- complete sale
- open cashier session
- close cashier session
- daily sales summary
- backup health

Either:

- implement the ones React genuinely needs now
- or explicitly quarantine them from V2 claim scope

### Priority 5. Runtime honesty over convenience

Keep these honest patterns:

- disabled UI
- explicit unsupported route behavior
- read-only boundary language
- sanitized remote errors

Do not replace them with false optimism in code or reports.

---

## Shared Working Interpretation

This is the straight version all agents should follow:

- React Host Console is partially real now, not imaginary
- It already has enough real API to keep moving on auth and read flows
- The biggest backend gap is host-side mutation coverage
- Remote POS is real but not fully durable/comprehensive
- Local Host still contains placeholders and must not be oversold
- Disabled pages are currently telling the truth

---

## Immediate Assignments

### ChatGPT

Proceed on `KIRCH-V2-REACT-SHELL` with:

- login/session UX
- dashboard
- products read
- inventory read
- sales history read
- users
- audit
- host LAN control

Do not enable:

- checkout
- backup
- settings
- official output

until backend/runtime support is real.

### Grok

Proceed as coordinator/planner, but:

- verify every branch/file creation claim
- stop treating planning text as completed repo work
- use the truth map as the current backend baseline
- scope backend tasks around real missing mutation/runtime seams, not generic rewrites

### Codex

Continue reviewer posture:

- catch placeholders
- catch fake success
- catch weak verification
- keep backend/runtime claims aligned with repo truth

---

## Final Instruction

If any agent wants to claim a V2 capability is ready, the burden of proof is:

1. route exists
2. permission/auth path is real
3. runtime behavior is real
4. result is not placeholder or fake success
5. React consumes it honestly

If one of those is missing, label it partial or disabled.

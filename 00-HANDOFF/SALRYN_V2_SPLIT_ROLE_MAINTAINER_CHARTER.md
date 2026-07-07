# SALRYN V2 SPLIT ROLE MAINTAINER CHARTER

Status: Active coordination charter
Date: 2026-07-07
Branch: `KIRCH-V2-BACKEND-RUNTIME`
Maintainer: Codex
Project Lead: Kirch

This charter exists to stop role drift.
The repo must stay grounded in evidence, not in whichever AI spoke most recently.

---

## Core Rule

Each agent gets one primary lane.
No lane may claim work from another lane without explicit repo-backed evidence and Kirch approval.

If something is not implemented, it must be shown as disabled, quarantined, or unsupported.
No fake success.
No placeholder pretending.

---

## Truth Baseline From Repo Inspection

The current repo state already proves:

- React Host Console can use real local auth/session and real read endpoints
- Backend/runtime still has missing host-side mutation coverage
- Local Host still contains placeholder-backed operations
- Remote POS is real but intentionally bounded
- Remote read-only is real and explicitly non-mutating
- WinForms auto-session behavior is legacy only and must not be treated as V2 auth evidence

This means V2 work should focus on the real gaps, not on re-litigating the whole stack.

---

## Lane Assignments

### 1. Grok CLI

Primary role:

- local execution
- local verification
- local repo writing
- commit/push when authorized
- command-backed truth checking

Use Grok CLI for:

- `git status`, `git diff`, `git log`, `git branch`, `dotnet build`, `dotnet test`
- local file edits and verification
- branch creation and integration when authorized

Grok CLI must not:

- claim work is done without command output
- treat planning text as repo truth
- overstate completion of files, branches, or verification

Best use:

- backend runtime implementation
- repo verification
- branch plumbing
- evidence collection

---

### 2. Grok Expert

Primary role:

- architecture coordination
- compliance planning
- RMO/BIR direction
- Crystal / output / reporting planning

Use Grok Expert for:

- RMO engineering direction
- official-output and reporting strategy
- Crystal Reports planning if Kirch authorizes it
- broader roadmap synthesis

Grok Expert must not:

- pretend to have locally verified changes unless command output proves it
- take over frontend implementation scope
- blur compliance planning into legal certification claims

Best use:

- RMO
- Crystal / output reporting
- compliance matrix interpretation
- roadmap sequencing for non-frontend work

---

### 3. ChatGPT Website

Primary role:

- React Host Console
- LAN Client UX
- frontend wiring to real backend APIs

Use ChatGPT for:

- React shell work
- page composition
- navigation
- disabled / unsupported UI states
- frontend polish that follows real backend truth

ChatGPT must not:

- fake backend responses
- claim backend completion it did not build or verify
- expand into backend runtime ownership

Best use:

- frontend
- UX states
- React API consumption
- visual structure and interaction design

---

### 4. Codex

Primary role:

- backend/runtime reviewer
- placeholder detector
- verification skeptic
- QA gatekeeper for future V2 verification work

Use Codex for:

- catching fake success
- checking placeholder behavior
- verifying backend contract honesty
- reviewing whether React is wired to real endpoints

Codex must not:

- rubber-stamp claims without evidence
- treat build success as full test coverage
- let legacy WinForms behavior count as V2 proof

Best use:

- backend review
- truth audit
- QA verification

---

## Example Scope Split

### Frontend

Owner:

- ChatGPT

Examples:

- React Host Console login
- React navigation
- dashboard / products / inventory / sales / audit screens
- disabled state for unsupported pages

### RMO / Crystal / Compliance

Owner:

- Grok Expert

Examples:

- RMO engineering plan
- official-output / report strategy
- Crystal Reports study
- compliance matrix alignment

### Backend / Verification / Local Writes

Owner:

- Grok CLI for local execution
- Codex for review

Examples:

- local API changes
- durable backend contracts
- command-backed verification
- placeholder quarantine

---

## What Is Already Clear From the Repo

1. React is not blocked at the start line.
2. The backend still needs honest work around host mutations and durable runtime seams.
3. RMO and Crystal should stay in a separate compliance/reporting lane unless Kirch says otherwise.
4. Local execution and verification should belong to Grok CLI when actual repo writes are needed.

---

## What Must Not Happen

- Frontend agents invent backend behavior
- Backend agents claim frontend completion
- Compliance agents claim legal certification
- Anyone claims a branch/file/task is done without proof
- Anyone uses WinForms auto-login as V2 auth evidence

---

## Maintainer Instruction

If a task crosses lanes, the default response is:

1. keep the current lane narrow
2. write the missing contract or disabled state
3. ask Kirch only when the decision changes scope materially

This is how we avoid scope chaos and keep Salryn moving.

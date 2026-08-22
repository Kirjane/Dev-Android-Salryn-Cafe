# POS-SURFACE3 LAN Coming Soon Report

Date: 2026-07-15

Branch: `KIRCH-POS-SURFACE3-LAN-COMING-SOON-RMO-REPORTING-STUDY`

Base branch: `KIRCH-POS-PRINTER1-WINDOWS-PRINTER-WIRING`

Base SHA: `b21fe40cc6437200a853cf5bcd44edbea209da6c`

Mode: narrow UI surface repair plus repo study. No backend LAN behavior, PRINTER1 behavior, Backup UI, licensing, installer, beta, or branch promotion work was performed.

Status: `READY_FOR_SOL_VERIFICATION`

---

## Purpose

Make LAN / Host LAN Control visibly **Coming Soon / internal preview** so it does not look client-ready or production multi-terminal ready before beta/client build.

This is intentionally a surface/copy lane. Backend LAN code remains intact for future verification.

---

## Files changed for LAN coming-soon copy

- `src/Salryn.App.Web/src/components/AppShell.tsx`
- `src/Salryn.App.Web/src/pages/DashboardPage.tsx`
- `00-HANDOFF/POS_SURFACE3_LAN_COMING_SOON_REPORT.md`
- `00-HANDOFF/RMO_BIR_REPORTING_ENGINE_DEEP_REPO_STUDY.md`
- `00-HANDOFF/RMO_BIR_REPORTING_ENGINE_DECISION_MATRIX.md`

No route ID was changed.

No permission check was weakened.

No backend LAN endpoint or pairing/runtime service was edited.

No PRINTER1 file was edited in this Surface3 lane.

---

## Check-first findings

### `navigation.ts`

Current Advanced nav has these page IDs/labels:

- `host-lan-control` / `Host LAN Control`
- `official-setup` / `Official Setup`
- `traditional-reporting` / `Traditional Reporting`
- `production-reports` / `Production Reports`

Decision: keep `Host LAN Control` label and route ID unchanged to avoid icon/test/permission churn. Add visible coming-soon copy instead.

### `permissions.ts`

`host-lan-control` is not cashier-facing. It requires StoreDeployment / StoreTerminal style permissions, and Manager is not included in the manager page allowlist for this route.

Decision: do not remove the page, and do not weaken or bypass permission gating.

### `AppShell.tsx`

Before Surface3, Advanced copy said Advanced contained host setup, reporting utilities, and LAN controls. That was accurate but not strong enough to prevent client-ready interpretation.

### `DashboardPage.tsx`

Dashboard contained a LAN terminals card when the current operator can open Host LAN Control. It said LAN access stays disabled until authorized, but did not clearly say Coming Soon / internal preview.

### `HostLanControlPage.tsx`

The page itself already says remote clients cannot open it and is host-local. No backend or page-level functional change was made in this lane because the lower-risk visible remediation was to mark the entry surfaces before navigation.

---

## LAN coming-soon change

### `AppShell.tsx`

Changed Advanced group copy to state:

- `Host LAN Control is Coming Soon / internal preview only`
- `not client-ready`
- `LAN terminal licensing and entitlement remain deferred`

Added a small `Soon` badge beside the `Host LAN Control` nav row.

Changed the pending join banner copy to say:

- `Internal LAN preview request`
- `Host LAN Control is Coming Soon / internal preview`
- open only for verification to accept/reject pending request

### `DashboardPage.tsx`

Changed the Dashboard LAN terminals card to show:

- status pill: `Coming Soon / internal preview`
- LAN terminal control is not part of the current client-ready release
- use only for internal verification on this host
- LAN sync, multi-terminal production use, licensing entitlement, and client deployment remain deferred
- a second computer remains an independent host; installing the same app does not mean sync

---

## Visible effect

Operators who can see Advanced or the Dashboard LAN card now see LAN as an internal-preview / coming-soon surface before using it.

The branch does not claim:

- LAN sync ready
- multi-terminal production ready
- LAN licensing ready
- terminal entitlement ready
- client deployment ready

---

## Permission and route safety

No route names changed.

No `AppPage` type changed.

No permission table changed.

No role access changed.

No remote client route changed.

No backend LAN pairing, terminal registry, host runtime, or remote POS transport code changed.

---

## What was intentionally not changed

- `navigation.ts` label remains `Host LAN Control`.
- `HostLanControlPage.tsx` behavior is unchanged.
- Remote client pages are unchanged.
- Backend LAN status/pairing APIs are unchanged.
- LAN terminal entitlement/licensing is not implemented.
- Multi-terminal production readiness is not claimed.

---

## Test / gate status

Local execution is not available from this chat.

Required SOL/local verification remains:

```powershell
cd C:\Users\kirch\Salryn
git fetch origin
git checkout KIRCH-POS-SURFACE3-LAN-COMING-SOON-RMO-REPORTING-STUDY
git reset --hard origin/KIRCH-POS-SURFACE3-LAN-COMING-SOON-RMO-REPORTING-STUDY

git rev-parse HEAD
git status

dotnet build Salryn.sln
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-printer1-windows-printer-wiring
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-manual1-r3-frontend-safety-ux-repair
npm --prefix src\Salryn.App.Web run build
git diff --check
git status
```

Gates:

- build: `NOT RUN / NEEDS SOL`
- targeted PRINTER1: `NOT RUN / NEEDS SOL`
- targeted R3: `NOT RUN / NEEDS SOL`
- web build: `NOT RUN / NEEDS SOL`
- diff check: `NOT RUN / NEEDS SOL`
- git status: `NOT RUN / NEEDS SOL`

No `CANDIDATE_AUTOMATED_PROVEN_MANUAL_PENDING` claim is made from this chat.

---

## Risk review

### Compile risk

Low-to-medium. The code changes are React copy/JSX only and did not add imports or route types. However, because local TypeScript build was not executed, this must still be SOL-verified.

### Product risk

Reduced. LAN is now visibly marked as coming soon/internal preview at major entry points.

### Remaining product risk

`HostLanControlPage.tsx` still contains operational actions and readiness terms. It is permission-gated and host-local, but before beta it should either remain internal/admin-only or receive a page-level banner in a future cleanup if Kirch wants even stronger wording.

---

## Recommended follow-up

Do not implement LAN entitlement here.

For the next UI cleanup lane, consider:

- page-level banner inside `HostLanControlPage.tsx`
- optional disabled beta-build feature flag for LAN surfaces
- route still present only in internal/admin builds

Status: `READY_FOR_SOL_VERIFICATION`

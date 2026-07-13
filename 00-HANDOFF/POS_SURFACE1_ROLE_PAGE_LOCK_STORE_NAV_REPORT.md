# POS-SURFACE1 Role Page Lock / Store Navigation Report

Branch: `KIRCH-POS-SURFACE1-ROLE-PAGE-LOCK-STORE-NAV`  
Base: POS-M7 final automated-proof SHA `ea48055206d37a2ac5aaaa707e685b77f9c6655b`

## Feature

- Removes unimplemented Official Output Text File, Settings, and Backup entries plus maintainer-facing contract explanations from normal store navigation.
- Groups implemented pages into Cashier, Manager, Admin, and Advanced surfaces.
- Adds a role-ID page-surface policy on top of existing permission checks.
- Cashier is limited to Checkout and Sales History even though `Products.View` remains available for checkout product lookup.
- Manager cannot open Host LAN Control, Official Setup, Users, or other admin surfaces by direct navigation.
- App-level `canAccessPage` continues to block direct page rendering with Access Denied; endpoint permission guards are unchanged.

## Automated proof

The focused check verifies navigation absence/grouping, central direct-page guards, stable role-ID policy, continued permission checks, and seeded Admin/Manager/Cashier permission boundaries including Sales.Void and stock-write denial.

## Gates

Self-check results:

- `dotnet build Salryn.sln`: PASS, 0 errors.
- Full CoreSmoke: PASS.
- `pos-surface1-role-page-lock-store-nav`: PASS.
- Targeted POS-M1 through POS-M7 CoreSmoke: PASS.
- `npm --prefix src/Salryn.App.Web run build`: PASS with the existing Vite chunk-size advisory.
- `git diff --check`: PASS.

Full CoreSmoke initially exposed stale source-shape assertions. ROLE-M4's authenticated-dashboard marker was retained while cashier dashboard access remains denied. RMO-A2 through RMO-A7 now assert that the unsupported Official Output surface is absent from store navigation while retaining their output policy, permission, audit, artifact, and legal-boundary checks. The LAN-M16 assertion now recognizes the enabled Host LAN Control item in its `Advanced` group. No RMO or LAN runtime behavior changed.

## Manual proof

Visual navigation cleanliness and operator comfort are deferred to Kirch.

## Status

`CANDIDATE_AUTOMATED_PROVEN_MANUAL_PENDING`

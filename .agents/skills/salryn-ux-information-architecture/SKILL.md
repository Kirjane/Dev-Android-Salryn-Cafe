---
name: salryn-ux-information-architecture
description: Design premium desktop POS navigation, page hierarchy, content priorities, information discovery, keyboard-first action delivery and coherent component tokens for cashier, owner and admin roles.
---

# Salryn Premium POS Information Architecture & Design Skill

## Product principle
**No second look**: within a glance, an operator should know their current module, the core information, next permissible task, result/feedback, and path to underlying records. Premium means fast, restrained, legible, reliable—not maximal animation.

## Design procedure
1. Map **personas and tasks**, not screens alone: cashier/scan operator, manager/owner, administrator, licensed U1 Host, base Host and Terminal (non-release feature).
2. Propose domain-oriented navigation groups and a consistent location indicator (group, selected module, page title, optional subview/breadcrumb); preserve access/entitlement and real entry points.
3. For each module, write a `content -> decision -> action -> feedback -> record` matrix:
   - **Checkout**: active cart, price/quantity, tender/Utang states, change, receipt success and recovery.
   - **Owner**: period, sales vs money received vs outstanding AR, as-of labels, comparison, drilldowns, anomalies.
   - **Products/Stock**: availability, recent movements, reorder, product edit permissions.
   - **Accounts Receivable**: correct customer, outstanding/aging, collection context, immutable history and reversal.
   - **Admin**: store identity, users, backup/recovery, licensing, audit, risks.
4. Define type scale, semantic color, spacing/density, component dimensions, tables, tabs, status chips, chart labels, form validation, focus, confirmation and multi-step task feedback. Use existing `components/ui` and tokens as starting point; recommend refactors only when concrete evidence supports it.
5. Make primary actions easy to find and irreversible actions deliberately harder; no false "live" state, "net profit", compliant tax claims, available money where data errored, or enabled LAN.
6. Specify Windows desktop and touchscreen constraints. Test keyboard-only checkout and scan focus before judging effects or hover. Interactive targets should be comfortably usable without making every table row huge.
7. Report specific **before/after** user journey, proposed module navigation and objective acceptance criteria. Use annotated screenshots from actual isolated Playwright renders; wireframes are proposals, not screenshots of shipping UI.

## Outputs
- `SALRYN_FRONTEND_PERSONA_NAVIGATION_INFORMATION_ARCHITECTURE.md`
- `SALRYN_PREMIUM_POS_DESIGN_SYSTEM_BLUEPRINT.md`
- P0/P1/P2/P3 issues linked back to exact source and screenshot.

## Design review red flags
No generic admin dashboard look, no gratuitous gradients/glassmorphism, no hidden primary action, no purple "AI app" chrome, no chart without readable numbers and source drilldowns, no empty-state dead end, no split role context inside an admin-only page, no runtime network font/CDN, no style-only permission gate.

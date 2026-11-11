---
name: salryn-ux-information-architecture
description: Opt-in Salryn navigation and design-system planning when a dedicated UX milestone is requested; do not activate for an immediate client release or operational defect.
---

# Salryn UX Information Architecture (Explicit Opt-In)

Use only after an Engineer-approved UX scope. Respect existing `navigation.ts`, `security/permissions.ts`, role IDs and licensing boundaries; different manager/cashier menus may share backend entities but must enforce permissions on API and UI.

Map persona task → existing data → decision → action → record; propose concise before/after navigation, error/empty states and acceptance checks. Avoid invented customer requirements, unsupported financial metrics, generic redesigns, new payment types or changes to transactions. For urgent delivery, route to `salryn-deadline-delivery` instead and defer redesign.

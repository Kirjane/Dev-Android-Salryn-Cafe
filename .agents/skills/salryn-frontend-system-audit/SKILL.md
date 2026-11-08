---
name: salryn-frontend-system-audit
description: Systematically inspect every base Salryn POS frontend route, shell, authentication, licensing, onboarding, checkout, reporting and administrator state before redesigning any UI.
---

# Salryn Whole-Frontend Audit Skill

Use this skill **before design or code changes**. Source of truth: `src/Salryn.App.Web/src`, including `App.tsx`, `navigation.ts`, `security/permissions.ts`, `components/ui/*`, all `pages/*`, hooks, APIs, `index.css` and Tailwind config. Do not infer actual UI solely from screenshots.

## Method
1. Verify branch, HEAD, worktree isolation and issue #19. Identify React 19, Vite, Tailwind, TanStack Query, TypeScript and Windows WebView2 constraints.
2. Enumerate all pages, entry modes and private/internal/Host/Terminal surfaces by opening source code and matching route selectors; don't rely on `pages/` filenames alone. Read all API method contracts that affect page states.
3. Build a complete flow graph: license absent/mismatch/expired/entitled; first-run onboarding/admin; login/retry/logout; cashier checkout/scanner/discount/split tender/receipt/void; Sales History; Utang; Reports/analytics; products/inventory; users/audit/settings; backup/recovery; remote/terminal support-only. Mark production LAN as Future Feature.
4. For **each screen and state**, capture: source reference, persona/permission/entitlement, user intent, prominent content, primary and secondary actions, recovery, where Back goes, loading/empty/denied/error/success, focus and keyboard, screen-density hazards, safe next action, evidence type.
5. Inspect actual runtime renders with Playwright skill wherever lawful. Mark static-only findings separately from observed findings. Never invent visual bugs without evidence.
6. Prioritize defects P0 blocker, P1 misleading/dangerous, P2 friction, P3 polish. Report file+screen+state, reproduction, recommended change, impact, required test, scope.
7. Output `00-HANDOFF/SALRYN_FRONTEND_SYSTEM_WIDE_INVENTORY.md` and `SALRYN_FRONTEND_UX_FINDINGS_PRIORITIZED.md`. No missing screen: explicitly list unreachable/internal/feature gated states.

## Fail-fast questions
- Would a new cashier know which app state they are in?
- Is a high-consequence action distinguishable from a read-only action?
- Is a value genuinely zero, missing, denied or still loading?
- Does primary navigation reflect actual permissions and licensing, not product marketing?
- Can any visual change obscure a field needed for financial integrity or receipt identity?
- Does empty/error help the user recover without contacting support?

Do **not** perform major redesign during audit; build a trustworthy prioritized diagnosis first.

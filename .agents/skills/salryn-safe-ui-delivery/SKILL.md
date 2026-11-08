---
name: salryn-safe-ui-delivery
description: Execute isolated premium frontend improvements for base Salryn without changing money, licensing, transaction semantics, API contracts or deployment safety, and provide exact-SHA rendered regression evidence.
---

# Salryn Isolated UI Delivery Skill

## Branch safety
- This is BASE Salryn retail POS, not Café Yza.
- `antigravity/salryn-premium-pos-ux` is an isolated branch originating from corrected analytics PR #18 evidence HEAD `ee16c9cc89055bcf7c2c155d20398c0bf43011ec`; verify current refs, don't reset/force-push.
- Use a distinct physical worktree, not the Codex active `C:\Salryn` checkout. Keep PR #18 and PR #10 draft/unmodified; stack future draft PR on `codex/salryn-owner-analytics-w0-w4`.
- Stop on conflict or base moving; negotiate through issue rather than blindly rebasing a finance branch.

## File ownership
May change: `src/Salryn.App.Web` page rendering, component architecture, TS/CSS, ui primitives, navigation **without expanding permissions**, safe test/dev harness, documentation on this branch.
Must not change absent explicit authority: C# services, SQLite/migrations, sales/payment calculation, AR/collections, client-request idempotency, role/licensing proof/security, WebView2 host and installer, real signed licenses, remote LAN enablement, any BIR claims.
If visual design needs backend data/API, file a scoped contract request to Codex and continue independent frontend work with test-only synthetic fixtures.

## Change protocol
1. Read the A0 full source inventory and Playwright baseline first; don't overrule current app with an assumed generic SaaS template.
2. For each UI slice write an acceptance scenario: persona, entry state, core task, button names, valid/empty/error/denied states, expected backend calls (unchanged), keyboard and accessibility, screen sizes.
3. Implement component and data flow; keep pure presentation separated from mutations; no request replay introduced by rerenders, no unsafe double-action, preserve existing cache/React Query semantics.
4. Build, run existing focused financial/U1 checks where navigation impacts those flows, execute Playwright visual + behavioral tests with before/after screenshots. Correct visual regressions before next slice.
5. Document performance (input latency where measurable), focus and scroll retention, offline dependencies, contrast and task completion; no invented numerical gains.
6. Commit small coherent commits, maintain exact-SHA evidence and release-no-merge state.

## Completion gate
A component is not "premium" until the user can orient, find correct data, perform the permitted action, see the outcome, recover from errors, and verify underlying records; the rendered test must demonstrate the path. No visual acceptance by compile alone.

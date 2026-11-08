# Salryn Antigravity Frontend Lane — Start Here

Date: 2026-11-08 (Asia/Manila)
Role: Antigravity, Frontend Architecture & UX Lead
Authority: Kirch Ivan Balite
Product: BASE Salryn POS retail with existing U1 Utang and owner analytics; NOT Café Yza.

## Active work

- [#19 — Full frontend study + Playwright rendered baseline](https://github.com/Kirjane/Salryn/issues/19) **EXECUTE NOW**.
- [#20 — Premium full UI/UX redesign](https://github.com/Kirjane/Salryn/issues/20) **QUEUED**, after #19 evidence and review.
- [PR #18 — latest corrected financial analytics](https://github.com/Kirjane/Salryn/pull/18): upstream stacked branch. Do not modify.
- [PR #10 — stabilization](https://github.com/Kirjane/Salryn/pull/10): frozen draft release candidate. Do not modify.

## Branch

`antigravity/salryn-premium-pos-ux`, created from corrected PR #18 SHA `ee16c9cc89055bcf7c2c155d20398c0bf43011ec` and containing four workspace skills. Do not update/merge upstream independently.

**Important**: Codex and Antigravity must not use the same writable worktree.

On a Windows machine with local base checkout at `C:\Salryn`, run PowerShell (stop on any mismatched condition):

```powershell
$ErrorActionPreference = 'Stop'
$Repo = 'C:\Salryn'
$Destination = 'C:\Salryn-Antigravity'
if (-not (Test-Path (Join-Path $Repo '.git'))) { throw 'STOP: C:\Salryn repository not found.' }
if (Test-Path $Destination) { throw 'STOP: destination already exists. Inspect; do not overwrite.' }
git -C $Repo fetch origin --prune
if ($LASTEXITCODE -ne 0) { throw 'STOP: git fetch failed.' }
$Existing = @(git -C $Repo branch --list 'antigravity/salryn-premium-pos-ux')
if ($Existing.Count -eq 0) {
  git -C $Repo worktree add --track -b antigravity/salryn-premium-pos-ux $Destination origin/antigravity/salryn-premium-pos-ux
} else {
  git -C $Repo worktree add $Destination antigravity/salryn-premium-pos-ux
}
if ($LASTEXITCODE -ne 0) { throw 'STOP: worktree creation failed.' }
git -C $Destination branch --show-current
git -C $Destination status --short
```

Open **`C:\Salryn-Antigravity` as the Antigravity project workspace** (not `C:\Salryn`). Confirm displayed branch and source HEAD. If the user has other worktree paths, adapt only the directory path after checking existing worktrees.

## Available Antigravity skills

All are source-controlled in workspace `.agents/skills` and use Antigravity's documented Agent Skills format; inspect Customizations to confirm discovery:
- `/salryn-frontend-system-audit`
- `/salryn-ux-information-architecture`
- `/salryn-playwright-visual-qa`
- `/salryn-safe-ui-delivery`

## First Antigravity instruction

> Work as Frontend Architecture & UX Lead for BASE Salryn POS. Execute issue #19 from the isolated `antigravity/salryn-premium-pos-ux` worktree. First inspect every frontend surface across startup, licensing, bootstrap, login, checkout, Sales, Utang, Reports, inventory, products, users, audit, settings, backup and internal/Terminal paths. Use all four workspace skills. Build a complete state/permission/navigation/IA map, prioritize UX defects, propose premium design-system architecture, add and actually run Playwright mocked rendered screenshots + keyboard/overflow/console checks. Record genuine integrated Host/physical blockers separately; never bypass entitlements. Commit the audit, runnable test suite and screenshot evidence on this branch, and open a DRAFT stacked PR targeting `codex/salryn-owner-analytics-w0-w4`. Do not touch PR #10/#18, backend money/license APIs, or ship a mass UI rewrite before A0 architecture review. Continue through all executable A0 work, not a plan-only response.

## Production safety

LAN is Future Feature and normal release remains Host-only. U1 is paid signed entitlement. Never check in real license or secrets, real customer/production database or raw console logs containing PII. Browser-mocked UI screenshots are **not** auth or finance proof. Playwright screenshots require an auditable deterministic fixture and exact SHA. No merge, release or distribution.

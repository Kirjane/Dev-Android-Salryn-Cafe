# PRE-I Surgical State + License UX Review

**Decision:** `SURGICAL_REVIEW_COMPLETE_IMPLEMENTATION_NOT_STARTED`

Review type: **report + design only**. No product code changes. No package/I1. No promotion.

---

## 1. Branch / base / final SHA

| Item | Value |
| --- | --- |
| Branch | `KIRCH-PRE-I-SURGICAL-STATE-LICENSE-UX-REVIEW` |
| Base branch | `KIRCH-PRE-I-CODE-PROVEN-QA-HARDENING` |
| Base SHA | `fed5ec23e9cb064af7d887e5411a661282b06625` |
| Final tip SHA | `8f75f059e8350bb91fca4699f93ab255d05bcb91` |
| Protected | Do not touch `KIRCH-SANDBOX`, `KIRCH-INSTALLER-M1` |

---

## 2. State path truth table

Evidence-first map from code (not operator memory).

### A. Database / app data roots

| Mode | Resolver | Root | Database | Backups / exports / logs / receipts / config |
| --- | --- | --- | --- | --- |
| **Development (`--dev`)** | `SalrynLocalLaunchModeResolver` → `DbPaths.CreateDevelopment(projectRoot)` | `{repo}\.salryn-dev` | `{repo}\.salryn-dev\data\salryn.db` | under `.salryn-dev\{backups,exports,logs,receipts,config}` |
| **Production** | `DbPaths.CreateProduction()` | `%ProgramData%\Salryn` → typically `C:\ProgramData\Salryn` | `C:\ProgramData\Salryn\data\salryn.db` | under `ProgramData\Salryn\...` |

Source:

- `src/Salryn.Infrastructure/Paths/DbPaths.cs` (`CreateDevelopment` = `projectRoot/.salryn-dev`; `CreateProduction` = `CommonApplicationData/Salryn`)
- `src/Salryn.Composition.Local/SalrynLocalLaunchModeResolver.cs` (`--dev` / `SALRYN_APP_ENV=Development`)
- `src/Salryn.Api.Local/LocalApiApplicationFactory.cs` logs: `[Salryn Dev] SQLite database: {composition.DbPaths.DatabasePath}`

**Kirch’s `--dev` path is project-root `.salryn-dev`, not LocalAppData.**

### B. LocalAppData (`%LOCALAPPDATA%\Salryn`)

| Path | Purpose | Used by |
| --- | --- | --- |
| `%LOCALAPPDATA%\Salryn\Licensing\license.json` | Signed Host (or Terminal) license document | `LocalLicenseFileStore.GetDefaultRootDirectory()` |
| `%LOCALAPPDATA%\Salryn\Licensing\installation-id.txt` | Stable installation id for machine code material | `LocalMachineFingerprint.GetOrCreateInstallationId()` |
| `%LOCALAPPDATA%\Salryn\Licensing\*.bak.json` | License replace backups | license file store |
| `%LOCALAPPDATA%\Salryn\Reports\NativePdf\...` | Native PDF report artifacts | `NativePdfArtifactRootPolicy` |

Documented in `SalrynLocalDataContract`:

- `LocalLicenseRootOwner = LocalApplicationData/Salryn/Licensing`
- `NativePdfReportRootOwner = LocalApplicationData/Salryn/Reports/NativePdf`

**LocalAppData is NOT the development SQLite root.** Resetting only LocalAppData leaves `.salryn-dev` fully intact.

### C. Browser / web client state

| Store | Keys / use | Scope |
| --- | --- | --- |
| `localStorage` | Terminal machine code + terminal license JSON (`terminalLicenseStorage.ts`) | Per-origin browser |
| `localStorage` | LAN client pairing/identity (`lanClientStorage.ts`) | Per-origin |
| `localStorage` / `sessionStorage` | Remote POS operator session tokens (`lanClientStorage.ts`) | Per-origin |
| `localStorage` | Hidden terminals list on Host LAN Control (`HostLanControlPage.tsx`) | Per-origin |
| Cookie | Host operator session cookie (`LocalApiSessionService.SessionCookieName`) | Loopback API origin; **process-scoped server store** |

No IndexedDB usage found under `src/Salryn.App.Web`.

**Important:** Host operator “signed in as X” is primarily **cookie + in-memory session** on `Salryn.Api.Local`. Browser localStorage does not store Host POS users/audit; those live in SQLite.

### D. Optional env / smoke isolation

| Variable | Effect |
| --- | --- |
| `SALRYN_PROJECT_ROOT` | Overrides repo root used to locate `.salryn-dev` |
| `SALRYN_LICENSE_DEVELOPMENT_MODE=1` | Opt-in Development license status (bypasses unlicensed gate) |
| `SALRYN_CORE_SMOKE_DEVELOPMENT_ROOT` | CoreSmoke isolation root (also enables development license mode) |

Development license is **not** automatic for every `--dev` launch. Default `--dev` still reads real LocalAppData license files unless env opt-in is set.

### E. Route order (host console)

From `App.tsx` `ConsoleApp`:

1. Loading / error shells  
2. **If Host license not ready** → `LicensePage` (blocks bootstrap/login/dashboard)  
3. **Else if first-run required** → `BootstrapSetupPage`  
4. **Else if not authenticated** → `LoginPage`  
5. **Else** → `AppShell` + role-gated pages  

`isHostLicenseReady`:

- requires `machineMatched`
- treats status `Development` (5) as ready
- otherwise requires `Licensed` + license type `Host`

---

## 3. Why Kirch’s reset was not fresh

### What the helper does

`tools/dev/Backup-And-Reset-DevSalrynState.ps1`:

- Renames **only** `%LOCALAPPDATA%\Salryn` → `%LOCALAPPDATA%\Salryn.backup-{stamp}`
- Does **not** delete (rename only) — good safety
- Does **not** touch:
  - `C:\Users\kirch\Salryn\.salryn-dev\**`
  - `C:\ProgramData\Salryn\**`
  - browser localStorage / sessionStorage
  - cookies (process must restart anyway)
- Comments claim: “Launch Salryn; expect License landing…” and “after reset … no users, no login”

### What Kirch actually launched

```text
dotnet run --project ...\Salryn.Api.Local -- --dev
```

→ SQLite root = **repo `.salryn-dev`**, not LocalAppData.

### Classification of what Kirch saw

| Observation | Classification | Explanation |
| --- | --- | --- |
| **DEVKIRCH TEST STORE** | **Stale `.salryn-dev` store_settings** (expected if previously seeded/bootstrapped) | Store name lives in SQLite under `.salryn-dev\data\salryn.db`, not in LocalAppData. Helper never touched it. |
| **Signed in as DEVKirch** | **Stale users + active host session** | Users table in `.salryn-dev`; login cookie against still-running or re-login to same DB. Not a LocalAppData bug. |
| **Audit logs** | **Stale SQLite `audit_logs`** | Same `.salryn-dev` DB footprint. |
| **User account / first-run admin** | **Stale or prior bootstrap/seed** | Explicit `dev-prelaunch-seed` or manual bootstrap creates admin in `.salryn-dev` (see `J20_4C_DEV_DATABASE_PRELAUNCH_SEED.md`). Default seed does not auto-create users every launch, but residual DB retains them. |
| **LAN footprints** | **Stale SQLite terminal/LAN tables + optional browser localStorage** | Terminal registry/pairing in DB; hidden terminals / LAN client records in browser storage. Helper touched neither. |
| **License landing skipped** | **Possible valid LocalAppData license still present OR Development mode env OR Host license already installed** | Helper *would* move license files if run successfully; if license still active after helper, either helper was not run, wrong machine profile, or Development mode env was set. If license was moved but DB remained, user would see License *or* re-import path then still land on bootstrap/login with old store. Kirch’s report of signed-in operator + store name strongly implies **DB never reset**. |

### Root defect (docs + naming, not silent product bug)

| Issue | Severity |
| --- | --- |
| Helper name `Backup-And-Reset-DevSalrynState` implies **all** dev state | High — operator misled |
| Helper comments claim “no users / no login” | High — false for `--dev` |
| Helper only resets **LocalAppData licensing/PDF root** | By design incomplete for `--dev` SQLite |
| PRE-I QA reports recommended this helper for “fresh License landing” without always stating `.salryn-dev` separately | High — instruction incompleteness |

**This is not primarily an App.tsx routing bug.** It is a **state-location mismatch** between operator instructions and launch mode.

---

## 4. Actual fresh-state definitions by mode

| Mode | What “fresh” must mean |
| --- | --- |
| **A. Fresh development state** | No (or empty) `{repo}\.salryn-dev`; no users/sessions/audit/LAN rows; optionally no LocalAppData license (see C); browser storage cleared for loopback origin; API process restarted; no `SALRYN_LICENSE_DEVELOPMENT_MODE` unless intentional. Expect: License (if unlicensed) → Bootstrap → Login. |
| **B. Fresh production state** | Empty/new `C:\ProgramData\Salryn`; license policy per installer (often keep or re-import); operator consent before DB wipe; not for casual QA. |
| **C. Fresh license-only state** | Move/rename `%LOCALAPPDATA%\Salryn\Licensing` only; **DB and users remain**. Expect: LicensePage if Host not ready; if Development mode, may still skip. |
| **D. Fresh DB, existing license** | Reset `.salryn-dev` or ProgramData DB only; keep LocalAppData license. Expect: skip License (if Host licensed + machineMatched), land on Bootstrap if no users. |
| **E. Fresh browser, existing DB** | Clear localStorage/sessionStorage/cookies for API origin; restart API (in-memory sessions die). Expect: Login again; store name and audit history still present. |

**Automated CoreSmoke “fresh”** already uses **isolated temp roots** — correct for code proof, **orthogonal** to Kirch’s real `.salryn-dev` + LocalAppData machine.

---

## 5. Proposed reset-helper repair design (DO NOT IMPLEMENT YET)

### Rename + split (recommended)

| Script (proposed names) | Scope | Use when |
| --- | --- | --- |
| `Backup-And-Reset-LocalAppData-Licensing.ps1` | Rename `%LOCALAPPDATA%\Salryn` (or only `Licensing` subfolder) | Fresh license / machine-id rebind tests |
| `Backup-And-Reset-DevProjectState.ps1` | Rename `{repo}\.salryn-dev` → `.salryn-dev.backup-{stamp}` | **Default for `--dev` QA “fresh store”** |
| `Backup-And-Reset-AllDevState.ps1` | Calls both + prints browser cookie/localStorage clear checklist | Full PRE-I manual fresh checklist |

### Shared rules (all helpers)

1. **WhatIf** support  
2. **Rename, never `Remove-Item`**  
3. Refuse if Salryn.Api.Local / WinForms running  
4. Print exact source + backup paths  
5. Print **exact next launch command** and **expected first screen** for that mode  
6. Never claim “no users” unless `.salryn-dev` (or production root) was actually reset  
7. Document browser steps: hard reload, clear site data for `http://127.0.0.1:5299` if sessions stick  

### Operator truth card (for future handoff)

```text
--dev DB:     <repo>\.salryn-dev\data\salryn.db
License:      %LOCALAPPDATA%\Salryn\Licensing\license.json
Machine id:   %LOCALAPPDATA%\Salryn\Licensing\installation-id.txt
Prod DB:      %ProgramData%\Salryn\data\salryn.db
Host session: cookie + API process memory
LAN client:   browser localStorage/sessionStorage
```

### What not to do in the repair lane

- Do not auto-delete ProgramData  
- Do not auto-clear browser storage from PowerShell (document only, or optional Edge/Chrome instructions)  
- Do not make `--dev` write SQLite into LocalAppData (path contract change is product, not a quick fix)  
- Do not conflate CoreSmoke isolated roots with operator machine state  

---

## 6. Current License page critique

File: `src/Salryn.App.Web/src/pages/LicensePage.tsx`

Kirch’s rejection is justified by the current implementation pattern.

| Problem | Evidence in current page |
| --- | --- |
| **Screams AI / SaaS launch** | Dark navy `#050815` full-bleed stage, multi-layer radial gradients, contour rings, grid mask, gradient text wordmark |
| **Too loud** | Giant `SALRYN` gradient H1 (~5.6rem), glowing CTAs, sparkles icon, cyan/violet glow blobs |
| **Forced marketing** | “Less manual store work…”, “Sell clearly / Know your stock / Stay recoverable”, feature pill cloud |
| **Generic template** | Hero + glass card + step rail + value columns + pill badges — interchangeable “AI product landing” |
| **Weak personality** | Personality attempted via chrome and slogans, not via craft restraint or local-store voice |
| **Terms feel thin** | Eight short bullets in a scroll box; serious topics compressed; “beta” compliance one-liner without ownership clarity |
| **Over-iconed** | lucide: Sparkles, WifiOff, Layers3, ShoppingBag, Database, HardDriveDownload, ShieldCheck, etc. |
| **Wrong first job** | Page sells product features before calmly proving “this device needs a license file” |
| **Active state still theatrical** | “License Active” huge headline + emerald glow pill; maintenance buried under marketing chrome |

What is **worth keeping** (behavior, not chrome):

- Machine Code copy action  
- Activate → Terms → Import step order  
- Terms required before install  
- Replace path: terms re-ack + replace consequence  
- Host vs Terminal license distinction  
- Offline-local framing (conceptually)  
- Support email contact  
- Collapsible technical details pattern (for active state)  

---

## 7. Minimalist visual direction

### Feeling

Calm local POS certificate desk — not a startup launch site.

References (discipline, not cloning):

- Apple System Settings density and quiet chrome  
- Stripe spacing (generous, aligned, few elements)  
- Vercel restraint (almost no decoration)  
- Notion section calm  
- Old POS reliability (clear actions, no hype)  

### Palette (proposed)

| Role | Token | Notes |
| --- | --- | --- |
| Page bg | `#F4F5F7` or `#F7F7F5` | Warm light gray, not pure white |
| Surface | `#FFFFFF` | Cards |
| Text | `#111827` / `#374151` / `#6B7280` | Primary / body / muted |
| Border | `#E5E7EB` | 1px only |
| Accent | `#1F4B7A` or deep ink blue | One accent, no gradient CTA |
| Success | soft green text, no neon glow | Active license |
| Danger | standard red text | Errors only |

**No** multi-stop gradients, **no** glassmorphism, **no** radial blobs, **no** glowing dots, **no** cyan/violet “AI night club”.

### Typography

- System UI stack / Inter / Segoe UI  
- Wordmark: medium weight, tracking modest (`0.08em` max), not black condensed 5rem  
- One page title ~22–28px semibold  
- Body 14–15px, line-height 1.55  
- Mono only for machine code  

### Spacing / density

- Max content width ~880–960px (not 7xl circus stage)  
- 24–32px section gaps  
- Cards: 20–24px padding, `rounded-xl` (12–16px), shadow optional single soft `0 1px 2px` or none  

### Motion

- **None** beyond browser default focus  
- No hover lift, no shimmer, no animated gradients  

### Borders / shadows

- Prefer border over shadow  
- Shadow only on modal overlay surface if needed  

---

## 8. Proposed License page information architecture

### Fresh / unlicensed (primary)

```text
┌──────────────────────────────────────────────────────────┐
│  Salryn                          by Kirjane Labs         │
│  Status: Not activated                                   │
├────────────────────────────┬─────────────────────────────┤
│  Activate Salryn           │  On this device             │
│  This device needs a       │  Machine Code               │
│  signed local license…     │  [ SALRYN-MC-… ] [Copy]     │
│                            │                             │
│  [ Import license ]        │  Support                    │
│  [ How activation works ]  │  salryn.info@gmail.com      │
│                            │                             │
│                            │  Salryn by Kirjane Labs     │
│                            │  Technology Lead: …         │
└────────────────────────────┴─────────────────────────────┘
```

- Left: purpose + primary actions  
- Right: machine code + quiet support/credits  
- **No** feature grid, **no** value columns, **no** giant hero wordmark  

### Active / Host licensed

```text
Salryn is activated
Host license · {Customer/Store} · Device matched

[ Replace license ]     [ Show technical details ▾ ]
```

Technical details collapsed: license id, store id, dates, machine code, application id.

### Replace license

Same sparse modal as first install, with extra consequence checkbox — not a second marketing page.

---

## 9. Proposed copy

### Fresh landing

| Element | Copy |
| --- | --- |
| Status | Not activated |
| Title | Activate Salryn |
| Subtitle | This device needs a signed local license before setup can continue. |
| Body | Salryn runs locally on this device. Send this Machine Code to Kirjane Labs to receive a signed license file. |
| Primary button | Import license |
| Secondary | Copy Machine Code |
| Helper | How activation works (link opens short modal or inline details, not a hype section) |
| Support | Questions: salryn.info@gmail.com |

### Active

| Element | Copy |
| --- | --- |
| Title | Salryn is activated |
| Body | This Host is licensed for local use on this device. |
| Action | Replace license |

### Avoid

- “Unlock your workflow”  
- “Sell clearly”  
- “Ready in three steps” with sparkles  
- “Less manual store work. More control where it matters.”  
- Feature pill walls  

---

## 10. Proposed Terms / Services text

**Title:** Salryn Local Activation Terms (Beta)

**Intro:**  
These terms apply when you install a signed Salryn license on this device. They are written for beta use. They are not a full commercial contract and do not replace any signed agreement with Kirjane Labs.

**1. Local license**  
A Salryn license is a signed file that authorizes use of Salryn software features on a specific device. It is not a cloud subscription activation. Installation stores the license on this computer.

**2. Device binding**  
Host licenses are bound to this device’s Machine Code. Moving Salryn to another computer requires a new or reissued license. Do not attempt to forge, copy, or alter license signatures.

**3. Data ownership**  
Store data created in Salryn (products, sales, stock, users, settings) remains on the Host workstation under operator control. Kirjane Labs does not automatically receive your store database when you install a license.

**4. Backups**  
You are responsible for creating and safeguarding portable backup files. License activation does not replace a backup routine. Loss of the Host computer without backups can mean loss of store history.

**5. Hardware limits**  
This beta supports documented local workflows only (including keyboard-wedge barcode input and Windows print-queue receipts where enabled). Salryn does not claim universal printer, cash-drawer, or hardware-bundle certification.

**6. Receipts and compliance**  
Salryn may produce local operational receipts and engineering records. This beta does **not** claim BIR accreditation, official receipt certification, e-sales submission readiness, or legal tax-authority approval. Do not present Salryn as government-certified POS output unless a separate accredited program says so in writing.

**7. LAN terminals**  
Remote POS or read-only terminals, when licensed and paired, connect to this Host over the local network. Each terminal may require its own Terminal license. Host licenses do not automatically authorize unlimited untrusted devices.

**8. Support boundaries**  
Beta support is provided through Kirjane Labs (salryn.info@gmail.com) on a best-effort basis. Support does not guarantee uptime, recovery from missing backups, or advice that substitutes for your accountant or counsel.

**9. Beta acceptance**  
By installing a license you acknowledge Salryn is under active development, features may change, and you will not represent beta builds as final production-certified systems.

**Checkbox:**  
I have read and accept the Salryn Local Activation Terms (Beta).

**Replace-license extra checkbox:**  
I understand this replaces the active local license on this device.

---

## 11. Proposed modal flow

Sparse dialog (light surface, dark text, no gradient header):

| Step | Content | Actions |
| --- | --- | --- |
| 1 Machine Code | Code + Copy + one sentence on emailing Kirjane Labs | Cancel · Continue |
| 2 Terms | Scrollable terms (section 10) + accept checkbox | Back · Continue |
| 3 Import | Textarea for signed license only + Install | Back · Install |

Rules:

- Import entry still lands on Terms first if not yet accepted this session  
- No marketing inside modal  
- No step “sparkle” chrome  
- Error messages stay plain (existing friendly mapper can remain)  

---

## 12. Product credits treatment

**Do not** use a competing glass card with team org chart as visual focus.

Footer (small, muted, under main cards):

```text
Salryn by Kirjane Labs
Technology Lead: Kirch Ivan Balite
```

Optional one-line team credit on a single quiet line, or behind “Credits” disclosure.

`ProductCreditsCard` should be demoted to footer density in the implementation lane — not a second hero.

---

## 13. What not to implement (this review)

- Any LicensePage UI rewrite in this branch  
- Path migration of SQLite into LocalAppData  
- Auto ProgramData wipe  
- Fake browser proof  
- Package/I1  
- Promotion  
- Broad design system rewrite  

---

## 14. Future implementation lane scope

Recommended **two sequential lanes** (not one mega-PR):

### Lane 1 — `PRE-I-DEV-STATE-RESET-TRUTH`

- Split/rename reset scripts as in section 5  
- Update PRE-I handoff docs with truth card  
- Optionally: API startup log line that always prints **DB root + license root** in human form  
- Smoke/doc guard: helper comments must not claim “no users” without `.salryn-dev` reset  
- No License visual redesign in Lane 1  

### Lane 2 — `PRE-I-LICENSE-PAGE-MINIMALIST-REBUILD`

- Implement visual/copy/terms/modal per sections 7–12  
- Keep behavioral gates (terms required, replace re-ack, machine code, Host vs Terminal)  
- Update source guards that currently pin AI marketing strings  
- Manual screenshot acceptance against design checklist  
- No path/reset work unless Lane 1 done  

---

## 15. Risks

| Risk | Note |
| --- | --- |
| Operators keep using old helper name | Must rename aggressively and delete misleading comments |
| Development mode env forgotten on machine | Can skip License while DB is dirty — document in truth card |
| Design lane rewrites break copy guards | Expect guard updates as part of Lane 2 |
| Terms still not legal advice | Explicit beta framing required |
| Personality under-delivery | Restraint can feel “empty” if spacing is wrong — design review before merge |

---

## 16. Final recommendation

1. **Stop churn** on more License marketing polish until Lane 2 has a written design lock (this document).  
2. **Treat Kirch’s failed fresh run as instruction defect + incomplete helper**, not as “License page failed.”  
3. Implement **reset truth first** (Lane 1), then **minimalist License rebuild** (Lane 2).  
4. Keep automated code-proven fresh tests on **isolated temp roots**; do not pretend they reset operator `.salryn-dev`.  

**Decision:** `SURGICAL_REVIEW_COMPLETE_IMPLEMENTATION_NOT_STARTED`

---

## Appendix A — Evidence index

| Topic | Path |
| --- | --- |
| Dev/prod DB roots | `src/Salryn.Infrastructure/Paths/DbPaths.cs` |
| `--dev` resolver | `src/Salryn.Composition.Local/SalrynLocalLaunchModeResolver.cs` |
| Data contract | `src/Salryn.Infrastructure/Paths/SalrynLocalDataContract.cs` |
| License file root | `src/Salryn.Infrastructure/Licensing/LocalLicenseFileStore.cs` |
| Machine / installation id | `src/Salryn.Infrastructure/Licensing/LocalMachineFingerprint.cs` |
| Development license opt-in | `src/Salryn.Infrastructure/Licensing/LocalLicenseService.cs` |
| Route order | `src/Salryn.App.Web/src/App.tsx` |
| Current License UI | `src/Salryn.App.Web/src/pages/LicensePage.tsx` |
| Reset helper | `tools/dev/Backup-And-Reset-DevSalrynState.ps1` |
| Dev seed residue | `00-HANDOFF/J20_4C_DEV_DATABASE_PRELAUNCH_SEED.md` |
| Browser storage | `terminalLicenseStorage.ts`, `lanClientStorage.ts`, `HostLanControlPage.tsx` |

---

*Kirch remains project lead. Implementation not started in this branch.*

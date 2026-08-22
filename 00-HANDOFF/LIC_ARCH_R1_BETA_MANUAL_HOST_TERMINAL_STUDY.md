# LIC-ARCH-R1 — Beta Manual Host/Terminal Licensing Architecture Study

**Status:** `STUDY_COMPLETE` (report-only; no implementation)

**Branch:** `KIRCH-LIC-ARCH-R1-BETA-MANUAL-HOST-TERMINAL-STUDY`  
**Base:** `KIRCH-LIC-FINAL-OFFLINE-LICENSE-LAN-ENTITLEMENTS` @ `4979b525d5a721568ddf4785f27f774e4c9cacaa`  
**Compared against:**
- Backup UI parked: `KIRCH-POS-BACKUP-UI1-SAFE-BACKUP-RESTORE` @ `49ef49c440ec6ad655a85c8c5394c2113052f546`
- Baseline isolation: `KIRCH-REPAIR-CORESMOKE-DEVDB-ISOLATION` @ `cf1d925a4ce05083f2263f7fd1b958d17db538ef`

**Author role:** Senior software licensing architect / security reviewer / repo auditor  
**Project lead:** Kirch  
**Constraint:** Repo-backed evidence only. No code implementation in this study. Installer delayed.

**Protected refs (must remain unchanged):**
- `KIRCH-SANDBOX` = `1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `KIRCH-INSTALLER-M1` = `6b9c46676390b8a86a989eca600cb02e406ce865`

**Official beta contact:** `salryn.info@gmail.com`

---

## 1. Executive answers (mission checklist)

| # | Question | Answer (evidence-backed) |
|---|----------|---------------------------|
| 1 | What licensing architecture exists now? | Offline-first local signed JSON license; public-key verify only; single-file store under LocalAppData; status/install/entitlements APIs; passive enforcement helper; admin License UI. |
| 2 | What is implemented in LIC-FINAL? | Schema extensions (ApplicationId, StoreId, AllowedLanTerminals, SupportExpiresAt, UpdatesUntil), RSA public-key verifier, machine fingerprint helper, License page + nav/permissions, backup license-boundary copy, M3 source guards extended. **Ungated / not production-proven.** |
| 3 | What is missing or weak? | Host/Terminal license types; terminal licenses; LAN count enforcement wired to license; product-surface enforcement; strong fingerprint; activation contact UX; external generator; cloud deferred correctly but reissue process undefined. |
| 4 | Beta licensing for first five clients? | Manual email activation: machine code → email `salryn.info@gmail.com` → signed license file from private ops tool → offline import. No cloud. |
| 5 | Host vs LAN terminal licenses? | Separate machine-bound licenses: one Host per store workstation; one Terminal per operational LAN device. Never copy host license to terminal. |
| 6 | What must live in Salryn repo? | Verifier, public key, contracts, local store, status/install/entitlements, UI machine code + contact, host/terminal validation, LAN entitlement enforcement, tests/guards. |
| 7 | What must live outside Salryn repo? | Private signing key, license generator CLI/UI, customer issuance records, audit trail of issued licenses. Proposed: private `Kirch-Nairu/Salryn.LicensingOps`. |
| 8 | Future Vercel/cloud backend? | Activation codes, license DB, limits, reissue/reset, upgrades, key custody, admin portal, audit trail. Deferred after beta manual path works. |
| 9 | Implement next (branch order)? | See companion `LIC_ARCH_R1_IMPLEMENTATION_PLAN.md`. Installer remains blocked until architecture locked. |
| 10 | What should NOT be implemented yet? | Installer (I1), cloud activation, payment/subscription, 16-digit key as the license, private key in POS repo, production LAN without terminal entitlement enforcement, RMO/BIR licensing claims. |

---

## 2. Current repo implementation (what exists)

### 2.1 Layer map

| Layer | Location | Role |
|-------|----------|------|
| Contracts | `src/Salryn.Core/Services/Licensing/LicenseContracts.cs` | Status, edition, features, document, validation result, install request/response, enforcement decision, entitlements response |
| Policy constants | `src/Salryn.Core/Services/Licensing/LocalLicenseContractPolicy.cs` | Offline-first principles; no private key in repo; external issuer allowed |
| Interfaces | `ILocalLicenseService`, `ILocalLicenseEnforcementService` | Status, install, HasFeature; RequireFeature / IsFeatureAllowed |
| File store | `LocalLicenseFileStore` | Default `LocalApplicationData/Salryn/Licensing/license.json`; path-safe file names; atomic replace + `.bak.json` |
| Validator | `LocalLicenseValidator` | Schema, identity, features, machine match, expiry/grace, signature required |
| Signature | `LocalLicenseSignatureVerifier` | Embedded RSA **public** key PEM; SHA256 + PKCS1; canonical payload JSON |
| Fingerprint | `LocalMachineFingerprint` | SHA256 of `MachineName\|UserName\|OSVersion.Platform` |
| Service | `LocalLicenseService` | Default wires fingerprint + public-key verifier; install canonicalize + field allowlist |
| Enforcement | `LocalLicenseEnforcementService` | Feature allow/deny from current status |
| API guard | `ApiLicenseGuard.RequireLicensedFeatureAsync` | 403 `License.FeatureDenied` helper — **defined, not applied to product endpoints** |
| Endpoints | `LocalLicenseEndpoints` | `GET /api/license/status`, `GET /api/license/entitlements`, `POST /api/license/install` |
| Composition | `LocalApiApplicationFactory` | `app.MapLocalLicenseEndpoints()` |
| Path contract | `SalrynLocalDataContract` | License path must preserve across update |
| Frontend | `license.ts`, `LicensePage.tsx`, nav, permissions, App route, AppShell icon | Admin-only import + status UI |
| Backup boundary | `BackupRestorePage.tsx` + B1 guard | States license file is **outside** DB backup package |
| Tests | M1/M2/M3 checks; CoreSmoke hooks `licensing-m1-*`, `licensing-m2-*`, `licensing-m3-*` | Behavioral + source guards |
| Docs | LICENSING_M1/M2/M3 + LIC_FINAL reports | Milestone history |

### 2.2 License document schema (current)

`LocalLicenseDocument` (schema version 1):

| Field | Present | Notes |
|-------|---------|-------|
| LicenseId | Yes | Required by validator |
| CustomerName | Yes | Required by validator |
| Edition | Yes | Trial / Local / Professional / Enterprise |
| IssuedAt | Yes | |
| ExpiresAt | Yes nullable | Soft expiry; grace optional via options |
| MachineBinding | Yes nullable | **Empty binding matches any machine** |
| Features | Yes | Known set only: Pos, Rmo, Reports, Users, Backup, Licensing, LanTerminals |
| Signature | Yes | Required for Licensed status |
| SchemaVersion | Yes | Must be `1` |
| ApplicationId | Yes (LIC-FINAL) | Displayed; **not required / not fixed-value checked** |
| StoreId | Yes (LIC-FINAL) | Displayed; **not required for validity** |
| AllowedLanTerminals | Yes (LIC-FINAL) | Must not be negative; **not enforced against live LAN pairing** |
| SupportExpiresAt | Yes (LIC-FINAL) | Display only |
| UpdatesUntil | Yes (LIC-FINAL) | Display only |
| **LicenseType** | **No** | Host vs Terminal not modeled |
| **HostLicenseId** | **No** | Terminal→host binding not modeled |
| **TerminalId** | **No** | Per-terminal identity not in license |

### 2.3 Feature vocabulary (current)

```
Pos, Rmo, Reports, Users, Backup, Licensing, LanTerminals
```

Beta target language (POS.Core, LAN.Host, LAN.Terminal) is **not** the current enum. Migration mapping is an implementation decision for beta branch (extend features vs map names).

### 2.4 Signature model (current)

- Payload: canonical JSON of identity + edition + dates + machine + features + schema + ApplicationId + StoreId + AllowedLanTerminals + SupportExpiresAt + UpdatesUntil (**signature field excluded**).
- Algorithm: RSA PKCS#1 v1.5 + SHA-256 over UTF-8 payload bytes.
- Public key embedded in `LocalLicenseSignatureVerifier`.
- Private key: **not present** in repo (source scan for `BEGIN PRIVATE KEY` / `BEGIN RSA PRIVATE KEY` empty on LIC-FINAL tree).

### 2.5 Storage and install path

1. Admin with `Settings.Manage` pastes JSON into License page (no file path upload).
2. `POST /api/license/install` with `{ licenseJson }` only (loopback + permission).
3. Service rejects unsupported fields and any property name containing `path`.
4. Validate (signature, machine, expiry, schema) **before** write.
5. Replace under controlled root; prior file → system-named `.bak.json`.
6. Re-read and re-validate after write.

### 2.6 What LIC-FINAL added (relative to Backup UI park)

Commits from `49ef49c` → `4979b52` (summary):

- Offline license contract extensions
- Machine fingerprint helper
- Public-key verifier + default service wiring
- Identity/entitlement field validation surface
- Frontend license API client + License page + admin nav/permission/route/shell
- Backup UI copy: license outside DB package
- M3 source guards extended for LIC-FINAL artifacts
- Report: `CODE_COMPLETE_UNGATED_VERIFICATION_PARKED` (gates NOT RUN / NEEDS SOL)

### 2.7 Enforcement reality (critical)

| Capability | Status |
|------------|--------|
| Feature decision service | Implemented |
| Entitlements API (read decisions) | Implemented |
| `ApiLicenseGuard` on checkout/users/reports/LAN/backup APIs | **Not wired** (only defined; grep shows definition + test source guard only) |
| Startup hard lock when unlicensed | **Not present** |
| LAN `AllowedLanTerminals` vs `RemotePosCapacityPolicy` | **Disconnected** — capacity uses hardcoded defaults (5 paired / 3 sessions / hard cap 10), not license field |
| Host vs Terminal license role | **Not present** |
| Product UI gated by license | **Not present** (License page is management only) |

M3 report states intentionally: passive boundary only; no lockout of existing product flows.

### 2.8 LAN capacity vs license

`RemotePosCapacityPolicy`:

- `DefaultMaxPairedTerminals = 5`
- `DefaultMaxActiveSessions = 3`
- `HardCap = 10`

This is **runtime abuse/capacity policy**, not commercial entitlement. Host LAN Control page exists and is operational in code; License UI still labels LAN as Coming Soon / internal preview for **production claim**.

### 2.9 Backup interaction (current)

- Controlled backup package contains **database only** (`salryn.db` + manifest) — no license file.
- UI and B1 guard require copy: license lives outside DB backup package.
- Installer path contract says license **must preserve across update** (separate from backup package).

### 2.10 Development mode

- `AllowDevelopmentMode` is **opt-in** on validator options.
- Default production path (`LocalLicenseService` default validator) does **not** enable development mode.
- Missing license → `Unlicensed`, not Development.

### 2.11 Grace vs enforcement inconsistency

- Validator `IsFeatureEnabled` treats `Grace` as feature-enabled.
- `LocalLicenseEnforcementService` treats `Grace` as **denied** (`ExpiredLicense`).

Document this for beta implementation (pick one policy).

---

## 3. Current weaknesses

### 3.1 Security / binding

| Weakness | Severity | Detail |
|----------|----------|--------|
| Weak fingerprint | High | Only MachineName + UserName + Platform — easy collision / weak uniqueness; user rename changes code |
| Empty MachineBinding accepted | High | Blank binding validates as machine-matched for any PC |
| No LicenseType | High | Host license can be installed on a “terminal” role device with no type rejection |
| Single license file model | High | No first-class terminal license document or multi-license store |
| LAN count display-only | High | `AllowedLanTerminals` does not cap pairing |
| No StoreId hard requirement | Medium | Cross-store license reuse not blocked by store identity check |
| ApplicationId not fixed | Medium | Foreign product license could pass if signed by same key |
| No revocation | Medium (offline expected) | Offline model cannot force-pull revoke without reissue process |
| Passive enforcement | High for commercial | Unlicensed machines can still run POS features until gates applied |
| Grace/enforcement mismatch | Low–Medium | Inconsistent feature allow under Grace |
| Ungated verification | Process | LIC-FINAL gates not run; parked unproven |

### 3.2 Product / UX

| Weakness | Detail |
|----------|--------|
| No salryn.info@gmail.com activation copy | Beta email flow not in UI |
| Fingerprint not presented as “machine code” for email | Admin sees hex fingerprint; no guided activation steps |
| No reissue / hardware move instructions | Support process external only |
| Feature names vs commercial language | Pos vs POS.Core; LanTerminals vs LAN.Host/LAN.Terminal |

### 3.3 Operational / org boundary

| Weakness | Detail |
|----------|--------|
| No in-repo generator (correct) | But private **Salryn.LicensingOps** not specified/built yet |
| No issuance audit format | Customer records, LicenseId uniqueness, StoreId registry undefined in POS repo (should stay outside) |
| Cloud deferred but reissue not designed | Beta needs manual reissue SOP |

### 3.4 What is already strong (keep)

- Offline-first policy constants and source guards against cloud/payment/subscription in licensing slice
- Public key only in POS repo
- Fail-closed unsigned / missing verifier / invalid signature
- Install field allowlist and path rejection
- Controlled store paths; no caller paths
- Admin-only install permission
- Loopback operator guard on license APIs
- License outside DB backup (reduces accidental license clone via restore to wrong machine — still need re-import if machine differs)
- M1/M2/M3 smoke hooks exist for future gating

---

## 4. Beta manual licensing architecture (target)

### 4.1 Commercial model (locked for this study)

- Offline-first POS
- Permanent one-time software license
- One store
- One licensed **host** POS workstation
- Additional LAN operational terminals = permanent paid add-ons
- First five beta clients: **manual licensing**
- Cloud backend deferred for beta, not abandoned
- No continuous internet for normal operation
- Private signing material controlled by Kirjane Labs; never ships with app

### 4.2 Host license schema (target)

```json
{
  "SchemaVersion": 2,
  "LicenseType": "Host",
  "LicenseId": "lic_host_...",
  "CustomerName": "...",
  "StoreId": "store_...",
  "ApplicationId": "Salryn.POS",
  "MachineBinding": "<machine-code>",
  "Edition": "Local",
  "Features": ["Pos", "Reports", "Backup", "Licensing", "Users", "LanTerminals"],
  "AllowedLanTerminals": 0,
  "IssuedAt": "...",
  "ExpiresAt": null,
  "SupportExpiresAt": "...",
  "UpdatesUntil": "...",
  "Signature": "..."
}
```

**Host unlocks (when Licensed + machine match):**

- POS core operations (map to `Pos` / future POS.Core)
- Reports
- Backup
- Licensing management UI
- Users (if commercially included)
- `LAN.Host` / `LanTerminals` **only if** `AllowedLanTerminals > 0` (or explicit host LAN feature)

**Host must not unlock:**

- Pure terminal-only surface as substitute for host identity
- Unlimited terminals when `AllowedLanTerminals` is 0

### 4.3 Terminal license schema (target)

```json
{
  "SchemaVersion": 2,
  "LicenseType": "Terminal",
  "LicenseId": "lic_term_...",
  "CustomerName": "...",
  "StoreId": "store_...",
  "HostLicenseId": "lic_host_...",
  "TerminalId": "term_...",
  "ApplicationId": "Salryn.POS",
  "MachineBinding": "<terminal-machine-code>",
  "Features": ["LanTerminals"],
  "IssuedAt": "...",
  "ExpiresAt": null,
  "SupportExpiresAt": "...",
  "UpdatesUntil": "...",
  "Signature": "..."
}
```

**Terminal unlocks:**

- LAN terminal operational surface only
- Must not grant host-only admin licensing import for other devices beyond its role policy (import still admin-permissioned; type must be Terminal)

**Terminal must not unlock:**

- Standalone full host POS as if it were the licensed host (unless product explicitly allows offline terminal-local mode later — **out of beta scope**)

### 4.4 Manual activation flow (first five clients)

1. Customer installs Salryn (when installer is later unlocked; for now manual/dev deploy).
2. App shows **Machine Code** (human-friendly presentation of fingerprint) on License page when Unlicensed/Invalid/mismatched.
3. App shows contact: **email `salryn.info@gmail.com`** with required fields checklist:
   - Customer / business name
   - Store name / StoreId if assigned
   - Machine code
   - Request type: Host or Terminal add-on
   - For terminal: Host LicenseId (if already issued)
4. Customer emails Kirjane Labs.
5. Labs issues signed license via **private** generator (outside Salryn POS repo).
6. Customer pastes JSON (or controlled import) into License page.
7. Salryn verifies offline (public key + machine + type + schema).
8. Role unlocked by LicenseType + Features.

**No cloud. No 16-digit key as the license.** Optional future activation code is cloud-only later.

### 4.5 LAN terminal control rules (target host rejection)

Host must reject a terminal that:

| Condition | Reject |
|-----------|--------|
| Wrong StoreId | Yes |
| No terminal license | Yes |
| MachineBinding ≠ terminal machine | Yes |
| Paired terminals > host `AllowedLanTerminals` | Yes |
| Host license presented as terminal license | Yes |
| Terminal license used on another machine | Yes |
| Terminal LicenseType missing / wrong | Yes |
| HostLicenseId ≠ this host’s LicenseId (when required) | Yes |

**Beta production LAN** remains **blocked for commercial claim** until branch `KIRCH-LIC-LAN-TERMINAL-ENTITLEMENT-ENFORCEMENT` lands **and** LAN itself is separately accepted. Capacity policy alone is not entitlement.

### 4.6 Multi-file local store (recommended)

Today: single `license.json`.

Beta recommendation:

| Role | File | Notes |
|------|------|-------|
| Host | `license.json` or `host-license.json` | One active host license |
| Terminal device | `license.json` as **its** terminal license | Terminal PC never stores host license as its operational license |
| Host store of terminal proofs (optional later) | Host DB or controlled folder of terminal license digests | Needed for host-side pairing proof without trusting client self-assertion alone |

Minimum beta:

- Device-local single active license of the correct type is enough if host pairing protocol verifies terminal-presented signed terminal license blob.

### 4.7 Schema version strategy

- Keep reading schema v1 for transition if needed.
- Prefer **schema v2** for LicenseType + HostLicenseId + TerminalId in signing payload.
- Signing payload **must** include every commercially meaningful field so tampering is detectable.

---

## 5. Internal generator repo boundary

### 5.1 Decision

**YES** — internal license generator lives **outside** Salryn POS repo.

Proposed private repo: `Kirch-Nairu/Salryn.LicensingOps`

### 5.2 Salryn POS repo may contain

- Public verification key
- Verifier implementation
- Contracts and validation
- Local storage + install API + UI
- Enforcement
- Source guards forbidding private keys / cloud dependencies
- Spec references / handoff docs only

### 5.3 Salryn POS repo must never contain

- Private signing keys
- Key generation scripts that emit private keys into the tree
- Production license issuance tools that embed private keys
- Customer PII issuance database

### 5.4 LicensingOps responsibilities

- Hold private key (HSM/file ACL/secret store — process choice)
- Issue Host and Terminal licenses
- Canonical signing payload matching POS verifier
- Customer / store / machine registry (offline spreadsheet OK for first five)
- Reissue / hardware replacement SOP
- Audit log of issued LicenseIds
- Optional export of signed JSON only

Detailed non-code spec: branch `KIRCH-LIC-OPS-PRIVATE-GENERATOR-SPEC` (doc-only in POS or pure external repo).

---

## 6. Future cloud / Vercel architecture (deferred)

**Not required for first five beta clients.**

Future `KIRCH-CLOUD-LICENSING-VERCEL-ACTIVATION` should handle:

| Capability | Purpose |
|------------|---------|
| Activation code generation | Human-friendly short codes ≠ full license |
| License record database | StoreId, machines, terminal count, history |
| Activation limits | Prevent unlimited activations |
| Machine reset / reissue | Controlled rebinding after hardware failure |
| Terminal upgrade purchases | Raise AllowedLanTerminals |
| Private key custody | Sign only in secure backend |
| Audit trail | Compliance / support |
| Customer / admin portal | Self-serve status; admin ops |

Local app remains offline verifier of signed files (or signed activation responses cached offline). Continuous internet must not be required for daily POS.

---

## 7. Threat model

| Threat | Current mitigation | Missing mitigation | Beta acceptable? | Required future mitigation |
|--------|--------------------|--------------------|------------------|----------------------------|
| Copy license file to another PC | MachineBinding check when non-empty | Stronger fingerprint; **reject empty binding** | Acceptable only if binding always set | Hardware-ish factors; reissue portal |
| Edit license JSON | RSA signature over canonical payload | Ensure all commercial fields in payload (LicenseType, HostLicenseId, TerminalId) | Yes if signed issuance only | Same + schema v2 |
| Share licenses between stores | Weak StoreId (optional today) | Require StoreId; host/terminal store match | Partially — process + require fields | Cloud registry of StoreId |
| Host license as LAN terminal | None (no LicenseType) | Host vs Terminal type enforcement | **No** for multi-terminal beta | Type checks + host pairing proof |
| Terminal license as host | None | Type + feature matrix | **No** if terminals sold | Type checks |
| Exceed LAN terminal count | Hardcoded capacity only | Cap pairing by `AllowedLanTerminals` | **No** for paid add-ons | Entitlement + capacity min() |
| Replace machine fingerprint | Weak algo | Improve material; document user/profile sensitivity | Acceptable with improved fingerprint for beta | Better binding; reissue flow |
| Private key leakage | Not in POS repo; guards | LicensingOps key custody | N/A if keys external | HSM, rotation, access control |
| Dev mode in production | Explicit option only; default off | CI/source guard production composition never sets AllowDevelopmentMode | Yes if composition audited | Build-time strip / config assert |
| Rollback to older app version | None | Accept residual risk offline | Yes residual for beta | Min app version in license; update channel |
| Backup/restore license confusion | License outside DB backup | Separate license export SOP; restore UI warnings | Yes with current boundary | Optional licensed export tool later |
| Unauthorized import (manager/cashier) | Settings.Manage + loopback | Keep cashier/manager out of license page (already) | Yes | Audit log of install events |
| Offline revocation limits | N/A | Support/process reissue; short SupportExpiresAt if needed | Acceptable for permanent license model | CRL/online check optional later |

---

## 8. Exact no-code decisions (for Kirch)

| # | Decision | Recommendation | Rationale |
|---|----------|----------------|-----------|
| 1 | Defer cloud for first five beta clients? | **YES** | Manual signed machine-bound licenses suffice; offline-first preserved |
| 2 | Internal generator outside Salryn repo? | **YES** | `Salryn.LicensingOps` private; key hygiene |
| 3 | Private signing key in Salryn repo ever? | **NO** | Contract policy + threat model |
| 4 | One license file for host + all terminals? | **NO** | Separate machine-bound host and terminal licenses |
| 5 | Use 16-digit key as license? | **NO** (future activation code only) | Signed document is the license |
| 6 | Show salryn.info@gmail.com + machine code? | **YES** | Manual beta activation UX |
| 7 | Include license file in Backup DB package? | **NO for beta** | Keep outside DB package; reimport after machine change; optional later separate controlled license export |
| 8 | LAN production remain blocked? | **YES** until host/terminal entitlement enforcement + LAN acceptance | Avoid selling LAN without control |
| 9 | Installer? | **DELAY** | Architecture first; I1 not continued yet |
| 10 | Apply product feature hard locks in first beta branch? | **Soft then hard** | Prefer validate/import/type first; expand gates carefully to avoid blocking beta installs mid-debug |

### 8.1 Backup decision detail

**Recommend: license remains outside controlled DB backup package for beta.**

Reasons:

- Backup restore to another PC would otherwise transplant a machine-bound license (confusing Invalid state) or encourage multi-PC sharing if binding empty.
- Current engine packages DB only — aligns with LIC-FINAL copy.
- Support path: restore DB → re-import license if machine matches; if machine changed, email for reissue.
- Do **not** invent automatic license inclusion in backup without a dedicated signed export design.

### 8.2 Permanent license + SupportExpiresAt / UpdatesUntil

- `ExpiresAt` null = permanent runtime license for beta.
- Support/Updates dates are commercial metadata (display + future update channel), not necessarily hard runtime kill switches for beta unless Kirch decides otherwise.

---

## 9. What must be implemented next (summary)

See full branch order in `00-HANDOFF/LIC_ARCH_R1_IMPLEMENTATION_PLAN.md`.

1. **KIRCH-LIC-BETA-MANUAL-HOST-TERMINAL-ACTIVATION** — types, machine code UX, contact email, fingerprint improve, host/terminal validation, no cloud  
2. **KIRCH-LIC-LAN-TERMINAL-ENTITLEMENT-ENFORCEMENT** — count + pairing proof + reject wrong store/host/machine  
3. **KIRCH-LIC-OPS-PRIVATE-GENERATOR-SPEC** — external tool/repo definition (no private key in POS)  
4. **Future:** `KIRCH-CLOUD-LICENSING-VERCEL-ACTIVATION`  
5. **Not yet:** Installer I1, payment, subscription, RMO/BIR as licensing claims

---

## 10. Stack / process context (awareness only)

| Branch / item | SHA / status | Relation to licensing |
|---------------|--------------|------------------------|
| Surface4 hardware ban | `ab6b58bd...` CANDIDATE_AUTOMATED_PROVEN_MANUAL_PENDING | Out of scope for this study |
| CoreSmoke isolation | `cf1d925a...` CANDIDATE_AUTOMATED_PROVEN | Good baseline under stack |
| Backup UI | `49ef49c4...` CODE_COMPLETE_UNGATED_VERIFICATION_PARKED | License-outside-backup copy; LIC-FINAL based on it |
| LIC-FINAL | `4979b525...` CODE_COMPLETE_UNGATED_VERIFICATION_PARKED | Current licensing code target; unproven gates |
| Installer | DELAY | Do not continue I1 until licensing architecture locked |

---

## 11. Source inventory (audit anchors)

### Core / infrastructure

- `src/Salryn.Core/Services/Licensing/LicenseContracts.cs`
- `src/Salryn.Core/Services/Licensing/LocalLicenseContractPolicy.cs`
- `src/Salryn.Core/Services/Licensing/ILocalLicenseService.cs`
- `src/Salryn.Core/Services/Licensing/ILocalLicenseEnforcementService.cs`
- `src/Salryn.Infrastructure/Licensing/LocalLicenseFileStore.cs`
- `src/Salryn.Infrastructure/Licensing/LocalLicenseValidator.cs`
- `src/Salryn.Infrastructure/Licensing/LocalLicenseService.cs`
- `src/Salryn.Infrastructure/Licensing/LocalLicenseSignatureVerifier.cs`
- `src/Salryn.Infrastructure/Licensing/LocalMachineFingerprint.cs`
- `src/Salryn.Infrastructure/Licensing/LocalLicenseEnforcementService.cs`
- `src/Salryn.Infrastructure/Paths/SalrynLocalDataContract.cs`

### API / frontend

- `src/Salryn.Api.Local/LocalLicenseEndpoints.cs`
- `src/Salryn.Api.Local/Auth/ApiLicenseGuard.cs`
- `src/Salryn.Api.Local/LocalApiApplicationFactory.cs`
- `src/Salryn.App.Web/src/api/license.ts`
- `src/Salryn.App.Web/src/pages/LicensePage.tsx`
- `src/Salryn.App.Web/src/navigation.ts`
- `src/Salryn.App.Web/src/security/permissions.ts`
- `src/Salryn.App.Web/src/App.tsx`
- `src/Salryn.App.Web/src/components/AppShell.tsx`
- `src/Salryn.App.Web/src/pages/BackupRestorePage.tsx`

### LAN (entitlement gap)

- `src/Salryn.Api.Local/RemotePos/RemotePosCapacityPolicy.cs`
- `src/Salryn.Api.Local/RemotePos/RemotePosPairingGateService.cs`
- Host LAN UI: `HostLanControlPage.tsx`

### Tests / smoke

- `tests/Salryn.Tests/LocalLicensingM1FoundationChecks.cs`
- `tests/Salryn.Tests/LocalLicensingM2SignedInstallImportChecks.cs`
- `tests/Salryn.Tests/LocalLicensingM3LocalEnforcementBoundaryChecks.cs`
- CoreSmoke cases: `licensing-m1-local-license-foundation`, `licensing-m2-signed-license-install-import`, `licensing-m3-local-enforcement-boundaries`

### Prior reports

- `00-HANDOFF/LICENSING_M1_LOCAL_LICENSE_FOUNDATION_REPORT.md`
- `00-HANDOFF/LICENSING_M2_SIGNED_LICENSE_INSTALL_IMPORT_REPORT.md`
- `00-HANDOFF/LICENSING_M3_LOCAL_ENFORCEMENT_BOUNDARIES_REPORT.md`
- `00-HANDOFF/LIC_FINAL_OFFLINE_LICENSE_LAN_ENTITLEMENTS_REPORT.md`
- `00-HANDOFF/HARDEN_A1_A6_PRE_BETA_FOUNDATION_REPORT.md` (policy + path contract awareness)

---

## 12. Study gates

| Gate | Result |
|------|--------|
| Code implementation | None (report-only) |
| Protected refs | Unchanged |
| `git diff --check` | Run at study commit |
| Build | Not required (docs only) |

---

## 13. Final study status

**STUDY_COMPLETE**

Licensing architecture is clarified enough to sequence implementation branches. Installer remains delayed. Cloud remains deferred for first five beta clients. Next action for Kirch: approve no-code decisions in §8, then authorize implementation branch 1 only.

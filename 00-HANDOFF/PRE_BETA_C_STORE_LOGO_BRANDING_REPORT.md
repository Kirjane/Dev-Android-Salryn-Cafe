# PRE-BETA-C — Store Logo / Branding Report

## 1. Branch / base / final SHA

| Item | Value |
| --- | --- |
| Branch | `KIRCH-PRE-BETA-C-STORE-LOGO-BRANDING` |
| Base | `KIRCH-PRE-BETA-E-LAN-CLIENT-SHELL-PARITY` @ `541b6e5f8be42bd70bc2f1a51e5a571115af4c28` |
| Final SHA | `7ca4c3d12ca3730e890693043069b92c1e1e5bc1` |
| Protected SANDBOX | `1f74cd8bcaca1a5df487aa61677ee9829dd7fedb` (untouched) |
| Protected INSTALLER-M1 | `6b9c46676390b8a86a989eca600cb02e406ce865` (untouched) |

## 2. Decision

**`STORE_LOGO_BRANDING_REPAIRED_AND_PASSED`**

Repairs during gate loop (not scope creep):

- WEB-A3 / WEB-A4 global capability bans updated to allow **approved** Store Settings logo surfaces only (still ban FormData/`type=file` elsewhere).
- POS-MANUAL-1 guard inverted: logo upload is now **required** on Store Settings.
- RMO/QA1 migration expectations advanced for catalog **v20** (store logo migration); future probe uses **v21**.

## 3. Goal achieved

Store logo/branding persists in SQLite, survives restart, is readable by Host shell and LAN TerminalShell, with upload / preview / remove on Store Settings under Settings.Manage.

## 4. Implementation summary

### DB / migration

- Migration **v20** `StoreLogoBrandingSchemaMigration`: additive columns on `store_settings`:
  - `logo_content_type TEXT NULL`
  - `logo_bytes BLOB NULL`
- `SalrynDatabaseMigrationCatalog.SupportedMigrationVersion = 20`
- Registered in `DefaultDatabaseInitializationFactory`

### Backend

- `IStoreSettingsReadRepository.GetLogoAsync` / `IStoreSettingsWriteRepository.SetLogoAsync` / `ClearLogoAsync`
- SQLite repos persist bytes + content type; `HasLogo` / `LogoContentType` on read model
- Host endpoints (`LocalStoreBrandingEndpoints`):
  - `GET /api/branding/store` — any authenticated session
  - `GET /api/branding/store/logo` — bytes for Host shell `<img>`
  - `POST /api/settings/store/logo` — multipart, Settings.Manage
  - `DELETE /api/settings/store/logo` — Settings.Manage
- Validation: PNG / JPEG / WebP only; **SVG rejected**; max **512 KB**
- Remote POS: `GET .../branding/store` returns store name + optional `logoDataUrl` (base64 data URL) so LAN terminal does not need Host cookie for image bytes

### Frontend

- Store Settings: upload / preview / remove
- Host `AppShell`: `fetchStoreBranding` → logo in chrome when present
- `TerminalShell`: accepts `logoUrl` / store name props
- `RemotePosClientPage`: fetches remote branding after connect; passes into TerminalShell

### Tests

- `PreBetaCStoreLogoBrandingChecks` — migration 20, source guards, PNG round-trip, clear
- CoreSmoke case: `pre-beta-c-store-logo-branding`
- Guard updates: WebA3, WebA4, PosManual1, RMO migration version pins

## 5. Automated gates

| Gate | Result |
| --- | --- |
| `dotnet build Salryn.sln` | PASS |
| `pre-beta-c-store-logo-branding` | PASS |
| **full CoreSmoke** | **PASS** |
| `npm --prefix src/Salryn.App.Web run build` | PASS |
| `git diff --check` | PASS (CRLF warnings only) |

## 6. Explicit non-claims

- Not official / BIR receipt logo output
- Not cloud branding sync
- No SVG / scriptable image support
- No hardware proof required for this lane

## 7. Backup impact (Lane D note)

Logo is DB-backed (`store_settings.logo_*`). Portable backup of the SQLite DB will include branding when export/import is implemented in Lane D. No separate logo file tree was introduced.

## 8. DO LATER

- Logo visual/aesthetic polish
- Physical second-device LAN shell recheck of branding chrome

## 9. Risks

- Multipart logo upload is allowed only on Store Settings surfaces; WEB-A3/A4 still ban FormData elsewhere
- Remote branding uses data URL (bounded by 512 KB server limit)
- Migration 20 is additive; existing DBs upgrade without data loss path change

## 10. Push

Branch: `KIRCH-PRE-BETA-C-STORE-LOGO-BRANDING`  
Do not promote to protected refs.

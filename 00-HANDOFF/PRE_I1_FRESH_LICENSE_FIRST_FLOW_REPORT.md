# PRE-I1 Fresh License-First Flow Report

## 1. Branch / base / final SHA

- Branch: `KIRCH-PRE-I1-FRESH-LICENSE-FIRST-FLOW`
- Base branch: `KIRCH-PRE-I1-UIUX-BACKUP-QUALITY-REVIEW`
- Verified base SHA: `9f929bc6b47ff4f93a43d550ae474d12bbb01325`
- Base origin parity: `0 0`
- Final reviewed content SHA: `ec212c5390ca8e373e346d24542ebfe8298af61a`
- Protected `KIRCH-SANDBOX`: `1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- Protected `KIRCH-INSTALLER-M1`: `6b9c46676390b8a86a989eca600cb02e406ce865`
- Protected refs unchanged: yes

## 2. DB reset path and method

Source and launch-profile resolution proved the development root is:

- Development root: `C:\Users\kirch\Salryn\.salryn-dev`
- Development database: `C:\Users\kirch\Salryn\.salryn-dev\data\salryn.db`
- Production database (not touched): `C:\ProgramData\Salryn\data\salryn.db`

The original dev root contained 79 files totaling 6,450,021 bytes. Its database was 1,343,488 bytes with SHA-256 `711A56FE554E4C411B994A6447C4D2FEC6899C58BE108B7777AEF0CD21F55101`.

No dev data was deleted. Four runtime states were renamed and then moved outside the repository to:

`C:\Users\kirch\Documents\Codex\2026-07-16\files-mentioned-by-the-user-you\work\fresh-license-first\dev-data-backups`

The archived states preserve the original data, old-flow proof, unlicensed license-first proof, and explicit-development full-flow proof. At handoff, `C:\Users\kirch\Salryn\.salryn-dev` does not exist, so the next normal development launch recreates a clean DB.

Safe reset command used in final form:

```powershell
$repo = "C:\Users\kirch\Salryn"
$source = Join-Path $repo ".salryn-dev"
$archive = "C:\Users\kirch\Documents\Codex\2026-07-16\files-mentioned-by-the-user-you\work\fresh-license-first\dev-data-backups"
New-Item -ItemType Directory -Force -Path $archive | Out-Null
if (Test-Path -LiteralPath $source) {
    $destination = Join-Path $archive (".salryn-dev.backup-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
    Move-Item -LiteralPath $source -Destination $destination
}
```

## 3. Actual old fresh-launch flow

Runtime proof from a clean development DB before repair:

1. App loaded First-run Setup first.
2. Store name was prefilled as `Salryn Dev General Merchandise`.
3. Admin display name was prefilled as `DEV Admin`.
4. License was not checked before bootstrap.
5. `/api/license/status` and `/api/license/install` required authentication, so the desired pre-login activation flow was impossible.

Old flow: Fresh DB -> Bootstrap Setup -> Login -> authenticated shell -> License page.

## 4. Final fresh-launch flow

Final flow implemented:

1. Console fetches bootstrap, session, and local license status.
2. Unlicensed, invalid, expired, machine-mismatched, or Terminal-licensed Host launch renders License first.
3. Only a valid machine-matched Host license advances to Bootstrap Setup. Explicit development license mode is accepted only when the backend itself reports the opt-in Development status.
4. Bootstrap first-admin API independently enforces valid Host status/type/machine match; UI routing is not the only gate.
5. Completed bootstrap advances to Login.
6. Successful login advances to the full permitted Host shell.

Final flow: Fresh DB -> License / Activation -> valid Host license -> clean Bootstrap Setup -> Login -> full Host shell.

## 5. License-first proof

- Controlled license root: `C:\Users\kirch\AppData\Local\Salryn\Licensing`.
- No `license.json` existed during unlicensed proof.
- First repaired runtime screen was `License`, status `Unlicensed`.
- Machine code displayed: `SALRYN-MC-56DA-D28D-B387-9237`.
- Host and Terminal activation instructions were separate.
- License status/import endpoints were reachable before login and remained loopback-only.
- Pre-login install now requires `application/json`, preventing a simple cross-origin HTML form post from replacing the local license.
- Direct unlicensed `POST /api/bootstrap/first-admin` returned HTTP 403 with `Bootstrap.HostLicenseRequired`.
- Before and after that rejected request: `firstRunRequired=true`, `hasUsers=false`, `hasStoreSettings=false`.
- Existing signed-license gates prove Host/Terminal type separation, machine binding, signature validation, expiry/invalid denial, Terminal inability to unlock Host features, and Host inability to act as Terminal proof.

## 6. Bootstrap cleanup proof

- Removed the `Salryn Dev General Merchandise` store default.
- Removed the `DEV Admin` display-name default.
- Kept `admin` as the username default, which the task allowed.
- Runtime bootstrap proof showed blank Store name and Admin display name fields.
- Completed isolated setup with client-neutral values, then observed Login and the full Host shell.
- Full shell showed zero products, zero transactions, and no seeded/demo catalog data.
- The proof database was archived outside the repo; active `.salryn-dev` is absent at handoff.

## 7. Host/Terminal license model decision

Option A selected narrowly for beta compatibility:

- Host activation never asks the client for a terminal count.
- Terminal activation remains separate, with one signed machine-bound Terminal license per operational LAN terminal.
- A Terminal license cannot unlock Host bootstrap or the full Host application; the License screen now says this explicitly.
- `AllowedLanTerminals` remains issuer-only compatibility/enforcement metadata for internal LAN verification, not client-supplied activation data or commercial proof.
- Beta issuance policy should leave it at 0 unless Kirch explicitly authorizes an internal LAN verification entitlement.
- Removing the signed field and its accepted enforcement behavior would be a broader compatibility change and was not performed in this narrow lane.

## 8. AllowedLanTerminals handling

- Removed `Allowed LAN terminals (Host)` from the normal License UI.
- Replaced it with: each operational LAN terminal uses its own machine-bound Terminal license.
- The field remains in the signed schema/validator and internal Host LAN entitlement enforcement for backward compatibility and internal verification.
- It is not requested in Host activation instructions and is not editable/imported as a standalone client control.

## 9. Commands Kirch should run

Fresh reset, preserving any current dev data:

```powershell
cd C:\Users\kirch\Salryn

$archive = "C:\Users\kirch\Documents\Codex\2026-07-16\files-mentioned-by-the-user-you\work\fresh-license-first\dev-data-backups"
New-Item -ItemType Directory -Force -Path $archive | Out-Null
if (Test-Path -LiteralPath ".\.salryn-dev") {
    Move-Item -LiteralPath ".\.salryn-dev" -Destination (Join-Path $archive (".salryn-dev.backup-" + (Get-Date -Format "yyyyMMdd-HHmmss")))
}
```

Build and launch:

```powershell
cd C:\Users\kirch\Salryn

npm --prefix src\Salryn.App.Web run build

dotnet run `
  --project src\Salryn.Api.Local\Salryn.Api.Local.csproj `
  --launch-profile "Salryn.Api.Local Development"
```

Then open `http://127.0.0.1:5299`.

Do not set `SALRYN_LICENSE_DEVELOPMENT_MODE=1` for Kirch's normal fresh-license proof; that variable was used only for the isolated post-license bootstrap/login transition check.

## 10. Gates and results

| Gate | Result |
|---|---|
| `dotnet build Salryn.sln` | PASS, 0 warnings, 0 errors |
| `lic-closeout-hard-gates-and-terminal-proof` | PASS |
| `lic-lan-terminal-entitlement-enforcement` | PASS |
| `lic-beta-manual-host-terminal-activation` | PASS |
| `licensing-m2-signed-license-install-import` | PASS |
| `licensing-m3-local-enforcement-boundaries` | PASS |
| `pos-b1-backup-restore-safety` | PASS |
| `user-m1-host-only-user-creation` | PASS |
| `web-a3-shell-design-system` | PASS |
| `web-a4-native-host-visual-fidelity` | PASS |
| `web-b0-read-only-operator-surfaces` | PASS |
| New `pre-i1-fresh-license-first-flow` | PASS |
| Full CoreSmoke | PASS after updating one stale authenticated-import assertion |
| `npm --prefix src\\Salryn.App.Web run build` | PASS; existing >500 kB chunk advisory remains |
| `git diff --check` | PASS before report/commit |

## 11. Remaining risks

- No real customer/production-signed Host license was installed in the browser run. Signed Host/Terminal transition and denial behavior is automated-gate proven; the visible post-license bootstrap/login sequence used explicit development-license mode in an isolated DB.
- `AllowedLanTerminals` remains internal compatibility/enforcement metadata. Any future commercial LAN policy change should be a separately approved licensing-contract lane.
- Pre-login license status/import intentionally exposes only the loopback surface; install accepts signed JSON only and requires JSON content type, but this boundary should remain in security regression coverage.
- This task does not prove installer/package behavior, production ProgramData behavior, cloud activation, BIR/RMO/eSales, or physical printer behavior.
- Vite continues to report the existing main-chunk size advisory.

## 12. Final decision

`FRESH_LICENSE_FIRST_FLOW_REPAIRED_AND_VERIFIED`

The fresh beta flow is now License first, clean bootstrap second, Login third, and full Host access last. No package/install proof was attempted, and I1 must not proceed on the strength of this report alone.

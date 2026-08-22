# LIC-CLOSEOUT — Final Beta License Lane Report

**POS branch:** `KIRCH-LIC-CLOSEOUT-HARD-GATES-AND-TERMINAL-PROOF`
**POS base:** `KIRCH-LIC-LAN-TERMINAL-ENTITLEMENT-ENFORCEMENT` @ `d3922ab39022fb275850758456c2c77c91a616f1`
**POS final SHA:** *(branch tip after commit)*

**LicensingOps branch:** `KIRCH-LICOPS-M1-LOCAL-BETA-GENERATOR`
**LicensingOps SHA:** `d63e99255d22101ebe42c46b82044a8c38870db3`

**Status:** `LICENSE_LANE_BETA_COMPLETE` (pending final gate stamp)

## Key alignment

| Item | Status |
|------|--------|
| Historical POS public key private material | **Unavailable** in ops environment |
| Beta RSA-2048 keypair | Generated under `Salryn.LicensingOps/.secrets/` (gitignored) |
| POS public key updated | Yes — `LocalLicenseSignatureVerifier` aligned to beta public key |
| Private key in POS repo | **No** |
| Cloud/Vercel | Deferred |

## Generated license proof

- LicensingOps `npm test` and `npm run issue:host` produce signed schema-v2 JSON.
- POS closeout smoke optionally verifies sibling `Salryn.LicensingOps/generated/licenses/lic_host_*.json` with POS public-key verifier.
- Local Ops verify and tamper rejection covered by Ops self-test.

## Host license behavior

- Schema v2, ApplicationId `Salryn.POS`, MachineBinding required
- AllowedLanTerminals required; HostLicenseId/TerminalId forbidden
- Unlocks host feature set when Licensed + machine match
- Drives LAN entitlement effective max paired terminals

## Terminal license behavior

- Schema v2 Terminal type; HostLicenseId + TerminalId required
- Features filtered to LanTerminals only
- Cannot unlock host POS/Users/Backup/Reports
- Cannot be used as Host LAN operational license
- Host rejects Host document as terminal proof

## Endpoint enforcement (hard gates)

| Surface | Feature |
|---------|---------|
| POST `/api/checkout` | Pos |
| POST `/api/users` | Users |
| GET `/api/owner-reports/summary` | Reports |
| Backup package API | Backup |
| LAN join-code rotate | LanTerminals (+ entitlement service) |
| License status/install | **Not** product-gated (activation path remains) |

CoreSmoke process enables explicit development licensing mode via `SALRYN_CORE_SMOKE_DEVELOPMENT_ROOT` so unlicensed automated regressions still run product flows. Production path without that env fails closed when unlicensed.

## LAN terminal proof

- Activation **requires** `TerminalLicenseJson`
- Validates Terminal type, signature, StoreId, HostLicenseId, TerminalId
- Remote client sends proof from request or `localStorage.salryn.terminalLicenseJson`
- License page caches Terminal JSON after successful import
- **Limitation:** host does not independently re-fingerprint remote hardware; terminal device self-binds machine code

## Remaining acceptable beta limitations

1. Pending join request store still in-memory (process-scoped).
2. Not every read-only catalog endpoint is license-gated (writes/high-risk surfaces gated first).
3. Cloud activation deferred; manual email process remains.
4. Installer deferred.
5. Historical pre-beta public key retired for beta issuance; beta key custody is local Ops secrets.

## Non-goals (unchanged)

Cloud/Vercel, payment, installer, BIR/RMO production claims, eSales, hardware peripherals, generator/signing material inside POS.

## Manual beta process

1. Client copies machine code → emails salryn.info@gmail.com  
2. Ops issues Host/Terminal via LicensingOps  
3. Client imports signed JSON  
4. Host LAN uses AllowedLanTerminals; terminals present Terminal proof on activate  

See `Salryn.LicensingOps/docs/BETA_LICENSE_ISSUANCE_PLAYBOOK.md`.

## Protected refs

- `KIRCH-SANDBOX` = `1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `KIRCH-INSTALLER-M1` = `6b9c46676390b8a86a989eca600cb02e406ce865`

# Current Stable State and Next Work

## Source Stamp

- Stable branch: `KIRCH-SANDBOX`
- Stable SHA: `e477699dca27a21954a5ad9d388307fdb9ed08fd`
- Candidate branch: `KIRCH-FREEHAND`
- Candidate SHA before LAN-M16-QA-B: `80d784d84e6af635688085d8b97bfe935c953d65`
- Expected starting compare: `15 ahead / 0 behind`
- Status: Sandbox unchanged; Freehand unpromoted.

## Stable Sandbox Stack

Stable as of `e477699dca27a21954a5ad9d388307fdb9ed08fd`:

- `LAN-M13`
- `ROLE-M1`
- `ROLE-M2`
- `ROLE-M3`
- `ROLE-M4`
- `M5`
- `M6`
- `M7`
- `M8`
- `LAN-M14-A`
- `LAN-M14-B`
- `LAN-M14-C`
- `LAN-M14-D`
- `LAN-M14-E`

## Unpromoted Freehand Candidate Stack

Completed on Freehand, still unpromoted:

- `LAN-M14-F` through `LAN-M14-I`
- `LAN-M15-A` through `LAN-M15-F`
- `LAN-M16`
- `LAN-M16-QA-A`
- `LAN-M16-QA-B` — current candidate security/field UX hardening sprint

## LAN-M16-QA-B Current Candidate

LAN-M16-QA-B adds a host-approved Remote POS pairing gate and remote role login boundary.

Security model:

- Pairing approves device.
- Login approves person.
- Existing role matrix limits what the person can do remotely.
- Cashier/Manager only for Remote POS login.
- Admin/Owner remain host-local for this build.

Field setup model:

- Explicit host LAN IP remains the field setup.
- `localhost` from another device is wrong.
- No QR/mobile/cloud/discovery/custom DNS.
- No shared SQLite.
- No default/wildcard/`0.0.0.0` bind.

Remote POS boundary:

- Existing Remote POS checkout is still not strict official/RMO checkout.
- No remote official/RMO checkout claim.
- No printer/PDF/Crystal/ESC-POS behavior.
- No certification/accreditation/legal claim.

## Current LAN Boundary

- Remote-readonly remains GET-only under `/api/remote-readonly`.
- Local LAN control remains loopback/operator-only under `/api/lan-control`.
- Host owns SQLite, sales, inventory writes, receipt allocation, and audit.
- Remote clients never share SQLite.
- Independent hosts do not sync.
- Remote POS runtime is disabled by default and resets on host restart.
- Remote POS commands remain bound to terminal identity, handshake proof, host-issued operator session, host/store identity, and server-observed origin.
- Terminal disable/revoke still blocks remote POS through existing terminal trust checks.

## Live Coordination Docs

Current live coordination authority:

- `00-HANDOFF/CURRENT_STABLE_STATE_AND_NEXT_WORK.md`
- `00-HANDOFF/LAN_M16_QA_B_HOST_APPROVED_REMOTE_POS_PAIRING_GATE.md`
- `00-HANDOFF/LAN_M16_HOST_OWNED_MULTI_TERMINAL_ROLE_RUNTIME.md`
- `00-HANDOFF/LAN_M15_F_FINAL_FIELD_ACCEPTANCE_CLOSURE_AND_CODEX_REVIEW_BUNDLE.md`
- LAN-M14-F through LAN-M14-I docs
- LAN-M15-A through LAN-M15-F docs

Historical M-series and ROLE docs remain context only and must not override current source truth.

## Must Not Start Yet

- LAN-M17.
- LAN-M14-J.
- LAN-M15-G.
- Sandbox promotion.
- Default LAN bind enablement.
- Wildcard or `0.0.0.0` bind.
- Discovery, QR, mobile, cloud, or custom DNS.
- Shared SQLite over LAN.
- Remote official/RMO checkout over LAN.
- Printer/PDF/Crystal/ESC-POS.
- Certification/accreditation/legal claim.
- Role editor/custom permission manager.

## Next Step

Codex must verify LAN-M16-QA-B locally. Not stable until Codex verifies and maintainer review accepts it.

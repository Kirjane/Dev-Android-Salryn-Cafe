# LAN-M14-H Pairing Operator UX Surface

## Source Stamp

- Repo: `Kirch-Nairu/Salryn`
- Stable branch: `KIRCH-SANDBOX`
- Candidate branch: `KIRCH-FREEHAND`
- Stable SHA: `e477699dca27a21954a5ad9d388307fdb9ed08fd`
- `KIRCH-FREEHAND` SHA before LAN-M14-H: `e477699dca27a21954a5ad9d388307fdb9ed08fd`
- Ahead/behind before LAN-M14-H: `0 ahead / 0 behind`
- Date/context: LAN-M14-G hybrid bridge runtime complete; this sprint surfaces pairing and status UX for operators.
- Status: pairing/status UX surfaced; mutation controls remain absent; hybrid bridge disabled by default.

## Stable Stack

Stable on Sandbox as of `e477699dca27a21954a5ad9d388307fdb9ed08fd`:

- `LAN-M13` through `LAN-M14-E` as promoted on Sandbox
- `LAN-M14-F` - Host Bind Config Seam Implementation (candidate on Freehand)
- `LAN-M14-G` - Remote Read-Only Bridge Runtime (candidate on Freehand)

## LAN-M14-H Purpose

LAN-M14-H surfaces read-only pairing and session status UX on the React/Web remote-readonly client page so operators can see connection, terminal, and handshake state without enabling mutation.

It does not add Save/Delete/Create catalog controls.
It does not implement remote checkout.
It does not enable LAN bind by default.
It does not implement QR/discovery/mobile/cloud pairing.

## What Was Implemented

- `RemoteReadOnlyClientPage` exposes a Pairing / Session State card with connection status, operator instruction, and terminal identity fields.
- Status, terminal status, dashboard, product, bind policy, and audit visibility areas remain read-only query surfaces.
- `useRemoteReadOnlyClient` continues to use `useQuery` reads only; no `useMutation`.
- `remoteReadOnlyClient` keeps mutation-disabled warnings for remote clients.
- Page explicitly states that no catalog mutation controls are present.

## Security Boundaries

- Operator localhost session is separate from LAN remote terminal trust.
- Pairing and handshake remain required for remote reads; UX is visibility only.
- No Save, Delete, or Create mutation controls on the remote-readonly page.
- No remote cashier/admin execution surfaces.
- Hybrid bridge remains disabled by default; explicit operator enable is still required for LAN listen URLs.

## Forbidden Scope

LAN-M14-H explicitly forbids:

- catalog mutation controls (Save/Delete/Create)
- remote checkout or sales mutation
- settings/backup/official output/reports implementation
- role editor or custom permission manager
- QR/discovery/mobile/cloud pairing UX
- enabling LAN bind by default
- shared SQLite or SQLite path exposure
- RMO/printer/PDF/Crystal/ESC-POS work

## Decision

- LAN-M14-H surfaces pairing/status UX only; it does not authorize mutation or default LAN enablement.
- End-to-end closure docs, guards, and smokes remain LAN-M14-I work.
- Codex must verify LAN-M14-H before treating LAN-M14-I closure as approved.

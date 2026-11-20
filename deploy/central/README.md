# Salryn Cafe Central Sync Service — NON-PRODUCTION configuration reference

> **DEPLOYMENT AND RESTORE BLOCKED (S4):** Central.Server currently has no reconciled Cafe business-domain application authority. Recognized operations are durably held as `NeedsReview`, not applied; bootstrap returns 503. Do not deploy this service as a production sequencer, connect cashiers to it, or restore a live database from these examples. This document is an operator/developer reference only; PR #31 has not been accepted.

## 1. Security & Transport Boundary
- **Product Identity:** `Salryn.Cafe` (all `/api/sync/v1/*` requests except `/health` require `X-Salryn-Product-Id: Salryn.Cafe`).
- **Mandatory HTTPS:** Plaintext HTTP requests are rejected with `426 ERR_HTTPS_REQUIRED`. Only trust `X-Forwarded-Proto: https` when Kestrel sees a non-null remote IP matching an explicit proxy allowlist or loopback. A header from a device or absent peer is not evidence of TLS. Docker bridge and `cloudflared` container peers are generally **not loopback inside the service container**; verify and explicitly configure the actual trusted proxy address before lab testing. Never trust an entire unverified subnet or disable HTTPS to make tests pass.
- **No Live Credentials in Git:** Supply tunnel credentials and initial admin setup strictly on the target host.

## 2. Docker Template (disposable lab only; not authorized production deployment)
```bash
cp deploy/central/.env.example deploy/central/.env
docker compose -f deploy/central/docker-compose.yml up -d --build
```
A client-provided forwarded header is insufficient to establish a trusted proxy. Before a disposable HTTP test, inspect the server-observed peer IP and the configured allowlist; test both untrusted-header rejection (HTTP 426) and real TLS or correctly trusted proxy acceptance. No TLS, tunnel, Docker deployment or remote service behavior was executed by the S4 writer.

## 3. Ubuntu `systemd` Reference (do not start a production service)
1. Publish `Salryn.Sync.Central.Server` to `/opt/salryn-cafe-central`.
2. Create system user `salryn-cafe` and `/var/lib/salryn-cafe-central/{data,backups}`.
3. Copy `deploy/central/salryn-cafe-central.service` to `/etc/systemd/system/` and run:
   ```bash
   sudo systemctl daemon-reload
   sudo systemctl enable --now salryn-cafe-central.service
   ```

## 4. Backup and restore — DISABLED until financial recovery is designed

**Read-only backup:** A quiesced or SQLite online backup can be retained as evidence. Always validate `PRAGMA integrity_check`, account for WAL state and preserve immutable backup hashes and custody. Do not overwrite a running database or delete journal sidecars.

**Restore is NOT currently authorized or safe.** The former instructions to stop a service, overwrite the DB, delete WAL/SHM, restart, and bump the epoch were unsafe: a stale backup can erase previously acknowledged financial receipts and reuse the same epoch and sequence. `CentralSyncDatabase.RestoreAndBumpEpoch` now fails closed before modifying the destination.

A later Integrator-approved recovery protocol must (1) quiesce writes and prevent reconnect, (2) preserve an independent signed or otherwise tamper-evident pre-restore central instance ID, epoch, accepted operation IDs/hashes and global high-watermark, (3) stage and integrity-validate the proposed backup without touching the live target, (4) prove every previously acknowledged financial operation is present or deterministically recovered from independent durable outbox/inbox evidence, (5) allocate a non-reused fencing identity/epoch and non-regressing sequence, (6) atomically activate only after reconciliation, and (7) require clients to rebootstrap from a real verified domain projection. Missing evidence means stop for manual financial recovery, not an automatic restore.

An admin `/epoch/bump` response is **not** proof that restored ledger entries survived. The central bootstrap currently returns 503; it must not be used to reset or advance a client's applied cursor. No production restore, migration or data cleanup is authorized by this runbook.

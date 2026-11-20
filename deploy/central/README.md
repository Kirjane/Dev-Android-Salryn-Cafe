# Salryn Cafe Central Sync Service — Deployment & Backup/Restore Runbook

## 1. Security & Transport Boundary
- **Product Identity:** `Salryn.Cafe` (all `/api/sync/v1/*` requests except `/health` require `X-Salryn-Product-Id: Salryn.Cafe`).
- **Mandatory HTTPS:** Plaintext HTTP requests are rejected with `426 ERR_HTTPS_REQUIRED`. Bind Kestrel to `127.0.0.1:8443` behind Cloudflare Tunnel (`cloudflared-config.example.yml`) or a TLS 1.2+/1.3 reverse proxy (Caddy/Nginx) setting `X-Forwarded-Proto: https`.
- **No Live Credentials in Git:** Supply tunnel credentials and initial admin setup strictly on the target host.

## 2. Docker Deployment
```bash
cp deploy/central/.env.example deploy/central/.env
docker compose -f deploy/central/docker-compose.yml up -d --build
```
Verify health (simulating proxy TLS header on loopback):
```bash
curl -fsS -H "X-Forwarded-Proto: https" http://127.0.0.1:8443/api/sync/v1/health
```

## 3. Ubuntu `systemd` Deployment
1. Publish `Salryn.Sync.Central.Server` to `/opt/salryn-cafe-central`.
2. Create system user `salryn-cafe` and `/var/lib/salryn-cafe-central/{data,backups}`.
3. Copy `deploy/central/salryn-cafe-central.service` to `/etc/systemd/system/` and run:
   ```bash
   sudo systemctl daemon-reload
   sudo systemctl enable --now salryn-cafe-central.service
   ```

## 4. Backup & Restore with Mandatory Epoch Bump
- **Online Backup:** Use SQLite's online backup API (`sqlite3 /var/lib/salryn-cafe-central/data/salryn-cafe-central.db ".backup '/var/lib/salryn-cafe-central/backups/central-$(date -u +%Y%m%dT%H%M%SZ).db'"`). Never copy a live WAL database with `cp`.
- **Restore Procedure:**
  1. Stop `salryn-cafe-central.service`.
  2. Replace `salryn-cafe-central.db` with the verified backup and remove stale `-wal`/`-shm` sidecars.
  3. Start `salryn-cafe-central.service`.
  4. Immediately invoke `POST /api/sync/v1/admin/epoch/bump` (or `CentralSyncDatabase.RestoreAndBumpEpoch`) so `server_epoch` increments. Connected clients automatically receive `409 EPOCH_MISMATCH_REBOOTSTRAP_REQUIRED` and re-bootstrap cleanly without cursor corruption.

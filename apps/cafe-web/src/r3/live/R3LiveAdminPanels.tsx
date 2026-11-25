import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { createLiveBackofficeApi } from "../../api/backofficeClient";
import type {
  AuditLogRow,
  BackupPackageRow,
  LanControlStatusSnapshot,
  LanPairingSnapshot,
  LicenseStatusSnapshot,
  StoreSettingsSnapshot,
  UserRoleOption,
  UserRow,
} from "../../backoffice/types";
import type { CafePrintStation } from "../../cashier/types";
import { DurableIntentBanner } from "./DurableIntentBanner";
import { useDurableIntent, type DurableIntentAuthority } from "./durableIntent";

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
}

export function R3LiveUsersPanel() {
  const api = useMemo(() => createLiveBackofficeApi(), []);
  const [users, setUsers] = useState<UserRow[]>([]);
  const [roles, setRoles] = useState<UserRoleOption[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [msg, setMsg] = useState<string | null>(null);

  const [username, setUsername] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [password, setPassword] = useState("");
  const [roleId, setRoleId] = useState("");
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [u, r] = await Promise.all([api.getUsers(), api.getUserRoles()]);
      setUsers(u);
      setRoles(r.filter((x) => x.isActive));
      if (r.length > 0) setRoleId((prev) => prev || r[0].roleId);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load users.");
    } finally {
      setLoading(false);
    }
  }, [api]);

  useEffect(() => {
    void load();
  }, [load]);

  const handleCreate = async (e: FormEvent) => {
    e.preventDefault();
    setMsg(null);
    setError(null);
    if (!username.trim() || !displayName.trim() || !password || !roleId) {
      setError("Username, display name, password, and role are required.");
      return;
    }
    setBusy(true);
    try {
      await api.createUser({
        username: username.trim(),
        displayName: displayName.trim(),
        password,
        roleId,
      });
      setUsername("");
      setDisplayName("");
      setPassword("");
      setMsg("Operator account created in live Host.");
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to create user.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div style={{ display: "grid", gap: 16 }}>
      <div className="r3-page-header">
        <div>
          <h1 className="r3-page-title">Users &amp; Roles</h1>
          <p className="r3-page-subtitle">Manage canonical Host operators and role assignments (Cross-Cutting).</p>
        </div>
        <button type="button" className="r3-btn r3-btn--outline r3-btn--sm" onClick={() => void load()} disabled={loading}>
          Refresh Users
        </button>
      </div>

      {error && <div className="r3-alert r3-alert--danger" role="alert"><div className="r3-alert-body">{error}</div></div>}
      {msg && <div className="r3-alert r3-alert--success" role="status"><div className="r3-alert-body">{msg}</div></div>}

      <div className="r3-card">
        <div className="r3-card-header"><div className="r3-card-title">Live Host Operators ({users.length})</div></div>
        <div className="r3-table-wrap" tabIndex={0} role="region" aria-label="Host Operators">
          <table className="r3-table">
            <caption className="r3-sr-only">Host Operators</caption>
            <thead>
              <tr>
                <th scope="col">Username</th>
                <th scope="col">Display Name</th>
                <th scope="col">Role</th>
                <th scope="col">Status</th>
              </tr>
            </thead>
            <tbody>
              {users.map((u) => (
                <tr key={u.id}>
                  <th scope="row"><code>{u.username}</code></th>
                  <td>{u.displayName}</td>
                  <td>{u.roleName}</td>
                  <td><span className="r3-badge r3-badge--success">{u.status}</span></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <div className="r3-card">
        <div className="r3-card-header"><div className="r3-card-title">Create Operator Account</div></div>
        <form onSubmit={(e) => void handleCreate(e)} style={{ display: "grid", gap: 12, maxWidth: 480 }}>
          <label>
            <div className="r3-text-xs r3-text-muted">Username</div>
            <input className="r3-input" value={username} onChange={(e) => setUsername(e.target.value)} disabled={busy} />
          </label>
          <label>
            <div className="r3-text-xs r3-text-muted">Display Name</div>
            <input className="r3-input" value={displayName} onChange={(e) => setDisplayName(e.target.value)} disabled={busy} />
          </label>
          <label>
            <div className="r3-text-xs r3-text-muted">Initial Password</div>
            <input type="password" className="r3-input" value={password} onChange={(e) => setPassword(e.target.value)} disabled={busy} />
          </label>
          <label>
            <div className="r3-text-xs r3-text-muted">Role</div>
            <select className="r3-input" value={roleId} onChange={(e) => setRoleId(e.target.value)} disabled={busy}>
              {roles.map((r) => (
                <option key={r.roleId} value={r.roleId}>{r.roleName}</option>
              ))}
            </select>
          </label>
          <div>
            <button type="submit" className="r3-btn r3-btn--primary" disabled={busy}>
              {busy ? "Creating…" : "Create Operator"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

export function R3LiveSettingsPanel() {
  const api = useMemo(() => createLiveBackofficeApi(), []);
  const [settings, setSettings] = useState<StoreSettingsSnapshot | null>(null);
  const [storeName, setStoreName] = useState("");
  const [businessName, setBusinessName] = useState("");
  const [address, setAddress] = useState("");
  const [contactNumber, setContactNumber] = useState("");
  const [tin, setTin] = useState("");
  const [receiptWidthMm, setReceiptWidthMm] = useState("80");
  const [receiptFooter, setReceiptFooter] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    setError(null);
    try {
      const s = await api.getStoreSettings();
      setSettings(s);
      setStoreName(s.storeName ?? "");
      setBusinessName(s.businessName ?? "");
      setAddress(s.address ?? "");
      setContactNumber(s.contactNumber ?? "");
      setTin(s.tin ?? "");
      setReceiptWidthMm(String(s.receiptWidthMm ?? 80));
      setReceiptFooter(s.receiptFooter ?? "");
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load store settings.");
    }
  }, [api]);

  useEffect(() => {
    void load();
  }, [load]);

  const handleSave = async (e: FormEvent) => {
    e.preventDefault();
    if (!settings) return;
    setBusy(true);
    setMsg(null);
    setError(null);
    try {
      await api.updateStoreSettings({
        storeName: storeName.trim(),
        businessName: businessName.trim() || null,
        address: address.trim() || null,
        contactNumber: contactNumber.trim() || null,
        tin: tin.trim() || null,
        currencyLabel: settings.currencyLabel || "PHP",
        receiptWidthMm: Number(receiptWidthMm) || 80,
        receiptHeader: settings.receiptHeader,
        receiptFooter: receiptFooter.trim() || null,
        printerMode: settings.printerMode,
        windowsPrinterName: settings.windowsPrinterName,
      });
      setMsg("Store settings saved to live Host.");
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to update settings.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div style={{ display: "grid", gap: 16 }}>
      <div className="r3-page-header">
        <div>
          <h1 className="r3-page-title">Store Settings</h1>
          <p className="r3-page-subtitle">Authoritative store profile and receipt configuration (Cross-Cutting).</p>
        </div>
      </div>
      {error && <div className="r3-alert r3-alert--danger" role="alert"><div className="r3-alert-body">{error}</div></div>}
      {msg && <div className="r3-alert r3-alert--success" role="status"><div className="r3-alert-body">{msg}</div></div>}
      <div className="r3-card">
        <form onSubmit={(e) => void handleSave(e)} style={{ display: "grid", gap: 12, maxWidth: 540 }}>
          <label>
            <div className="r3-text-xs r3-text-muted">Store Name</div>
            <input className="r3-input" value={storeName} onChange={(e) => setStoreName(e.target.value)} disabled={busy} />
          </label>
          <label>
            <div className="r3-text-xs r3-text-muted">Business Name</div>
            <input className="r3-input" value={businessName} onChange={(e) => setBusinessName(e.target.value)} disabled={busy} />
          </label>
          <label>
            <div className="r3-text-xs r3-text-muted">Address</div>
            <input className="r3-input" value={address} onChange={(e) => setAddress(e.target.value)} disabled={busy} />
          </label>
          <label>
            <div className="r3-text-xs r3-text-muted">Contact Number</div>
            <input className="r3-input" value={contactNumber} onChange={(e) => setContactNumber(e.target.value)} disabled={busy} />
          </label>
          <label>
            <div className="r3-text-xs r3-text-muted">TIN</div>
            <input className="r3-input" value={tin} onChange={(e) => setTin(e.target.value)} disabled={busy} />
          </label>
          <label>
            <div className="r3-text-xs r3-text-muted">Thermal Paper Width (mm)</div>
            <select className="r3-input" value={receiptWidthMm} onChange={(e) => setReceiptWidthMm(e.target.value)} disabled={busy}>
              <option value="58">58mm</option>
              <option value="80">80mm</option>
            </select>
          </label>
          <label>
            <div className="r3-text-xs r3-text-muted">Receipt Footer</div>
            <input className="r3-input" value={receiptFooter} onChange={(e) => setReceiptFooter(e.target.value)} disabled={busy} />
          </label>
          <div>
            <button type="submit" className="r3-btn r3-btn--primary" disabled={busy}>
              {busy ? "Saving…" : "Save Store Settings"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

export function R3LivePrintStationsPanel({ authority }: { authority: DurableIntentAuthority | null }) {
  const api = useMemo(() => createLiveBackofficeApi(), []);
  const [stations, setStations] = useState<CafePrintStation[]>([]);
  const [name, setName] = useState("");
  const [kind, setKind] = useState<"CustomerReceipt" | "Kitchen" | "BarDrinks">("CustomerReceipt");
  const [routeKey, setRouteKey] = useState("COUNTER-80MM");
  const [msg, setMsg] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const stationIntent = useDurableIntent<
    { operationId: string; name: string; stationKind: "CustomerReceipt" | "Kitchen" | "BarDrinks"; routeKey: string },
    void
  >("admin-print-station", authority);

  const load = useCallback(async () => {
    setError(null);
    try {
      setStations(await api.getPrintStations());
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load print stations.");
    }
  }, [api]);

  useEffect(() => {
    void load();
  }, [load]);

  const handleAdd = async (e: FormEvent) => {
    e.preventDefault();
    setMsg(null);
    setError(null);
    if (!name.trim() || !routeKey.trim()) {
      setError("Station name and route key are required.");
      return;
    }
    const res = await stationIntent.execute(
      { name: name.trim(), stationKind: kind, routeKey: routeKey.trim() },
      () => ({
        operationId: crypto.randomUUID(),
        name: name.trim(),
        stationKind: kind,
        routeKey: routeKey.trim(),
      }),
      (payload) => api.createPrintStation(payload),
    );
    if (res.ok) {
      setName("");
      setMsg("Print station registered in live Host.");
      await load();
    } else {
      setError(res.message);
    }
  };

  return (
    <div style={{ display: "grid", gap: 16 }}>
      <div className="r3-page-header">
        <div>
          <h1 className="r3-page-title">Print Stations</h1>
          <p className="r3-page-subtitle">Receipt and Kitchen ticket routing stations (C7).</p>
        </div>
      </div>
      {error && <div className="r3-alert r3-alert--danger" role="alert"><div className="r3-alert-body">{error}</div></div>}
      {msg && <div className="r3-alert r3-alert--success" role="status"><div className="r3-alert-body">{msg}</div></div>}

      <div className="r3-card">
        <div className="r3-card-header"><div className="r3-card-title">Configured Stations ({stations.length})</div></div>
        <div className="r3-table-wrap" tabIndex={0} role="region" aria-label="Print Stations">
          <table className="r3-table">
            <caption className="r3-sr-only">Print Stations</caption>
            <thead>
              <tr>
                <th scope="col">Name</th>
                <th scope="col">Kind</th>
                <th scope="col">Route Key</th>
                <th scope="col">Status</th>
              </tr>
            </thead>
            <tbody>
              {stations.length === 0 ? (
                <tr><td colSpan={4} className="r3-text-muted">No print stations configured yet.</td></tr>
              ) : (
                stations.map((s) => (
                  <tr key={s.stationId}>
                    <th scope="row">{s.name}</th>
                    <td>{String(s.stationKind)}</td>
                    <td><code>{s.routeKey}</code></td>
                    <td><span className="r3-badge r3-badge--success">{s.isActive ? "Active" : "Inactive"}</span></td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>

      <div className="r3-card">
        <div className="r3-card-header"><div className="r3-card-title">Register Print Station</div></div>
        <DurableIntentBanner
          label="Print Station"
          operationId={stationIntent.pending?.payload.operationId ?? null}
          pending={stationIntent.pending}
          corruptReason={stationIntent.corruptReason}
          authorityMismatch={stationIntent.authorityMismatch}
          inFlight={stationIntent.inFlight}
          onRetry={() =>
            void stationIntent.retryPending((p) => api.createPrintStation(p)).then((r) => {
              if (r.ok) {
                setMsg("Print station retry succeeded.");
                void load();
              } else {
                setError(r.message);
              }
            })
          }
          onClearRejected={() => stationIntent.clearPending(false)}
          onClearCorrupt={() => stationIntent.clearPending(true)}
        />
        <form onSubmit={(e) => void handleAdd(e)} style={{ display: "grid", gap: 12, maxWidth: 460 }}>
          <label>
            <div className="r3-text-xs r3-text-muted">Station Name</div>
            <input className="r3-input" value={name} onChange={(e) => setName(e.target.value)} placeholder="Front Counter Thermal" />
          </label>
          <label>
            <div className="r3-text-xs r3-text-muted">Station Kind</div>
            <select className="r3-input" value={kind} onChange={(e) => setKind(e.target.value as "CustomerReceipt" | "Kitchen" | "BarDrinks")}>
              <option value="CustomerReceipt">Customer Receipt</option>
              <option value="Kitchen">Kitchen</option>
              <option value="BarDrinks">Bar &amp; Drinks</option>
            </select>
          </label>
          <label>
            <div className="r3-text-xs r3-text-muted">Route Key</div>
            <input className="r3-input" value={routeKey} onChange={(e) => setRouteKey(e.target.value)} />
          </label>
          <div>
            <button type="submit" className="r3-btn r3-btn--primary" disabled={stationIntent.inFlight}>
              Add Print Station
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

export function R3LiveDevicesPanel() {
  const api = useMemo(() => createLiveBackofficeApi(), []);
  const [lanStatus, setLanStatus] = useState<LanControlStatusSnapshot | null>(null);
  const [lanPairing, setLanPairing] = useState<LanPairingSnapshot | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    setError(null);
    try {
      const [status, pairing] = await Promise.all([
        api.getLanStatus().catch(() => null),
        api.getLanPairing().catch(() => null),
      ]);
      setLanStatus(status);
      setLanPairing(pairing);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load LAN device status.");
    }
  }, [api]);

  useEffect(() => {
    void load();
  }, [load]);

  const handleRotate = async () => {
    setBusy(true);
    setError(null);
    try {
      const next = await api.rotateLanJoinCode();
      setLanPairing(next);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to rotate LAN join code.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div style={{ display: "grid", gap: 16 }}>
      <div className="r3-page-header">
        <div>
          <h1 className="r3-page-title">Devices, LAN Pairing &amp; Central Sync</h1>
          <p className="r3-page-subtitle">Local Host LAN terminal control + isolated link to Central Owner Console (Cross-Cutting).</p>
        </div>
        <a href="/cafe/central" className="r3-btn r3-btn--primary r3-btn--sm">
          Open Isolated Central Console
        </a>
      </div>
      {error && <div className="r3-alert r3-alert--danger" role="alert"><div className="r3-alert-body">{error}</div></div>}

      <div className="r3-card">
        <div className="r3-card-header"><div className="r3-card-title">Windows Host LAN Control</div></div>
        {lanStatus ? (
          <div style={{ display: "grid", gap: 6 }} className="r3-text-sm">
            <div>Local URL: <code>{lanStatus.hostLocalUrl}</code></div>
            <div>LAN URL: <code>{lanStatus.hostLanUrl}</code></div>
            <div>Bind Mode: <strong>{lanStatus.bindMode}</strong> · Remote POS Runtime: <strong>{lanStatus.remotePosRuntimeEnabled ? "Enabled" : "Disabled"}</strong></div>
          </div>
        ) : (
          <p className="r3-text-muted r3-text-sm">LAN status unavailable on this Host instance.</p>
        )}
      </div>

      <div className="r3-card">
        <div className="r3-card-header">
          <div>
            <div className="r3-card-title">Remote POS Pairing</div>
            <div className="r3-card-subtitle">{lanPairing?.message ?? "Rotate join code to enroll trusted LAN devices."}</div>
          </div>
          <button type="button" className="r3-btn r3-btn--outline r3-btn--sm" onClick={() => void handleRotate()} disabled={busy}>
            Rotate LAN Join Code
          </button>
        </div>
        {lanPairing?.activeJoinCode && (
          <div className="r3-alert r3-alert--info">
            <div className="r3-alert-body">
              Active Join Code: <code>{lanPairing.activeJoinCode}</code>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

export function R3LiveBackupLicensePanel() {
  const api = useMemo(() => createLiveBackofficeApi(), []);
  const [license, setLicense] = useState<LicenseStatusSnapshot | null>(null);
  const [backups, setBackups] = useState<BackupPackageRow[]>([]);
  const [restoreTargetId, setRestoreTargetId] = useState("");
  const [restoreConfirm, setRestoreConfirm] = useState("");
  const [msg, setMsg] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    setError(null);
    try {
      const [lic, bks] = await Promise.all([api.getLicenseStatus(), api.getBackups()]);
      setLicense(lic);
      setBackups(bks);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load backup & license status.");
    }
  }, [api]);

  useEffect(() => {
    void load();
  }, [load]);

  const handleCreateBackup = async () => {
    setBusy(true);
    setMsg(null);
    setError(null);
    try {
      const created = await api.createBackup();
      setMsg(`Backup package created: ${created.backupId} (${formatBytes(created.sizeBytes)}).`);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to create backup.");
    } finally {
      setBusy(false);
    }
  };

  const handleVerify = async (backupId: string) => {
    setBusy(true);
    setMsg(null);
    setError(null);
    try {
      const res = await api.verifyBackup(backupId);
      setMsg(`Backup ${backupId.slice(0, 8)}… verified: ${res.summary}`);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Backup verification failed.");
    } finally {
      setBusy(false);
    }
  };

  const handleRestore = async (e: FormEvent) => {
    e.preventDefault();
    if (!restoreTargetId || restoreConfirm.trim() !== "RESTORE SALRYN BACKUP") {
      setError("Type exact confirmation phrase RESTORE SALRYN BACKUP to restore a Host backup.");
      return;
    }
    setBusy(true);
    setMsg(null);
    setError(null);
    try {
      await api.restoreBackup(restoreTargetId, restoreConfirm.trim());
      setRestoreConfirm("");
      setMsg(`Backup ${restoreTargetId} restored.`);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Backup restore failed.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div style={{ display: "grid", gap: 16 }}>
      <div className="r3-page-header">
        <div>
          <h1 className="r3-page-title">Backup &amp; License</h1>
          <p className="r3-page-subtitle">Host license status and verified SQLite backup packages (Cross-Cutting).</p>
        </div>
        <button type="button" className="r3-btn r3-btn--primary r3-btn--sm" onClick={() => void handleCreateBackup()} disabled={busy}>
          Create Backup Now
        </button>
      </div>
      {error && <div className="r3-alert r3-alert--danger" role="alert"><div className="r3-alert-body">{error}</div></div>}
      {msg && <div className="r3-alert r3-alert--success" role="status"><div className="r3-alert-body">{msg}</div></div>}

      <div className="r3-card">
        <div className="r3-card-header"><div className="r3-card-title">Host License Authority</div></div>
        {license ? (
          <div style={{ display: "grid", gap: 6 }} className="r3-text-sm">
            <div>Status: <strong>{String(license.status)}</strong> · Machine Matched: <strong>{license.machineMatched ? "Yes" : "No"}</strong></div>
            <div>Message: {license.message}</div>
          </div>
        ) : (
          <p className="r3-text-muted r3-text-sm">Loading license status…</p>
        )}
      </div>

      <div className="r3-card">
        <div className="r3-card-header"><div className="r3-card-title">SQLite Backup Packages ({backups.length})</div></div>
        <div className="r3-table-wrap" tabIndex={0} role="region" aria-label="Backup Packages">
          <table className="r3-table">
            <caption className="r3-sr-only">Backup Packages</caption>
            <thead>
              <tr>
                <th scope="col">Backup ID</th>
                <th scope="col">Created</th>
                <th scope="col">Size</th>
                <th scope="col">Actions</th>
              </tr>
            </thead>
            <tbody>
              {backups.length === 0 ? (
                <tr><td colSpan={4} className="r3-text-muted">No backups yet.</td></tr>
              ) : (
                backups.map((b) => (
                  <tr key={b.backupId}>
                    <th scope="row"><code>{b.backupId}</code></th>
                    <td>{new Date(b.createdAt).toLocaleString()}</td>
                    <td className="r3-tabular">{formatBytes(b.sizeBytes)}</td>
                    <td style={{ display: "flex", gap: 6 }}>
                      <button type="button" className="r3-btn r3-btn--outline r3-btn--sm" onClick={() => void handleVerify(b.backupId)} disabled={busy}>
                        Verify
                      </button>
                      <button type="button" className="r3-btn r3-btn--outline r3-btn--sm" onClick={() => setRestoreTargetId(b.backupId)} disabled={busy}>
                        Select for Restore
                      </button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
        {restoreTargetId && (
          <form onSubmit={(e) => void handleRestore(e)} style={{ display: "grid", gap: 10, maxWidth: 460, marginTop: 14 }}>
            <div className="r3-text-sm">Selected backup: <code>{restoreTargetId}</code></div>
            <label>
              <div className="r3-text-xs r3-text-muted">Type RESTORE SALRYN BACKUP to confirm destructive overwrite</div>
              <input className="r3-input" value={restoreConfirm} onChange={(e) => setRestoreConfirm(e.target.value)} placeholder="RESTORE SALRYN BACKUP" />
            </label>
            <div>
              <button type="submit" className="r3-btn r3-btn--primary" disabled={busy}>Confirm Restore</button>
            </div>
          </form>
        )}
      </div>
    </div>
  );
}

export function R3LiveAuditLogPanel() {
  const api = useMemo(() => createLiveBackofficeApi(), []);
  const [logs, setLogs] = useState<AuditLogRow[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setLogs(await api.getAuditLogs());
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load audit log.");
    } finally {
      setLoading(false);
    }
  }, [api]);

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <div style={{ display: "grid", gap: 16 }}>
      <div className="r3-page-header">
        <div>
          <h1 className="r3-page-title">Audit Log</h1>
          <p className="r3-page-subtitle">Immutable Host security and operational audit trail (Cross-Cutting).</p>
        </div>
        <button type="button" className="r3-btn r3-btn--outline r3-btn--sm" onClick={() => void load()} disabled={loading}>
          Refresh Audit Log
        </button>
      </div>
      {error && <div className="r3-alert r3-alert--danger" role="alert"><div className="r3-alert-body">{error}</div></div>}
      <div className="r3-card">
        <div className="r3-table-wrap" tabIndex={0} role="region" aria-label="Audit Log Entries">
          <table className="r3-table">
            <caption className="r3-sr-only">Audit Log Entries</caption>
            <thead>
              <tr>
                <th scope="col">Time</th>
                <th scope="col">Action</th>
                <th scope="col">Entity</th>
                <th scope="col">Operator</th>
                <th scope="col">Reason</th>
              </tr>
            </thead>
            <tbody>
              {logs.length === 0 ? (
                <tr><td colSpan={5} className="r3-text-muted">No audit events returned.</td></tr>
              ) : (
                logs.map((row) => (
                  <tr key={row.id}>
                    <td>{new Date(row.createdAt).toLocaleString()}</td>
                    <th scope="row">{row.action}</th>
                    <td>{row.entityType}</td>
                    <td>{row.userDisplayName ?? "System"}</td>
                    <td>{row.reason ?? "—"}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

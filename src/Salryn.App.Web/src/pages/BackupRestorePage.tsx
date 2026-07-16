import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  createBackup,
  listBackups,
  restoreBackup,
  RESTORE_CONFIRMATION_INPUT,
  verifyBackup,
  type BackupSummary,
  type BackupVerification,
} from "../api/backups";
import { Alert, Button, Card, CardBody, CardHeader, CardTitle, CommandBar, LoadingState, StatusPill, TextInput } from "../components/ui";

function formatBytes(value: number | null | undefined): string {
  if (value === null || value === undefined) return "Not available";
  if (value < 1024) return `${value} B`;
  if (value < 1024 * 1024) return `${(value / 1024).toFixed(1)} KB`;
  return `${(value / (1024 * 1024)).toFixed(1)} MB`;
}

function formatDate(value: string | null | undefined): string {
  if (!value) return "Not available";
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString();
}

function firstFileHash(verification: BackupVerification | null): string | null {
  const file = verification?.manifest.files?.[0];
  return file?.sha256 ?? null;
}

function isVerifiedRestorable(verification: BackupVerification | null | undefined): boolean {
  return verification?.database?.isRestorable === true;
}

export function BackupRestorePage() {
  const backupsQuery = useQuery({ queryKey: ["backup-packages"], queryFn: listBackups });
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [verifyingId, setVerifyingId] = useState<string | null>(null);
  const [restoreTarget, setRestoreTarget] = useState<string | null>(null);
  const [restoreText, setRestoreText] = useState("");
  const [restoring, setRestoring] = useState(false);
  const [verifications, setVerifications] = useState<Record<string, BackupVerification>>({});

  const backups = useMemo(() => backupsQuery.data ?? [], [backupsQuery.data]);
  const selectedVerification = restoreTarget ? verifications[restoreTarget] ?? null : null;
  const restoreReady = Boolean(restoreTarget && isVerifiedRestorable(selectedVerification) && restoreText === RESTORE_CONFIRMATION_INPUT && !restoring);

  async function handleCreateBackup() {
    setCreating(true);
    setMessage(null);
    setError(null);
    try {
      const created = await createBackup();
      setMessage(`Backup created: ${created.backupId}`);
      await backupsQuery.refetch();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setCreating(false);
    }
  }

  async function handleVerifyBackup(backupId: string) {
    setVerifyingId(backupId);
    setMessage(null);
    setError(null);
    try {
      const verification = await verifyBackup(backupId);
      setVerifications((current) => ({ ...current, [backupId]: verification }));
      setMessage(verification.database.isRestorable ? `Backup verified: ${backupId}` : `Backup is not restorable: ${backupId}`);
    } catch (e) {
      setError((e as Error).message);
      setVerifications((current) => {
        const next = { ...current };
        delete next[backupId];
        return next;
      });
    } finally {
      setVerifyingId(null);
    }
  }

  async function handleRestore() {
    if (!restoreTarget || !restoreReady) return;
    setRestoring(true);
    setMessage(null);
    setError(null);
    try {
      const result = await restoreBackup(restoreTarget);
      setMessage(`Restore completed from ${restoreTarget}${result.completedAt ? ` at ${formatDate(result.completedAt)}` : ""}. Restart the app if the local API reports stale data.`);
      setRestoreText("");
      setRestoreTarget(null);
      await backupsQuery.refetch();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setRestoring(false);
    }
  }

  function beginRestore(backup: BackupSummary) {
    setRestoreTarget(backup.backupId);
    setRestoreText("");
    setMessage(null);
    setError(null);
  }

  return (
    <div className="space-y-6">
      <CommandBar
        title="Backup & Restore"
        subtitle="Create, verify, and restore local backups."
        status={<StatusPill tone="warning">Restore requires confirmation</StatusPill>}
        actions={(
          <Button variant="secondary" onClick={() => void backupsQuery.refetch()} disabled={backupsQuery.isFetching || creating || restoring}>
            Refresh
          </Button>
        )}
      />

      <Alert tone="warning" title="Restore replaces the current database">
        Verify a backup before restoring it. Keep a recent copy before you continue.
      </Alert>

      <div className="grid gap-4 lg:grid-cols-3">
        <Card>
          <CardHeader><CardTitle>Create backup</CardTitle></CardHeader>
          <CardBody className="space-y-3">
            <p className="text-sm text-slate-600">Create a backup of the current local database.</p>
            <Button onClick={() => void handleCreateBackup()} disabled={creating || restoring}>{creating ? "Creating…" : "Create Backup"}</Button>
          </CardBody>
        </Card>
        <Card>
          <CardHeader><CardTitle>Verify before restore</CardTitle></CardHeader>
          <CardBody className="space-y-3">
            <p className="text-sm text-slate-600">Verify a backup before using it for restore.</p>
            <StatusPill tone="info">Verification required</StatusPill>
          </CardBody>
        </Card>
        <Card>
          <CardHeader><CardTitle>Restore warning</CardTitle></CardHeader>
          <CardBody className="space-y-3">
            <p className="text-sm text-slate-600">Restore replaces the current local database. Continue only if you already created a current backup or accept the risk.</p>
            <StatusPill tone="danger">Typed confirmation required</StatusPill>
          </CardBody>
        </Card>
      </div>

      {message ? <Alert tone="success">{message}</Alert> : null}
      {error ? <Alert tone="danger">{error}</Alert> : null}
      {backupsQuery.isLoading ? <LoadingState label="Loading backups…" /> : null}
      {backupsQuery.isError ? <Alert tone="danger">{(backupsQuery.error as Error).message}</Alert> : null}

      <Card>
        <CardHeader><CardTitle>Known backup packages</CardTitle></CardHeader>
        <CardBody className="space-y-4">
          {backups.length === 0 && !backupsQuery.isLoading ? (
            <Alert tone="info" title="No backups found">
              Create a backup to begin.
            </Alert>
          ) : null}

          <div className="space-y-3">
            {backups.map((backup) => {
              const verification = verifications[backup.backupId] ?? null;
              const verified = isVerifiedRestorable(verification);
              const hash = firstFileHash(verification);
              return (
                <div key={backup.backupId} className="rounded-lg border border-slate-200 bg-white p-4 shadow-sm">
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div className="min-w-0 space-y-1">
                      <p className="break-all font-mono text-sm font-semibold text-slate-950">{backup.backupId}</p>
                      <p className="text-xs text-slate-500">Created: {formatDate(backup.createdAt)} · Size: {formatBytes(backup.sizeBytes)}</p>
                      {verification ? (
                        <div className="mt-2 grid gap-2 text-xs text-slate-600 md:grid-cols-3">
                          <span>Schema: {verification.manifest.schemaVersion}</span>
                          <span>Users: {verification.database.userCount ?? "n/a"}</span>
                          <span>Products: {verification.database.productCount ?? "n/a"}</span>
                          <span>Sales: {verification.database.saleCount ?? "n/a"}</span>
                          <span>Migration: {verification.database.backupMigrationVersion ?? "n/a"} / {verification.database.supportedMigrationVersion}</span>
                          <span>SHA-256: <span className="break-all font-mono">{hash ?? "n/a"}</span></span>
                        </div>
                      ) : null}
                    </div>
                    <div className="flex shrink-0 flex-wrap gap-2">
                      <StatusPill tone={verified ? "success" : verification ? "danger" : "neutral"}>{verified ? "Verified" : verification ? "Invalid" : "Unverified"}</StatusPill>
                      <Button variant="secondary" size="sm" disabled={verifyingId === backup.backupId || restoring} onClick={() => void handleVerifyBackup(backup.backupId)}>
                        {verifyingId === backup.backupId ? "Verifying…" : "Verify"}
                      </Button>
                      <Button variant="danger" size="sm" disabled={!verified || restoring} onClick={() => beginRestore(backup)}>
                        Restore
                      </Button>
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        </CardBody>
      </Card>

      {restoreTarget ? (
        <Card>
          <CardHeader><CardTitle>Confirm restore</CardTitle></CardHeader>
          <CardBody className="space-y-4">
            <Alert tone="danger" title="Destructive database replacement">
              Restore replaces the current local database from verified backup ID <span className="font-mono">{restoreTarget}</span>. Continue only if you already created a current backup or accept the risk.
            </Alert>
            {!isVerifiedRestorable(selectedVerification) ? <Alert tone="danger">This backup must be verified and restorable before restore is enabled.</Alert> : null}
            <label className="space-y-1 text-sm font-medium text-slate-700">
              <span>Type RESTORE to enable restore</span>
              <TextInput value={restoreText} onChange={(event) => setRestoreText(event.target.value)} placeholder="RESTORE" />
            </label>
            <div className="flex flex-wrap gap-2">
              <Button variant="danger" disabled={!restoreReady} onClick={() => void handleRestore()}>{restoring ? "Restoring…" : "Restore verified backup"}</Button>
              <Button variant="secondary" disabled={restoring} onClick={() => { setRestoreTarget(null); setRestoreText(""); }}>Cancel</Button>
            </div>
          </CardBody>
        </Card>
      ) : null}
    </div>
  );
}

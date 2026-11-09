import { useMemo, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  createBackup,
  downloadBackupFileUrl,
  importBackupFile,
  listBackups,
  restoreBackup,
  RESTORE_CONFIRMATION_INPUT,
  verifyBackup,
  type BackupSummary,
  type BackupVerification,
} from "../api/backups";
import { Alert, Button, Card, CardBody, CardHeader, CardTitle, CommandBar, Dialog, LoadingState, StatusPill, TextInput, useNotifications } from "../components/ui";
import { safeNotificationDescription } from "../utils/notificationError";

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

function VerificationSummary({ verification }: { verification: BackupVerification }) {
  return (
    <div className="mt-2 grid gap-2 text-xs text-slate-600 md:grid-cols-3">
      <span>Store: {verification.storeName?.trim() || "n/a"}</span>
      <span>Created: {formatDate(verification.manifest.createdAt)}</span>
      <span>Backup format version: {verification.manifest.schemaVersion} · {verification.database.backupMigrationVersion ?? "n/a"} / {verification.database.supportedMigrationVersion}</span>
      <span>Users: {verification.database.userCount ?? "n/a"}</span>
      <span>Products: {verification.database.productCount ?? "n/a"}</span>
      <span>Sales: {verification.database.saleCount ?? "n/a"}</span>
      <span>Logo / branding: {verification.hasLogo === true ? "Present" : verification.hasLogo === false ? "None" : "n/a"}</span>
      <span className="md:col-span-2">SHA-256: <span className="break-all font-mono">{firstFileHash(verification) ?? "n/a"}</span></span>
    </div>
  );
}

// Verified modal notices: Backup created successfully | Backup verified and restorable | Restore completed
export function BackupRestorePage() {
  const { notify } = useNotifications();
  const backupsQuery = useQuery({ queryKey: ["backup-packages"], queryFn: listBackups });
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [importing, setImporting] = useState(false);
  const [verifyingId, setVerifyingId] = useState<string | null>(null);
  const [restoreTarget, setRestoreTarget] = useState<string | null>(null);
  const [restoreText, setRestoreText] = useState("");
  const [restoring, setRestoring] = useState(false);
  const [verifications, setVerifications] = useState<Record<string, BackupVerification>>({});
  const importInputRef = useRef<HTMLInputElement | null>(null);

  const backups = useMemo(() => backupsQuery.data ?? [], [backupsQuery.data]);
  const selectedVerification = restoreTarget ? verifications[restoreTarget] ?? null : null;
  const restoreReady = Boolean(restoreTarget && isVerifiedRestorable(selectedVerification) && restoreText.trim().length > 0 && !restoring);

  async function handleCreateBackup() {
    setCreating(true);
    setMessage(null);
    setError(null);
    try {
      const created = await createBackup();
      setMessage(`Backup created: ${created.backupId}`);
      notify.success("Backup created", "Keep a copy outside this computer.");
      await backupsQuery.refetch();
    } catch (e) {
      setError((e as Error).message);
      notify.error("Backup was not created", safeNotificationDescription(e, "The backup was not created. Try again."));
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
      if (verification.database.isRestorable) {
        notify.success("Backup verified", "This backup is restorable.");
      } else {
        notify.error("Backup verification failed", "This backup is not restorable.");
      }
    } catch (e) {
      setError((e as Error).message);
      notify.error("Backup verification failed", safeNotificationDescription(e, "The backup could not be verified."));
      setVerifications((current) => {
        const next = { ...current };
        delete next[backupId];
        return next;
      });
    } finally {
      setVerifyingId(null);
    }
  }

  async function handleImportBackup(file: File | null) {
    if (!file) return;
    setImporting(true);
    setMessage(null);
    setError(null);
    try {
      const verification = await importBackupFile(file);
      setVerifications((current) => ({ ...current, [verification.backupId]: verification }));
      setMessage(`Backup imported and verified: ${verification.backupId}`);
      notify.success("Backup imported", isVerifiedRestorable(verification) ? "The backup is verified and ready for restore review." : "The backup was imported but is not restorable.");
      await backupsQuery.refetch();
      if (isVerifiedRestorable(verification)) {
        setRestoreTarget(verification.backupId);
        setRestoreText("");
      }
    } catch (e) {
      setError((e as Error).message);
      notify.error("Backup was not imported", safeNotificationDescription(e, "Choose a valid Salryn backup file and try again."));
    } finally {
      setImporting(false);
      if (importInputRef.current) importInputRef.current.value = "";
    }
  }

  async function handleRestore() {
    if (!restoreTarget || !restoreReady) return;
    if (restoreText !== RESTORE_CONFIRMATION_INPUT) {
      const description = `Type ${RESTORE_CONFIRMATION_INPUT} exactly to continue.`;
      setError(description);
      notify.error("Restore phrase incorrect", description);
      return;
    }
    setRestoring(true);
    setMessage(null);
    setError(null);
    try {
      const result = await restoreBackup(restoreTarget, restoreText);
      setMessage(`Restore completed from ${restoreTarget}${result.completedAt ? ` at ${formatDate(result.completedAt)}` : ""}. Restart the app if the local API reports stale data.`);
      setRestoreText("");
      setRestoreTarget(null);
      notify.success("Restore completed", "The active database was replaced from the verified backup.");
      await backupsQuery.refetch();
    } catch (e) {
      setError((e as Error).message);
      notify.error("Restore failed", safeNotificationDescription(e, "The active database was not replaced."));
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
        subtitle="Create, download, import, verify, and restore local backups."
        status={<StatusPill tone="warning">Restore requires confirmation</StatusPill>}
        actions={(
          <Button variant="secondary" onClick={() => void backupsQuery.refetch()} disabled={backupsQuery.isFetching || creating || restoring || importing}>
            Refresh
          </Button>
        )}
      />

      <Alert tone="warning" title="Restore replaces the current local database">
        Verify a backup before restoring it. Keep a recent copy before you continue. Download Backup File packages are portable .salryn-backup.zip archives.
      </Alert>

      <div className="grid gap-4 lg:grid-cols-3">
        <Card>
          <CardHeader><CardTitle>Create backup</CardTitle></CardHeader>
          <CardBody className="space-y-3">
            <p className="text-sm text-slate-600">Create a backup of the current local database.</p>
            <Button onClick={() => void handleCreateBackup()} disabled={creating || restoring || importing}>{creating ? "Creating…" : "Create Backup"}</Button>
          </CardBody>
        </Card>
        <Card>
          <CardHeader><CardTitle>Download / Import</CardTitle></CardHeader>
          <CardBody className="space-y-3">
            <p className="text-sm text-slate-600">Download Backup File for portable transfer, or Import Backup File as an update/failure fallback.</p>
            <input
              ref={importInputRef}
              type="file"
              accept=".salryn-backup.zip,application/zip"
              className="block w-full text-xs"
              disabled={importing || restoring}
              onChange={(event) => void handleImportBackup(event.target.files?.[0] ?? null)}
            />
            <Button variant="secondary" disabled={importing || restoring} onClick={() => importInputRef.current?.click()}>
              {importing ? "Importing…" : "Import Backup File"}
            </Button>
            <StatusPill tone="info">Verify Backup before Restore Backup</StatusPill>
          </CardBody>
        </Card>
        <Card>
          <CardHeader><CardTitle>Restore warning</CardTitle></CardHeader>
          <CardBody className="space-y-3">
            <p className="text-sm text-slate-600">Restore replaces the current local database. The current database is archived automatically before restore. Continue only if you already created a current backup or accept the risk.</p>
            <StatusPill tone="danger">Type RESTORE SALRYN BACKUP to confirm</StatusPill>
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
              Create a backup or Import Backup File to begin.
            </Alert>
          ) : null}

          <div className="space-y-3">
            {backups.map((backup) => {
              const verification = verifications[backup.backupId] ?? null;
              const verified = isVerifiedRestorable(verification);
              return (
                <div key={backup.backupId} className="rounded-lg border border-slate-200 bg-white p-4 shadow-sm">
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div className="min-w-0 space-y-1">
                      <p className="break-all font-mono text-sm font-semibold text-slate-950">{backup.backupId}</p>
                      <p className="text-xs text-slate-500">Created: {formatDate(backup.createdAt)} · Size: {formatBytes(backup.sizeBytes)}</p>
                      {verification ? <VerificationSummary verification={verification} /> : null}
                    </div>
                    <div className="flex shrink-0 flex-wrap gap-2">
                      <StatusPill tone={verified ? "success" : verification ? "danger" : "neutral"}>{verified ? "Verified" : verification ? "Invalid" : "Unverified"}</StatusPill>
                      <Button variant="secondary" size="sm" disabled={verifyingId === backup.backupId || restoring || importing} onClick={() => void handleVerifyBackup(backup.backupId)}>
                        {verifyingId === backup.backupId ? "Verifying…" : "Verify Backup"}
                      </Button>
                      <a
                        className="inline-flex items-center rounded-md border border-slate-300 bg-white px-2.5 py-1.5 text-xs font-medium text-slate-800 shadow-sm hover:bg-slate-50"
                        href={downloadBackupFileUrl(backup.backupId)}
                      >
                        Download Backup File
                      </a>
                      <Button variant="danger" size="sm" disabled={!verified || restoring || importing} onClick={() => beginRestore(backup)}>
                        Restore Backup
                      </Button>
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        </CardBody>
      </Card>

      <Dialog
        open={Boolean(restoreTarget)}
        title="Confirm restore"
        description="Destructive database replacement. Type RESTORE SALRYN BACKUP to continue."
        onClose={() => {
          if (!restoring) {
            setRestoreTarget(null);
            setRestoreText("");
          }
        }}
        footer={(
          <>
            <Button variant="secondary" disabled={restoring} onClick={() => { setRestoreTarget(null); setRestoreText(""); }}>Cancel</Button>
            <Button variant="danger" disabled={!restoreReady} onClick={() => void handleRestore()}>{restoring ? "Restoring…" : "Restore Backup"}</Button>
          </>
        )}
      >
        <div className="space-y-4 text-sm">
          <Alert tone="danger" title="Restore replaces the current local database">
            Restore replaces the current local database from verified backup ID <span className="font-mono">{restoreTarget}</span>. The current database is archived before restore. Continue only if you already created a current backup or accept the risk.
          </Alert>
          {!isVerifiedRestorable(selectedVerification) ? <Alert tone="danger">This backup must be verified and restorable before restore is enabled.</Alert> : null}
          {selectedVerification ? <VerificationSummary verification={selectedVerification} /> : null}
          <label className="space-y-1 text-sm font-medium text-slate-700">
            <span>Type RESTORE SALRYN BACKUP to enable restore</span>
            <TextInput value={restoreText} onChange={(event) => setRestoreText(event.target.value)} placeholder="RESTORE SALRYN BACKUP" />
          </label>
        </div>
      </Dialog>
    </div>
  );
}

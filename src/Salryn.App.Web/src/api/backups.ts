import { readJson } from "./http";

export type BackupSummary = {
  backupId: string;
  createdAt: string;
  sizeBytes: number;
};

export type BackupManifestFile = {
  relativePath: string;
  sizeBytes: number;
  sha256: string;
};

export type BackupManifest = {
  backupId: string;
  createdAt: string;
  schemaVersion: number;
  files: BackupManifestFile[];
};

export type BackupDatabaseVerification = {
  sizeBytes: number;
  lastModifiedAt: string;
  coreTablesExist: boolean;
  appMetadataExists: boolean;
  migrationMetadataExists: boolean;
  userCount: number | null;
  productCount: number | null;
  saleCount: number | null;
  backupMigrationVersion: number | null;
  supportedMigrationVersion: number;
  criticalTablesExist: boolean;
  migrationVersionCompatible: boolean;
  missingCriticalTables: string[] | null;
  hasFutureMigrationVersion: boolean;
  isRestorable: boolean;
};

export type BackupVerification = {
  backupId: string;
  manifest: BackupManifest;
  database: BackupDatabaseVerification;
  storeName?: string | null;
  hasLogo?: boolean | null;
};

export type BackupRestoreResult = {
  completedAt: string;
};

/** Exact phrase operators type in UI and send to the restore API (must match backend). */
export const RESTORE_CONFIRMATION_INPUT = "RESTORE SALRYN BACKUP";

export function listBackups() {
  return fetch("/api/backups", { credentials: "include" }).then(readJson<BackupSummary[]>);
}

export function createBackup() {
  return fetch("/api/backups", { method: "POST", credentials: "include" }).then(readJson<BackupSummary>);
}

export function verifyBackup(backupId: string) {
  return fetch(`/api/backups/${encodeURIComponent(backupId)}/verify`, { method: "POST", credentials: "include" })
    .then(readJson<BackupVerification>);
}

export function restoreBackup(backupId: string, confirmation: string = RESTORE_CONFIRMATION_INPUT) {
  return fetch(`/api/backups/${encodeURIComponent(backupId)}/restore`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    credentials: "include",
    body: JSON.stringify({ confirmation }),
  }).then(readJson<BackupRestoreResult>);
}

/** Client-friendly download of a portable .salryn-backup.zip package. */
export function downloadBackupFileUrl(backupId: string): string {
  return `/api/backups/${encodeURIComponent(backupId)}/download`;
}

/** Import a portable .salryn-backup.zip package; returns verified package summary. */
export async function importBackupFile(file: File) {
  const body = new FormData();
  body.append("package", file);
  const response = await fetch("/api/backups/import", {
    method: "POST",
    credentials: "include",
    body,
  });
  return readJson<BackupVerification>(response);
}

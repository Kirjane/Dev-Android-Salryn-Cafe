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
};

export type BackupRestoreResult = {
  completedAt: string;
};

export const RESTORE_CONFIRMATION_INPUT = "RESTORE";
const BACKEND_RESTORE_CONFIRMATION = "RESTORE SALRYN BACKUP";

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

export function restoreBackup(backupId: string) {
  return fetch(`/api/backups/${encodeURIComponent(backupId)}/restore`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    credentials: "include",
    body: JSON.stringify({ confirmation: BACKEND_RESTORE_CONFIRMATION }),
  }).then(readJson<BackupRestoreResult>);
}

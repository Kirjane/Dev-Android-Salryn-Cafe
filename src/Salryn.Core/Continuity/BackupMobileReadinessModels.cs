namespace Salryn.Core.Continuity;

// Kirch: Defines the backup package category without implementing backup execution yet.
public enum SalrynBackupPackageType
{
    FullDatabase = 1,
    ManualFullDatabase = 2,
    ScheduledFullDatabase = 3,
    DiagnosticExport = 4
}

// Kirch: Defines whether a backup package is protected enough for client or maintainer handling.
public enum SalrynBackupProtectionMode
{
    Unprotected = 1,
    Encrypted = 2
}

// Kirch: Defines backup health for dashboard display without binding Core to a backup engine.
public enum SalrynBackupHealthState
{
    Unknown = 1,
    Healthy = 2,
    Warning = 3,
    Failed = 4
}

// Kirch: Defines how truthful a future mobile dashboard snapshot is about freshness.
public enum MobileSnapshotFreshness
{
    CurrentLanFetch = 1,
    LastFetchedSnapshot = 2,
    StaleSnapshot = 3
}

// Kirch: Describes an encrypted backup package manifest without exposing raw database contents.
public sealed record BackupPackageManifest(
    Guid TenantId,
    Guid StoreId,
    Guid BackupId,
    DateTimeOffset BackupTime,
    string AppVersion,
    int SchemaVersion,
    string Checksum,
    string ChecksumAlgorithm,
    SalrynBackupPackageType BackupType,
    SalrynBackupProtectionMode ProtectionMode,
    Guid HostId,
    string HostMachineName,
    long DatabaseSizeBytes);

// Kirch: Summarizes backup health for owner/admin dashboards without implementing storage.
public sealed record BackupHealthSnapshot(
    Guid TenantId,
    Guid StoreId,
    SalrynBackupHealthState State,
    DateTimeOffset CheckedAt,
    DateTimeOffset? LastSuccessfulBackupAt,
    Guid? LastBackupId,
    string? Message);

// Kirch: Summarizes sales numbers for a future read-only owner dashboard snapshot.
public sealed record OwnerSalesSnapshot(
    long GrossSalesCents,
    long NetSalesCents,
    int CompletedSaleCount,
    int VoidedSaleCount);

// Kirch: Summarizes inventory alerts for a future read-only owner dashboard snapshot.
public sealed record OwnerInventorySnapshot(
    int LowStockProductCount,
    int NegativeStockProductCount,
    int TotalTrackedProductCount);

// Kirch: Summarizes cashier activity for a future read-only owner dashboard snapshot.
public sealed record OwnerCashierSnapshot(
    int OpenSessionCount,
    long ExpectedCashCents);

// Kirch: Carries the future mobile owner dashboard payload as read-only snapshot data.
public sealed record MobileOwnerDashboardSnapshot(
    Guid TenantId,
    Guid StoreId,
    Guid SnapshotId,
    DateTimeOffset GeneratedAt,
    DateTimeOffset? LastFetchedAt,
    MobileSnapshotFreshness Freshness,
    OwnerSalesSnapshot Sales,
    OwnerInventorySnapshot Inventory,
    OwnerCashierSnapshot Cashier,
    BackupHealthSnapshot BackupHealth);

// Kirch: Centralizes readiness rules before backup engines, cloud sync, or mobile apps exist.
public static class BackupMobileReadinessRules
{
    // Kirch: Primary database backups must be encrypted before they are safe for maintainer/cloud handling.
    public static bool IsProtectedDatabaseBackup(BackupPackageManifest manifest)
    {
        return (manifest.BackupType is SalrynBackupPackageType.FullDatabase
            or SalrynBackupPackageType.ManualFullDatabase
            or SalrynBackupPackageType.ScheduledFullDatabase)
            && manifest.ProtectionMode == SalrynBackupProtectionMode.Encrypted;
    }

    // Kirch: A restorable manifest must identify the client, store, backup, schema, host, and checksum.
    public static bool HasRestorableManifestIdentity(BackupPackageManifest manifest)
    {
        return manifest.TenantId != Guid.Empty
            && manifest.StoreId != Guid.Empty
            && manifest.BackupId != Guid.Empty
            && manifest.HostId != Guid.Empty
            && manifest.SchemaVersion > 0
            && manifest.DatabaseSizeBytes > 0
            && !string.IsNullOrWhiteSpace(manifest.Checksum)
            && !string.IsNullOrWhiteSpace(manifest.ChecksumAlgorithm);
    }

    // Kirch: Mobile snapshots outside LAN must clearly degrade to stale instead of pretending to be live.
    public static bool IsSnapshotStale(
        MobileOwnerDashboardSnapshot snapshot,
        DateTimeOffset now,
        TimeSpan maxAge)
    {
        return now - snapshot.GeneratedAt > maxAge;
    }

    // Kirch: Mobile V1 is read-only, so dashboard snapshots must never represent write-capable operations.
    public static bool IsReadOnlyMobileSnapshot(MobileOwnerDashboardSnapshot snapshot)
    {
        return snapshot.SnapshotId != Guid.Empty
            && snapshot.TenantId != Guid.Empty
            && snapshot.StoreId != Guid.Empty;
    }
}

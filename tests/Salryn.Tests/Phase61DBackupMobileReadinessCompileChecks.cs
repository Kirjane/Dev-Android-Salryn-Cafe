using Salryn.Core.Continuity;

namespace Salryn.Tests;

// Kirch: Verifies Phase 61D backup and mobile snapshot readiness models compile without implementing backup or mobile runtime.
public static class Phase61DBackupMobileReadinessCompileChecks
{
    // Kirch: This compile check protects backup manifest and mobile snapshot shapes for future implementation phases.
    public static void Run()
    {
        Guid tenantId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        Guid backupId = Guid.NewGuid();
        Guid hostId = Guid.NewGuid();

        BackupPackageManifest manifest = new(
            TenantId: tenantId,
            StoreId: storeId,
            BackupId: backupId,
            BackupTime: DateTimeOffset.UtcNow,
            AppVersion: "0.1.0-phase61d",
            SchemaVersion: 1,
            Checksum: "sample-checksum",
            ChecksumAlgorithm: "SHA256",
            BackupType: SalrynBackupPackageType.FullDatabase,
            ProtectionMode: SalrynBackupProtectionMode.Encrypted,
            HostId: hostId,
            HostMachineName: "SALRYN-HOST",
            DatabaseSizeBytes: 1024);

        BackupHealthSnapshot backupHealth = new(
            TenantId: tenantId,
            StoreId: storeId,
            State: SalrynBackupHealthState.Healthy,
            CheckedAt: DateTimeOffset.UtcNow,
            LastSuccessfulBackupAt: DateTimeOffset.UtcNow,
            LastBackupId: backupId,
            Message: "Backup healthy.");

        MobileOwnerDashboardSnapshot snapshot = new(
            TenantId: tenantId,
            StoreId: storeId,
            SnapshotId: Guid.NewGuid(),
            GeneratedAt: DateTimeOffset.UtcNow,
            LastFetchedAt: DateTimeOffset.UtcNow,
            Freshness: MobileSnapshotFreshness.CurrentLanFetch,
            Sales: new OwnerSalesSnapshot(
                GrossSalesCents: 125000,
                NetSalesCents: 120000,
                CompletedSaleCount: 25,
                VoidedSaleCount: 1),
            Inventory: new OwnerInventorySnapshot(
                LowStockProductCount: 3,
                NegativeStockProductCount: 0,
                TotalTrackedProductCount: 150),
            Cashier: new OwnerCashierSnapshot(
                OpenSessionCount: 1,
                ExpectedCashCents: 120000),
            BackupHealth: backupHealth);

        if (!BackupMobileReadinessRules.HasRestorableManifestIdentity(manifest))
        {
            throw new InvalidOperationException("Backup manifest should contain restorable identity.");
        }

        if (!BackupMobileReadinessRules.IsProtectedDatabaseBackup(manifest))
        {
            throw new InvalidOperationException("Database backup should be protected.");
        }

        if (!BackupMobileReadinessRules.IsReadOnlyMobileSnapshot(snapshot))
        {
            throw new InvalidOperationException("Mobile dashboard snapshot should be read-only identity data.");
        }

        if (BackupMobileReadinessRules.IsSnapshotStale(
                snapshot,
                DateTimeOffset.UtcNow.AddMinutes(1),
                TimeSpan.FromMinutes(5)))
        {
            throw new InvalidOperationException("Fresh LAN snapshot should not be stale.");
        }
    }
}

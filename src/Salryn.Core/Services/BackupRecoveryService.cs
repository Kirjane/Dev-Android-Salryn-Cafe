using Salryn.Core.Common;

namespace Salryn.Core.Services;

public interface IBackupRecoveryService
{
    Result<string> CreateBackup();

    Result<IReadOnlyList<BackupFileSummary>> ListBackups();

    Result<BackupVerificationSummary> VerifyBackup(string backupFilePath);

    Result<BackupRestoreResult> RestoreVerifiedBackup(
        BackupVerificationSummary verifiedBackup,
        string confirmationText);
}

public sealed record BackupFileSummary(
    string FilePath,
    long SizeBytes,
    DateTimeOffset LastModifiedAt);

public sealed record BackupVerificationSummary(
    string FilePath,
    long SizeBytes,
    DateTimeOffset LastModifiedAt,
    bool CoreTablesExist,
    bool AppMetadataExists,
    bool MigrationMetadataExists,
    long? UserCount,
    long? ProductCount,
    long? SaleCount,
    int? BackupMigrationVersion = null,
    int SupportedMigrationVersion = 0,
    bool CriticalTablesExist = false,
    bool MigrationVersionCompatible = false,
    IReadOnlyList<string>? MissingCriticalTables = null)
{
    public bool HasFutureMigrationVersion =>
        BackupMigrationVersion.HasValue
        && SupportedMigrationVersion > 0
        && BackupMigrationVersion.Value > SupportedMigrationVersion;

    public bool IsRestorable =>
        CoreTablesExist
        && CriticalTablesExist
        && MigrationVersionCompatible
        && !HasFutureMigrationVersion;
}

public sealed record BackupRestoreResult(
    string RestoredBackupPath,
    string SafetyBackupPath,
    DateTimeOffset CompletedAt);

using Salryn.Core.Common;

namespace Salryn.Core.Services;

public interface IBackupRecoveryService
{
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
    long? SaleCount)
{
    public bool IsRestorable => CoreTablesExist;
}

public sealed record BackupRestoreResult(
    string RestoredBackupPath,
    string SafetyBackupPath,
    DateTimeOffset CompletedAt);

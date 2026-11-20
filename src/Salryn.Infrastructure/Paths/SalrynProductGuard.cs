using Salryn.Core.Common;

namespace Salryn.Infrastructure.Paths;

public enum BackupRestoreProductMode
{
    StandardCafeRestore = 1,
    LegacyYzaAdoptionRestore = 2
}

public sealed record BackupProductGuardResult(
    bool Allowed,
    BackupRestoreProductMode? Mode,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static BackupProductGuardResult Allow(BackupRestoreProductMode mode) =>
        new(true, mode, null, null);

    public static BackupProductGuardResult Deny(string errorCode, string errorMessage) =>
        new(false, null, errorCode, errorMessage);
}

/// <summary>
/// Fail-closed wrong-product isolation guards for Salryn Cafe across service identity,
/// licenses, backup manifests (including gated legacy Yza v39 adoption), and filesystem roots.
/// </summary>
public static class SalrynProductGuard
{
    public const string ErrorWrongProductIdentity = "ERR_WRONG_PRODUCT_IDENTITY";
    public const string ErrorWrongProductLicense = "ERR_WRONG_PRODUCT_LICENSE";
    public const string ErrorWrongProductBackup = "ERR_WRONG_PRODUCT_BACKUP";
    public const string ErrorBackupMissingProductId = "ERR_BACKUP_MISSING_PRODUCT_ID";
    public const string ErrorProtectedRetailPath = "ERR_PROTECTED_RETAIL_PATH";

    public static Result ValidateProductId(string? productId)
    {
        if (string.IsNullOrWhiteSpace(productId) ||
            !string.Equals(productId.Trim(), SalrynProductIdentity.ProductId, StringComparison.Ordinal))
        {
            return Result.Fail(Error.Failure(
                ErrorWrongProductIdentity,
                $"Expected product identity '{SalrynProductIdentity.ProductId}', but received '{productId ?? "<missing>"}'."));
        }

        return Result.Success();
    }

    public static Result ValidateLicenseProductId(string? applicationId)
    {
        if (string.IsNullOrWhiteSpace(applicationId) ||
            !string.Equals(applicationId.Trim(), SalrynProductIdentity.ProductId, StringComparison.Ordinal))
        {
            return Result.Fail(Error.Failure(
                ErrorWrongProductLicense,
                $"License ApplicationId must be '{SalrynProductIdentity.ProductId}'. Rejected '{applicationId ?? "<missing>"}'."));
        }

        return Result.Success();
    }

    /// <summary>
    /// Enforces ADR-I1 (#74 / #81 P1 #5):
    /// - Manifest with ProductId == "Salryn.Cafe" -> StandardCafeRestore.
    /// - Manifest without ProductId -> allowed ONLY when ADR-I2 pre-restore SQLite classifier
    ///   confirms YzaV39Legacy (LegacyYzaAdoptionRestore).
    /// - Missing ProductId without YzaV39Legacy verification -> FAIL CLOSED.
    /// - Any mismatched ProductId (e.g. "Salryn.POS") -> FAIL CLOSED.
    /// </summary>
    public static BackupProductGuardResult ValidateBackupManifestForRestore(
        string? manifestProductId,
        Func<bool>? isVerifiedYzaV39LegacyDb = null)
    {
        if (!string.IsNullOrWhiteSpace(manifestProductId))
        {
            string normalized = manifestProductId.Trim();
            if (string.Equals(normalized, SalrynProductIdentity.ProductId, StringComparison.Ordinal))
            {
                return BackupProductGuardResult.Allow(BackupRestoreProductMode.StandardCafeRestore);
            }

            return BackupProductGuardResult.Deny(
                ErrorWrongProductBackup,
                $"Backup manifest belongs to product '{normalized}', not '{SalrynProductIdentity.ProductId}'. Cross-product restore is blocked.");
        }

        if (isVerifiedYzaV39LegacyDb is not null && isVerifiedYzaV39LegacyDb())
        {
            return BackupProductGuardResult.Allow(BackupRestoreProductMode.LegacyYzaAdoptionRestore);
        }

        return BackupProductGuardResult.Deny(
            ErrorBackupMissingProductId,
            "Backup manifest omits product_id and did not pass YzaV39Legacy pre-restore classification. Restore is blocked.");
    }

    /// <summary>
    /// Rejects any candidate filesystem path that targets protected General Merchandise / Retail
    /// installation or data directories.
    /// </summary>
    public static Result ValidateNotProtectedRetailPath(string? candidatePath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return Result.Fail(Error.Failure(
                ErrorProtectedRetailPath,
                "Candidate path must not be empty."));
        }

        string fullPath = Path.GetFullPath(candidatePath.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        string[] protectedRoots =
        [
            SalrynProductIdentity.ProtectedRetailInstallDir,
            Path.Combine(programData, SalrynProductIdentity.ProtectedRetailProgramDataFolderName),
            Path.Combine(localAppData, SalrynProductIdentity.ProtectedRetailLocalAppDataFolderName)
        ];

        foreach (string rawRoot in protectedRoots)
        {
            if (string.IsNullOrWhiteSpace(rawRoot))
            {
                continue;
            }

            string root = Path.GetFullPath(rawRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Fail(Error.Failure(
                    ErrorProtectedRetailPath,
                    $"Path '{fullPath}' overlaps protected retail path '{root}'. Salryn Cafe must remain isolated."));
            }
        }

        return Result.Success();
    }
}

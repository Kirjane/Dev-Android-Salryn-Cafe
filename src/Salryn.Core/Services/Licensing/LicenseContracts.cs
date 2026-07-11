namespace Salryn.Core.Services.Licensing;

public enum LicenseStatus
{
    Unlicensed = 0,
    Licensed = 1,
    Expired = 2,
    Invalid = 3,
    Grace = 4,
    Development = 5
}

public enum LicenseEdition
{
    Trial = 0,
    Local = 1,
    Professional = 2,
    Enterprise = 3
}

public static class LicenseFeatures
{
    public const string Pos = "Pos";
    public const string Rmo = "Rmo";
    public const string Reports = "Reports";
    public const string Users = "Users";
    public const string Backup = "Backup";
    public const string Licensing = "Licensing";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Pos,
        Rmo,
        Reports,
        Users,
        Backup,
        Licensing
    };

    public static IReadOnlyList<string> AllList { get; } = new[]
    {
        Pos,
        Rmo,
        Reports,
        Users,
        Backup,
        Licensing
    };
}

public sealed record LocalLicenseDocument(
    string? LicenseId,
    string? CustomerName,
    LicenseEdition Edition,
    DateTimeOffset IssuedAt,
    DateTimeOffset? ExpiresAt,
    string? MachineBinding,
    IReadOnlyList<string>? Features,
    string? Signature,
    int SchemaVersion);

public sealed record LocalLicenseValidationResult(
    LicenseStatus Status,
    LicenseEdition? Edition,
    IReadOnlyList<string> EnabledFeatures,
    DateTimeOffset? ExpiresAt,
    int? DaysRemaining,
    bool MachineMatched,
    string Message,
    string SourcePath,
    DateTimeOffset ValidationTimestamp);

public sealed record LocalLicenseInstallRequest(
    string? LicenseJson);

public sealed record LocalLicenseInstallResponse(
    string InstallStatus,
    LicenseStatus ValidationStatus,
    string? SourcePath,
    string? BackupPath,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset Timestamp);

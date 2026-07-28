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

/// <summary>
/// Host = licensed store workstation. Terminal = one paid LAN operational device.
/// </summary>
public enum LicenseType
{
    Host = 0,
    Terminal = 1
}

public static class LicenseFeatures
{
    public const string Pos = "Pos";
    public const string Rmo = "Rmo";
    public const string Reports = "Reports";
    public const string Users = "Users";
    public const string Backup = "Backup";
    public const string Licensing = "Licensing";
    public const string LanTerminals = "LanTerminals";
    public const string ReceivablesUtangLedger = "ReceivablesUtangLedger";

    /// <summary>Features a Terminal license may enable (LAN terminal surface only).</summary>
    public static IReadOnlySet<string> TerminalAllowed { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        LanTerminals
    };

    /// <summary>Features that require a Host license (not unlocked by Terminal).</summary>
    public static IReadOnlySet<string> HostOnly { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Pos,
        Rmo,
        Reports,
        Users,
        Backup,
        Licensing,
        ReceivablesUtangLedger
    };

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Pos,
        Rmo,
        Reports,
        Users,
        Backup,
        Licensing,
        LanTerminals,
        ReceivablesUtangLedger
    };

    public static IReadOnlyList<string> AllList { get; } = new[]
    {
        Pos,
        Rmo,
        Reports,
        Users,
        Backup,
        Licensing,
        LanTerminals,
        ReceivablesUtangLedger
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
    int SchemaVersion,
    string? ApplicationId = null,
    string? StoreId = null,
    int? AllowedLanTerminals = null,
    DateTimeOffset? SupportExpiresAt = null,
    DateTimeOffset? UpdatesUntil = null,
    LicenseType? LicenseType = null,
    string? HostLicenseId = null,
    string? TerminalId = null);

public sealed record LocalLicenseValidationResult(
    LicenseStatus Status,
    LicenseEdition? Edition,
    IReadOnlyList<string> EnabledFeatures,
    DateTimeOffset? ExpiresAt,
    int? DaysRemaining,
    bool MachineMatched,
    string Message,
    string SourcePath,
    DateTimeOffset ValidationTimestamp,
    string? LicenseId = null,
    string? CustomerName = null,
    string? ApplicationId = null,
    string? StoreId = null,
    int? AllowedLanTerminals = null,
    DateTimeOffset? SupportExpiresAt = null,
    DateTimeOffset? UpdatesUntil = null,
    string? MachineFingerprint = null,
    LicenseType? LicenseType = null,
    string? HostLicenseId = null,
    string? TerminalId = null,
    string? MachineCode = null);

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

public enum LicenseEnforcementReason
{
    LicensedFeatureAllowed = 0,
    DevelopmentModeAllowed = 1,
    MissingLicense = 2,
    InvalidLicense = 3,
    ExpiredLicense = 4,
    FeatureNotLicensed = 5,
    UnknownFeature = 6,
    VerifierUnavailable = 7,
    MachineMismatch = 8,
    WrongLicenseType = 9
}

public sealed record LicenseEnforcementDecision(
    bool Allowed,
    string RequiredFeature,
    LicenseStatus LicenseStatus,
    LicenseEnforcementReason ReasonCode,
    string Message,
    IReadOnlyList<string> EnabledFeatures);

public sealed record LocalLicenseEntitlementsResponse(
    LicenseStatus LicenseStatus,
    IReadOnlyList<string> EnabledFeatures,
    IReadOnlyList<string> KnownFeatures,
    IReadOnlyList<LicenseEnforcementDecision> FeatureDecisions,
    bool MachineMatched,
    string Message,
    DateTimeOffset Timestamp,
    LicenseType? LicenseType = null,
    string? HostLicenseId = null,
    string? TerminalId = null,
    string? MachineCode = null,
    int? AllowedLanTerminals = null);

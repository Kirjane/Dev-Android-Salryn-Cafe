namespace Salryn.Infrastructure.Paths;

/// <summary>
/// Stable local data contract for future installer/update work. This is documentation-as-code:
/// it does not move paths, migrate data, or implement installer behavior.
/// </summary>
public static class SalrynLocalDataContract
{
    public const string ProductionRootOwner = "ProgramData/Salryn";
    public const string DevelopmentRootOwner = ".salryn-dev";

    public static IReadOnlyList<SalrynLocalPathContractEntry> RequiredEntries =>
    [
        new("database", "data/salryn.db", mustPreserveAcrossUpdate: true, mayDeleteOnUninstallOnlyWithOperatorConsent: true),
        new("backups", "backups", mustPreserveAcrossUpdate: true, mayDeleteOnUninstallOnlyWithOperatorConsent: true),
        new("exports", "exports", mustPreserveAcrossUpdate: true, mayDeleteOnUninstallOnlyWithOperatorConsent: true),
        new("logs", "logs", mustPreserveAcrossUpdate: false, mayDeleteOnUninstallOnlyWithOperatorConsent: false),
        new("receipts", "receipts", mustPreserveAcrossUpdate: true, mayDeleteOnUninstallOnlyWithOperatorConsent: true),
        new("config", "config", mustPreserveAcrossUpdate: true, mayDeleteOnUninstallOnlyWithOperatorConsent: true),
        new("license", "config/license.json", mustPreserveAcrossUpdate: true, mayDeleteOnUninstallOnlyWithOperatorConsent: true),
        new("license-backups", "config/license-backups", mustPreserveAcrossUpdate: true, mayDeleteOnUninstallOnlyWithOperatorConsent: true),
        new("native-pdf-reports", "LocalApplicationData/Salryn/Reports/NativePdf", mustPreserveAcrossUpdate: true, mayDeleteOnUninstallOnlyWithOperatorConsent: true)
    ];

    public static bool IsKnownEntry(string name) => RequiredEntries.Any(
        entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
}

public sealed record SalrynLocalPathContractEntry(
    string Name,
    string RelativePath,
    bool MustPreserveAcrossUpdate,
    bool MayDeleteOnUninstallOnlyWithOperatorConsent);

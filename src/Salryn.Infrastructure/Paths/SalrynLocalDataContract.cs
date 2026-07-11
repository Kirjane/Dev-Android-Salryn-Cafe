namespace Salryn.Infrastructure.Paths;

/// <summary>
/// Stable local data contract for future installer/update work. This is documentation-as-code:
/// it does not move paths, migrate data, or implement installer behavior.
/// </summary>
public static class SalrynLocalDataContract
{
    public const string ProductionRootOwner = "ProgramData/Salryn";
    public const string DevelopmentRootOwner = ".salryn-dev";
    public const string LocalLicenseRootOwner = "LocalApplicationData/Salryn/Licensing";
    public const string NativePdfReportRootOwner = "LocalApplicationData/Salryn/Reports/NativePdf";

    public static IReadOnlyList<SalrynLocalPathContractEntry> RequiredEntries =>
    [
        new("database", "ProgramData/Salryn/data/salryn.db", mustPreserveAcrossUpdate: true, mayDeleteOnUninstallOnlyWithOperatorConsent: true),
        new("backups", "ProgramData/Salryn/backups", mustPreserveAcrossUpdate: true, mayDeleteOnUninstallOnlyWithOperatorConsent: true),
        new("exports", "ProgramData/Salryn/exports", mustPreserveAcrossUpdate: true, mayDeleteOnUninstallOnlyWithOperatorConsent: true),
        new("logs", "ProgramData/Salryn/logs", mustPreserveAcrossUpdate: false, mayDeleteOnUninstallOnlyWithOperatorConsent: false),
        new("receipts", "ProgramData/Salryn/receipts", mustPreserveAcrossUpdate: true, mayDeleteOnUninstallOnlyWithOperatorConsent: true),
        new("config", "ProgramData/Salryn/config", mustPreserveAcrossUpdate: true, mayDeleteOnUninstallOnlyWithOperatorConsent: true),
        new("license", "LocalApplicationData/Salryn/Licensing/license.json", mustPreserveAcrossUpdate: true, mayDeleteOnUninstallOnlyWithOperatorConsent: true),
        new("license-backups", "LocalApplicationData/Salryn/Licensing/*.bak.json", mustPreserveAcrossUpdate: true, mayDeleteOnUninstallOnlyWithOperatorConsent: true),
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

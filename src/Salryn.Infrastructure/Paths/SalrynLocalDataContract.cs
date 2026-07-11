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
        new("database", "ProgramData/Salryn/data/salryn.db", MustPreserveAcrossUpdate: true, MayDeleteOnUninstallOnlyWithOperatorConsent: true),
        new("backups", "ProgramData/Salryn/backups", MustPreserveAcrossUpdate: true, MayDeleteOnUninstallOnlyWithOperatorConsent: true),
        new("exports", "ProgramData/Salryn/exports", MustPreserveAcrossUpdate: true, MayDeleteOnUninstallOnlyWithOperatorConsent: true),
        new("logs", "ProgramData/Salryn/logs", MustPreserveAcrossUpdate: false, MayDeleteOnUninstallOnlyWithOperatorConsent: false),
        new("receipts", "ProgramData/Salryn/receipts", MustPreserveAcrossUpdate: true, MayDeleteOnUninstallOnlyWithOperatorConsent: true),
        new("config", "ProgramData/Salryn/config", MustPreserveAcrossUpdate: true, MayDeleteOnUninstallOnlyWithOperatorConsent: true),
        new("license", "LocalApplicationData/Salryn/Licensing/license.json", MustPreserveAcrossUpdate: true, MayDeleteOnUninstallOnlyWithOperatorConsent: true),
        new("license-backups", "LocalApplicationData/Salryn/Licensing/*.bak.json", MustPreserveAcrossUpdate: true, MayDeleteOnUninstallOnlyWithOperatorConsent: true),
        new("native-pdf-reports", "LocalApplicationData/Salryn/Reports/NativePdf", MustPreserveAcrossUpdate: true, MayDeleteOnUninstallOnlyWithOperatorConsent: true)
    ];

    public static bool IsKnownEntry(string name) => RequiredEntries.Any(
        entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
}

public sealed record SalrynLocalPathContractEntry(
    string Name,
    string RelativePath,
    bool MustPreserveAcrossUpdate,
    bool MayDeleteOnUninstallOnlyWithOperatorConsent);

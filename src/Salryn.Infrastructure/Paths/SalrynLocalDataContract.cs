namespace Salryn.Infrastructure.Paths;

/// <summary>
/// Stable local data contract for future installer/update work. This is documentation-as-code:
/// it does not move paths, migrate data, or implement installer behavior.
/// </summary>
public static class SalrynLocalDataContract
{
    public const string ProductId = SalrynProductIdentity.ProductId;
    public const string ProductionRootOwner = "ProgramData/SalrynCafe";
    public const string DevelopmentRootOwner = ".salryn-cafe-dev";
    public const string LocalLicenseRootOwner = "LocalApplicationData/SalrynCafe/Licensing";
    public const string NativePdfReportRootOwner = "LocalApplicationData/SalrynCafe/Reports/NativePdf";
    public const string WebView2ProfileRootOwner = "LocalApplicationData/SalrynCafe/WebView2";

    public static IReadOnlyList<SalrynLocalPathContractEntry> RequiredEntries =>
    [
        new("database", "ProgramData/SalrynCafe/data/salryn-cafe.db", MustPreserveAcrossUpdate: true, MayDeleteOnUninstallOnlyWithOperatorConsent: true),
        new("backups", "ProgramData/SalrynCafe/backups", MustPreserveAcrossUpdate: true, MayDeleteOnUninstallOnlyWithOperatorConsent: true),
        new("exports", "ProgramData/SalrynCafe/exports", MustPreserveAcrossUpdate: true, MayDeleteOnUninstallOnlyWithOperatorConsent: true),
        new("logs", "ProgramData/SalrynCafe/logs", MustPreserveAcrossUpdate: false, MayDeleteOnUninstallOnlyWithOperatorConsent: false),
        new("receipts", "ProgramData/SalrynCafe/receipts", MustPreserveAcrossUpdate: true, MayDeleteOnUninstallOnlyWithOperatorConsent: true),
        new("config", "ProgramData/SalrynCafe/config", MustPreserveAcrossUpdate: true, MayDeleteOnUninstallOnlyWithOperatorConsent: true),
        new("license", "LocalApplicationData/SalrynCafe/Licensing/license.json", MustPreserveAcrossUpdate: true, MayDeleteOnUninstallOnlyWithOperatorConsent: true),
        new("license-backups", "LocalApplicationData/SalrynCafe/Licensing/*.bak.json", MustPreserveAcrossUpdate: true, MayDeleteOnUninstallOnlyWithOperatorConsent: true),
        new("native-pdf-reports", "LocalApplicationData/SalrynCafe/Reports/NativePdf", MustPreserveAcrossUpdate: true, MayDeleteOnUninstallOnlyWithOperatorConsent: true),
        new("webview2-profile", "LocalApplicationData/SalrynCafe/WebView2", MustPreserveAcrossUpdate: false, MayDeleteOnUninstallOnlyWithOperatorConsent: false)
    ];

    public static bool IsKnownEntry(string name) => RequiredEntries.Any(
        entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
}

public sealed record SalrynLocalPathContractEntry(
    string Name,
    string RelativePath,
    bool MustPreserveAcrossUpdate,
    bool MayDeleteOnUninstallOnlyWithOperatorConsent);

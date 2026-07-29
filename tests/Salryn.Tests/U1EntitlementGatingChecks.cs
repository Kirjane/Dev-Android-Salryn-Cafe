using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Licensing;

namespace Salryn.Tests;

public static class U1EntitlementGatingChecks
{
    public static async Task RunAsync()
    {
        LicenseEnforcementDecision baseHost = await EnforceAsync(new[] { LicenseFeatures.Pos });
        Require(!baseHost.Allowed && baseHost.ReasonCode == LicenseEnforcementReason.FeatureNotLicensed,
            "Base Host license must not enable U1.");

        LicenseEnforcementDecision paidHost = await EnforceAsync(new[]
        {
            LicenseFeatures.Pos,
            LicenseFeatures.ReceivablesUtangLedger
        });
        Require(paidHost.Allowed, "Host license with the U1 feature must enable entitlement.");

        LicenseEnforcementDecision terminal = await EnforceAsync(
            new[] { LicenseFeatures.LanTerminals, LicenseFeatures.ReceivablesUtangLedger },
            LicenseType.Terminal);
        Require(!terminal.Allowed && terminal.ReasonCode == LicenseEnforcementReason.WrongLicenseType,
            "Terminal license must not unlock the Host-only U1 feature.");

        string endpoints = ReadRepo("src", "Salryn.Api.Local", "LocalAccountsReceivableEndpoints.cs");
        string checkout = ReadRepo("src", "Salryn.Api.Local", "LocalHostCheckoutEndpoints.cs");
        string reports = ReadRepo("src", "Salryn.Api.Local", "LocalOwnerReportsEndpoints.cs");
        string navigation = ReadRepo("src", "Salryn.App.Web", "src", "navigation.ts");
        string receivablesPage = ReadRepo("src", "Salryn.App.Web", "src", "pages", "ReceivablesPage.tsx");
        string receivablesService = ReadRepo(
            "src", "Salryn.Infrastructure", "Data", "Services", "SqliteAccountsReceivableService.cs");
        Require(endpoints.Contains("LicenseFeatures.ReceivablesUtangLedger", StringComparison.Ordinal),
            "Every U1 endpoint must pass through the central U1 license guard.");
        Require(checkout.Contains("LicenseFeatures.ReceivablesUtangLedger", StringComparison.Ordinal) &&
                checkout.Contains("AccountsReceivable", StringComparison.Ordinal),
            "Utang checkout must enforce the U1 entitlement server-side.");
        Require(reports.Contains("receivablesDecision.Allowed && canViewReceivables", StringComparison.Ordinal) &&
                reports.Contains("LocalApiPermissions.ReceivablesView", StringComparison.Ordinal),
            "AR reports must require both the U1 entitlement and Receivables.View.");
        Require(navigation.Contains("ReceivablesUtangLedger", StringComparison.Ordinal),
            "Host navigation must combine entitlement with page permission.");
        foreach (string label in new[]
                 {
                     "Credit Sale", "Partial Credit Sale", "Payment Collection",
                     "Collection Reversal", "Credit Sale Reversal", "Adjustment"
                 })
            Require(receivablesPage.Contains($"\"{label}\"", StringComparison.Ordinal),
                $"Receivables ledger must expose the human-readable label: {label}.");
        Require(receivablesPage.Contains("Collection Receipt", StringComparison.Ordinal) &&
                receivablesPage.Contains("Print acknowledgment", StringComparison.Ordinal),
            "Collection UI must expose the operational payment acknowledgment.");
        Require(receivablesService.Contains("CustomerQueryLimit = 100", StringComparison.Ordinal) &&
                receivablesService.Contains("LedgerQueryLimit = 500", StringComparison.Ordinal),
            "Receivables customer and ledger queries must remain bounded.");

        Console.WriteLine("U1 ENTITLEMENT GATING: base denied, paid Host allowed, Terminal denied, runtime boundaries present PASS.");
    }

    private static async Task<LicenseEnforcementDecision> EnforceAsync(
        IReadOnlyList<string> features,
        LicenseType type = LicenseType.Host)
    {
        LocalLicenseValidationResult status = new(
            LicenseStatus.Licensed,
            LicenseEdition.Local,
            features,
            ExpiresAt: null,
            DaysRemaining: null,
            MachineMatched: true,
            Message: "Test license.",
            SourcePath: "test",
            ValidationTimestamp: DateTimeOffset.UtcNow,
            LicenseType: type);
        return await new LocalLicenseEnforcementService(new StubLicenseService(status))
            .RequireFeatureAsync(LicenseFeatures.ReceivablesUtangLedger);
    }

    private static string ReadRepo(params string[] parts)
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Salryn.sln")))
        {
            DirectoryInfo? parent = Directory.GetParent(root);
            if (parent is null) throw new InvalidOperationException("Repository root not found.");
            root = parent.FullName;
        }
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class StubLicenseService(LocalLicenseValidationResult status) : ILocalLicenseService
    {
        public Task<LocalLicenseValidationResult> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(status);

        public Task<LocalLicenseInstallResponse> InstallAsync(
            LocalLicenseInstallRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> HasFeatureAsync(string feature, CancellationToken cancellationToken = default) =>
            Task.FromResult(status.EnabledFeatures.Contains(feature, StringComparer.Ordinal));
    }
}

namespace Salryn.Tests;

public static class J21Rmo242023CheckoutOfficialModeUiReadinessChecks
{
    public static async Task RunAsync()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string boundaryText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Checkout", "CheckoutOfficialModeReadinessBoundary.cs");
        string officialCompletionText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Checkout", "OfficialCheckoutCompletionBoundary.cs");
        string presenterText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Checkout", "CheckoutPresenter.cs");
        string formText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Checkout", "CheckoutShellForm.cs");
        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string shellText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(boundaryText, "CheckActiveScopeAsync", "Checkout official mode readiness boundary must use backend active-scope readiness.");
        RequireContains(boundaryText, "Official output locked", "Checkout official mode boundary must expose a locked display state.");
        RequireContains(officialCompletionText, "public interface IOfficialCheckoutCompletionBoundary", "Checkout must expose a dedicated official completion boundary contract.");
        RequireContains(officialCompletionText, "ITrustedPosCheckoutSaleBuilder", "Official completion boundary must use trusted sale building before official persistence.");
        RequireContains(officialCompletionText, "ICashierSessionService", "Official completion boundary must enforce the standard open cashier shift gate before official persistence.");
        RequireContains(officialCompletionText, "IOfficialCheckoutPersistenceBoundary", "Official completion boundary must persist through an app-level official persistence gateway.");
        RequireContains(presenterText, "RefreshOfficialModeReadinessAsync", "Checkout presenter must expose official readiness refresh.");
        RequireContains(presenterText, "IOfficialCheckoutCompletionBoundary? officialCompletionBoundary", "Checkout presenter must accept dedicated official completion wiring.");
        RequireContains(presenterText, "_officialCompletionBoundary!.CompleteSaleAsync", "Checkout presenter must route official submit through official completion boundary.");
        RequireContains(presenterText, "CanCompleteSale: !saleCompleted && canExecuteSelectedMode", "Checkout presenter must enable completion only for the selected wired mode.");
        RequireNotContains(presenterText, "CheckoutOfficialMode.CompletionNotWired", "Official UI mode must no longer rely on the old not-wired completion lock.");
        RequireContains(formText, "_checkoutModeComboBox", "Checkout form must render an official/standard mode selector.");
        RequireContains(formText, "_presenter.RefreshOfficialModeReadinessAsync()", "Checkout form must refresh official readiness from the presenter.");
        RequireContains(appServicesText, "public ICheckoutOfficialModeReadinessBoundary CheckoutOfficialModeReadiness { get; }", "AppServices must expose checkout official readiness.");
        RequireContains(appServicesText, "public IOfficialCheckoutCompletionBoundary OfficialCheckoutCompletion { get; }", "AppServices must expose checkout official completion.");
        RequireContains(appServicesText, "new CheckoutOfficialModeReadinessBoundary", "AppServices must compose the checkout official readiness boundary.");
        RequireContains(appServicesText, "new OfficialCheckoutCompletionBoundary", "AppServices must compose the checkout official completion boundary.");
        RequireContains(appServicesText, "new SqliteOfficialCheckoutPersistenceBoundary", "AppServices must adapt official completion to SQLite official persistence inside composition.");
        RequireContains(shellText, "officialModeReadinessBoundary: _services.CheckoutOfficialModeReadiness", "Shell checkout route must pass official readiness to the presenter.");
        RequireContains(shellText, "officialCompletionBoundary: _services.OfficialCheckoutCompletion", "Shell checkout route must pass official completion to the presenter.");
        RequireContains(coreSmokeText, "J21Rmo242023CheckoutOfficialModeUiReadinessChecks.RunAsync().GetAwaiter().GetResult();", "CoreSmoke must run official mode UI readiness checks.");

        await Task.CompletedTask;
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        current = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Salryn repository root.");
    }

    private static string ReadRequiredFile(DirectoryInfo repoRoot, params string[] pathParts)
    {
        string path = repoRoot.FullName;

        foreach (string pathPart in pathParts)
        {
            path = Path.Combine(path, pathPart);
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required file was not found: {path}");
        }

        return File.ReadAllText(path);
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

}

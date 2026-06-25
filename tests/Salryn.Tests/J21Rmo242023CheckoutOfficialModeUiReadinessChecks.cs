namespace Salryn.Tests;

public static class J21Rmo242023CheckoutOfficialModeUiReadinessChecks
{
    public static async Task RunAsync()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string boundaryText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Checkout", "CheckoutOfficialModeReadinessBoundary.cs");
        string presenterText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Checkout", "CheckoutPresenter.cs");
        string formText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Checkout", "CheckoutShellForm.cs");
        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string shellText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(boundaryText, "CheckActiveScopeAsync", "Checkout official mode readiness boundary must use backend active-scope readiness.");
        RequireContains(boundaryText, "Official output locked", "Checkout official mode boundary must expose a locked display state.");
        RequireContains(presenterText, "RefreshOfficialModeReadinessAsync", "Checkout presenter must expose official readiness refresh.");
        RequireContains(presenterText, "CheckoutOfficialMode.CompletionNotWired", "Official UI mode must block completion until official submit wiring exists.");
        RequireContains(presenterText, "CanCompleteSale: !saleCompleted && !_isOfficialModeSelected", "Standard submit must be disabled while official mode is selected.");
        RequireContains(formText, "_checkoutModeComboBox", "Checkout form must render an official/standard mode selector.");
        RequireContains(formText, "_presenter.RefreshOfficialModeReadinessAsync()", "Checkout form must refresh official readiness from the presenter.");
        RequireContains(appServicesText, "public ICheckoutOfficialModeReadinessBoundary CheckoutOfficialModeReadiness { get; }", "AppServices must expose checkout official readiness.");
        RequireContains(appServicesText, "new CheckoutOfficialModeReadinessBoundary", "AppServices must compose the checkout official readiness boundary.");
        RequireContains(shellText, "officialModeReadinessBoundary: _services.CheckoutOfficialModeReadiness", "Shell checkout route must pass official readiness to the presenter.");
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

}

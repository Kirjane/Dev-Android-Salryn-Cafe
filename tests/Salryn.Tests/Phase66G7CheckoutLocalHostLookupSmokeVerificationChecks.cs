namespace Salryn.Tests;

public static class Phase66G7CheckoutLocalHostLookupSmokeVerificationChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string solutionText = ReadRequiredFile(repoRoot, "Salryn.sln");
        string projectText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CheckoutLocalHostSmoke", "Salryn.Tools.CheckoutLocalHostSmoke.csproj");
        string programText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CheckoutLocalHostSmoke", "Program.cs");
        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string shellText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string presenterText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Checkout", "CheckoutPresenter.cs");
        string handoffText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_66G7_CHECKOUT_LOCAL_HOST_LOOKUP_SMOKE.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(solutionText, "Salryn.Tools.CheckoutLocalHostSmoke", "66G7 smoke tool must be registered in the solution.");
        RequireContains(projectText, "<TargetFramework>net10.0-windows</TargetFramework>", "66G7 smoke tool must target Windows app layer.");
        RequireContains(projectText, "..\\..\\src\\Salryn.App.WinForms\\Salryn.App.WinForms.csproj", "66G7 smoke tool must reference App.WinForms.");

        RequireContains(programText, "CheckoutLocalHostLookupSmokeChecks.RunAsync", "66G7 smoke must execute the local checkout lookup smoke.");
        RequireContains(programText, "new CheckoutLocalHostProductSearchBoundary(gateway)", "66G7 smoke must use the Local Host-backed checkout boundary.");
        RequireContains(programText, "new CheckoutPresenter(boundary)", "66G7 smoke must exercise CheckoutPresenter through the boundary.");
        RequireContains(programText, "SearchProductAsync", "66G7 smoke must execute product search.");
        RequireContains(programText, "AddPreparedProductLine", "66G7 smoke must add the prepared product into checkout draft.");
        RequireContains(programText, "AppLocalHostProductLookupResult.FoundProduct", "66G7 smoke must use the enriched Local Host product payload.");
        RequireContains(programText, "AppLocalHostProductLookupResult.NotFound", "66G7 smoke must verify the not-found path.");
        RequireContains(programText, "AppLocalHostProductLookupResult.LocalHostUnavailable", "66G7 smoke must verify unavailable failure mapping.");
        RequireContains(programText, "PreparedProduct", "66G7 smoke must verify prepared product state.");
        RequireContains(programText, "CheckoutLineViewModel", "66G7 smoke must verify checkout line state.");
        RequireContains(programText, "Totals.SubtotalCents", "66G7 smoke must verify checkout totals.");

        RequireContains(appServicesText, "new CheckoutLocalHostProductSearchBoundary(LocalHostProductLookupGateway)", "66G7 requires 66G6 wiring to remain active.");
        RequireContains(shellText, "_services.CheckoutProductSearch", "Shell must still pass CheckoutProductSearch.");
        RequireNotContains(shellText, "new CheckoutLocalHostProductSearchBoundary", "Shell must not instantiate the Local Host checkout boundary directly.");
        RequireNotContains(shellText, "_services.LocalHostProductLookupGateway", "Shell must not directly pass LocalHostProductLookupGateway.");
        RequireContains(presenterText, "ICheckoutProductSearchBoundary", "CheckoutPresenter must remain boundary-based.");
        RequireNotContains(presenterText, "IAppLocalHostProductLookupGateway", "CheckoutPresenter must not know the Local Host gateway.");
        RequireNotContains(presenterText, "CheckoutLocalHostProductSearchBoundary", "CheckoutPresenter must not know the Local Host adapter class.");

        foreach (string forbidden in new[]
        {
            "HttpClient",
            "Socket",
            "Tcp",
            "Udp",
            "Kestrel",
            "SignalR"
        })
        {
            RequireNotContains(projectText + programText + appServicesText + shellText + presenterText, forbidden, $"66G7 must not add transport marker {forbidden}.");
        }

        RequireContains(handoffText, "Status: End-to-end local checkout lookup smoke verification only.", "66G7 handoff must mark smoke-only scope.");
        RequireContains(handoffText, "No transport was added.", "66G7 handoff must block transport.");
        RequireContains(handoffText, "No LAN behavior was added.", "66G7 handoff must block LAN.");

        RequireContains(coreSmokeText, "Phase66G6CheckoutLocalHostProductSearchWiringChecks.Run();", "CoreSmoke must still run 66G6.");
        RequireContains(coreSmokeText, "Phase66G7CheckoutLocalHostLookupSmokeVerificationChecks.Run();", "CoreSmoke must run 66G7.");

        int index66G6 = coreSmokeText.IndexOf("Phase66G6CheckoutLocalHostProductSearchWiringChecks.Run();", StringComparison.Ordinal);
        int index66G7 = coreSmokeText.IndexOf("Phase66G7CheckoutLocalHostLookupSmokeVerificationChecks.Run();", StringComparison.Ordinal);

        if (index66G6 < 0 || index66G7 < 0 || index66G7 < index66G6)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. Phase 66G7 must run after Phase 66G6.");
        }
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
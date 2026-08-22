namespace Salryn.Tests;

public static class Phase66G8CheckoutLocalHostDatabaseLookupSmokeVerificationChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string programText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CheckoutLocalHostSmoke", "Program.cs");
        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string adapterText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "CompositionLocalHostProductLookupGatewayAdapter.cs");
        string requestProviderText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostRequestContextProvider.cs");
        string sessionProviderText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostSessionIdentityProvider.cs");
        string boundaryText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Checkout", "CheckoutLocalHostProductSearchBoundary.cs");
        string handoffText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_66G8_CHECKOUT_LOCAL_HOST_DATABASE_LOOKUP_SMOKE.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(programText, "CheckoutLocalHostDatabaseLookupSmokeChecks.RunAsync", "66G8 smoke tool must expose database mode.");
        RequireContains(programText, "SalrynAppServices.CreateDevelopment(tempRoot)", "66G8 smoke must use isolated development AppServices.");
        RequireContains(programText, "services.DatabaseInitialization.InitializeDatabase()", "66G8 smoke must initialize an isolated database.");
        RequireContains(programText, "services.ProductWriteRepository.AddAsync(product)", "66G8 smoke must seed a controlled product through repository writes.");
        RequireContains(programText, "services.LocalHostSessionIdentityProvider.SetSession", "66G8 smoke must set passive app session identity.");
        RequireContains(programText, "new CheckoutPresenter(services.CheckoutProductSearch)", "66G8 smoke must exercise CheckoutPresenter through AppServices checkout boundary.");
        RequireContains(programText, "await presenter.SearchProductAsync($\"  {barcode}  \")", "66G8 smoke must search a database-backed barcode.");
        RequireContains(programText, "presenter.AddPreparedProductLine()", "66G8 smoke must add the database-backed prepared product.");
        RequireContains(programText, "TryDeleteTempRoot(tempRoot)", "66G8 smoke must attempt temporary database cleanup.");

        RequireContains(appServicesText, "LocalHostRuntimeStartupResult", "66G8 depends on AppServices Local Host runtime startup.");
        RequireContains(appServicesText, "new CompositionLocalHostProductLookupGatewayAdapter(LocalHostProductLookupCompositionHolder, LocalHostRequestContextProvider, LocalHostSessionIdentityProvider)", "66G8 requires provider-aware adapter wiring.");
        RequireContains(appServicesText, "new CheckoutLocalHostProductSearchBoundary(LocalHostProductLookupGateway)", "66G8 requires checkout boundary wiring through AppServices.");
        RequireContains(adapterText, "holder.Composition.Runtime.DispatchAsync<LocalHostProductLookupRequest, LocalHostProductLookupResponse>", "66G8 requires real holder dispatch.");
        RequireContains(adapterText, "sessionProvider.TryGetAuthenticatedIdentity", "66G8 requires passive session identity consumption.");
        RequireContains(requestProviderText, "TryCreateAuthenticatedContext", "66G8 requires authenticated request context creation.");
        RequireContains(sessionProviderText, "TryGetAuthenticatedIdentity", "66G8 requires session identity provider.");
        RequireContains(boundaryText, "TryLookupByBarcodeAsync", "66G8 requires checkout boundary to call the Local Host gateway.");
        RequireContains(boundaryText, "ProductLookupReadModel", "66G8 requires checkout boundary product mapping.");

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
            RequireNotContains(programText + appServicesText + adapterText + boundaryText, forbidden, $"66G8 must not add transport marker {forbidden}.");
        }

        RequireContains(handoffText, "Status: Real local database-backed checkout lookup smoke.", "66G8 handoff must mark database-backed smoke scope.");
        RequireContains(handoffText, "No transport was added.", "66G8 handoff must block transport.");
        RequireContains(handoffText, "No LAN behavior was added.", "66G8 handoff must block LAN.");
        RequireContains(handoffText, "No Shell wiring was changed.", "66G8 handoff must preserve Shell wiring.");

        RequireContains(coreSmokeText, "Phase66G7CheckoutLocalHostLookupSmokeVerificationChecks.Run();", "CoreSmoke must still run 66G7.");
        RequireContains(coreSmokeText, "Phase66G8CheckoutLocalHostDatabaseLookupSmokeVerificationChecks.Run();", "CoreSmoke must run 66G8.");

        int index66G7 = coreSmokeText.IndexOf("Phase66G7CheckoutLocalHostLookupSmokeVerificationChecks.Run();", StringComparison.Ordinal);
        int index66G8 = coreSmokeText.IndexOf("Phase66G8CheckoutLocalHostDatabaseLookupSmokeVerificationChecks.Run();", StringComparison.Ordinal);

        if (index66G7 < 0 || index66G8 < 0 || index66G8 < index66G7)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. Phase 66G8 must run after Phase 66G7.");
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

namespace Salryn.Tests;

public static class Phase66G6CheckoutLocalHostProductSearchWiringChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string shellText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string checkoutPresenterText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Checkout", "CheckoutPresenter.cs");
        string boundaryText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Checkout", "CheckoutLocalHostProductSearchBoundary.cs");
        string handoffText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_66G6_CHECKOUT_LOCAL_HOST_PRODUCT_SEARCH_WIRING.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(appServicesText, "public ICheckoutProductSearchBoundary CheckoutProductSearch { get; }", "AppServices must still expose the checkout product search boundary.");
        RequireContains(appServicesText, "LocalHostProductLookupGateway =", "AppServices must still create the Local Host ProductLookup gateway.");
        RequireContains(appServicesText, "new CompositionLocalHostProductLookupGatewayAdapter(LocalHostProductLookupCompositionHolder, LocalHostRequestContextProvider, LocalHostSessionIdentityProvider)", "AppServices must keep the holder-aware ProductLookup gateway.");
        RequireContains(appServicesText, "CheckoutProductSearch =", "66G6 must assign CheckoutProductSearch through AppServices.");
        RequireContains(appServicesText, "new CheckoutLocalHostProductSearchBoundary(LocalHostProductLookupGateway)", "66G6 must wire checkout product search to the Local Host-backed boundary.");
        RequireNotContains(appServicesText, "CheckoutProductSearch = checkoutProductSearch;", "66G6 must remove old constructor-passed checkout search assignment.");
        RequireNotContains(appServicesText, "new CheckoutProductRepositorySearchBoundary(productReadRepository)", "66G6 must stop wiring checkout search directly to the repository boundary.");

        RequireContains(shellText, "_services.CheckoutProductSearch", "Shell must continue passing the AppServices checkout search boundary.");
        RequireNotContains(shellText, "new CheckoutLocalHostProductSearchBoundary", "Shell must not instantiate the Local Host checkout boundary directly.");
        RequireNotContains(shellText, "_services.LocalHostProductLookupGateway", "Shell must not directly pass the Local Host gateway.");
        RequireContains(shellText, "new CheckoutPresenter(", "Shell must still open checkout through CheckoutPresenter.");

        RequireContains(checkoutPresenterText, "ICheckoutProductSearchBoundary", "CheckoutPresenter must remain boundary-based.");
        RequireNotContains(checkoutPresenterText, "IAppLocalHostProductLookupGateway", "CheckoutPresenter must not know the Local Host gateway.");
        RequireNotContains(checkoutPresenterText, "CheckoutLocalHostProductSearchBoundary", "CheckoutPresenter must not know the Local Host adapter class.");

        RequireContains(boundaryText, "public sealed class CheckoutLocalHostProductSearchBoundary : ICheckoutProductSearchBoundary", "66G5 boundary must remain.");
        RequireContains(boundaryText, "_localHostProductLookupGateway.TryLookupByBarcodeAsync", "Boundary must still call the Local Host gateway.");
        RequireContains(boundaryText, "new ProductLookupReadModel", "Boundary must still map to ProductLookupReadModel.");

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
            RequireNotContains(appServicesText + shellText + checkoutPresenterText + boundaryText, forbidden, $"66G6 must not add transport marker {forbidden}.");
        }

        RequireNotContains(shellText, "DispatchAsync", "Shell must not dispatch directly.");
        RequireNotContains(checkoutPresenterText, "DispatchAsync", "CheckoutPresenter must not dispatch directly.");
        RequireNotContains(appServicesText, "TryLookupByBarcodeAsync", "AppServices must not perform lookup calls directly; only wiring is allowed.");

        RequireContains(handoffText, "Status: Checkout product search now uses the Local Host-backed boundary through AppServices.", "66G6 handoff must mark AppServices wiring.");
        RequireContains(handoffText, "Shell still passes CheckoutProductSearch.", "66G6 handoff must preserve Shell route shape.");
        RequireContains(handoffText, "No transport was added.", "66G6 handoff must block transport.");

        RequireContains(coreSmokeText, "Phase66G5CheckoutLocalHostProductSearchBoundaryChecks.Run();", "CoreSmoke must still run 66G5.");
        RequireContains(coreSmokeText, "Phase66G6CheckoutLocalHostProductSearchWiringChecks.Run();", "CoreSmoke must run 66G6.");

        int index66G5 = coreSmokeText.IndexOf("Phase66G5CheckoutLocalHostProductSearchBoundaryChecks.Run();", StringComparison.Ordinal);
        int index66G6 = coreSmokeText.IndexOf("Phase66G6CheckoutLocalHostProductSearchWiringChecks.Run();", StringComparison.Ordinal);

        if (index66G5 < 0 || index66G6 < 0 || index66G6 < index66G5)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. Phase 66G6 must run after Phase 66G5.");
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
namespace Salryn.Tests;

public static class Phase66G5CheckoutLocalHostProductSearchBoundaryChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string boundaryText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Checkout", "CheckoutLocalHostProductSearchBoundary.cs");
        string shellText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string handoffText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_66G5_CHECKOUT_LOCAL_HOST_PRODUCT_SEARCH_BOUNDARY.md");

        RequireContains(boundaryText, "public sealed class CheckoutLocalHostProductSearchBoundary : ICheckoutProductSearchBoundary", "66G5 must add checkout-facing boundary implementation.");
        RequireContains(boundaryText, "IAppLocalHostProductLookupGateway localHostProductLookupGateway", "66G5 boundary must depend on app Local Host gateway.");
        RequireContains(boundaryText, "_localHostProductLookupGateway.TryLookupByBarcodeAsync", "66G5 boundary must call Local Host ProductLookup gateway.");
        RequireContains(boundaryText, "new AppLocalHostProductLookupRequest(trimmed)", "66G5 boundary must create Local Host lookup request from trimmed input.");
        RequireContains(boundaryText, "Result<ProductLookupReadModel?>.Success(null)", "66G5 boundary must preserve null result for blank or not-found lookup.");
        RequireContains(boundaryText, "new ProductLookupReadModel", "66G5 boundary must map to checkout ProductLookupReadModel.");
        RequireContains(boundaryText, "Id: product.ProductId", "66G5 boundary must map product id.");
        RequireContains(boundaryText, "Sku: product.Sku", "66G5 boundary must map SKU.");
        RequireContains(boundaryText, "Barcode: product.Barcode", "66G5 boundary must map barcode.");
        RequireContains(boundaryText, "Name: product.DisplayName", "66G5 boundary must map display name to checkout name.");
        RequireContains(boundaryText, "UnitName: product.UnitName", "66G5 boundary must map unit name.");
        RequireContains(boundaryText, "SellingPrice: Money.FromCents(product.SellingPriceCents)", "66G5 boundary must map selling price cents.");
        RequireContains(boundaryText, "Money.FromCents(product.CostPriceCents.Value)", "66G5 boundary must map optional cost price cents.");
        RequireContains(boundaryText, "Quantity.FromX1000(product.StockOnHandX1000)", "66G5 boundary must map stock quantity.");
        RequireContains(boundaryText, "TrackStock: product.TrackStock", "66G5 boundary must map track stock.");
        RequireContains(boundaryText, "AllowDiscount: product.AllowDiscount", "66G5 boundary must map allow discount.");
        RequireContains(boundaryText, "IsActive: product.IsActive", "66G5 boundary must map active state.");
        RequireContains(boundaryText, "MapFailure", "66G5 boundary must map gateway failures to Core errors.");

        RequireNotContains(boundaryText, "DispatchAsync", "66G5 boundary must not dispatch directly.");
        RequireNotContains(boundaryText, "Runtime.Start", "66G5 boundary must not start runtime.");
        RequireNotContains(boundaryText, ".Start(", "66G5 boundary must not start runtime.");
        RequireNotContains(boundaryText, "HttpClient", "66G5 boundary must not add HTTP.");
        RequireNotContains(boundaryText, "Socket", "66G5 boundary must not add sockets.");
        RequireNotContains(boundaryText, "Tcp", "66G5 boundary must not add TCP.");
        RequireNotContains(boundaryText, "Udp", "66G5 boundary must not add UDP.");
        RequireNotContains(boundaryText, "Kestrel", "66G5 boundary must not add server transport.");
        RequireNotContains(boundaryText, "SignalR", "66G5 boundary must not add SignalR.");

        RequireContains(shellText, "_services.CheckoutProductSearch", "66G5 must not change Shell checkout wiring yet.");
        RequireNotContains(shellText, "_services.LocalHostProductLookupGateway", "66G5 must not pass LocalHostProductLookupGateway into Shell UI yet.");
        RequireNotContains(shellText, "CheckoutLocalHostProductSearchBoundary", "66G5 must not instantiate the new boundary in Shell yet.");

        RequireContains(appServicesText, "public IAppLocalHostProductLookupGateway LocalHostProductLookupGateway { get; }", "AppServices must still expose Local Host gateway.");
        RequireContains(appServicesText, "new CheckoutLocalHostProductSearchBoundary(LocalHostProductLookupGateway)", "66G6 wires the checkout boundary through AppServices.");

        RequireContains(handoffText, "Status: Checkout-facing Local Host product search boundary only.", "66G5 handoff must mark boundary-only scope.");
        RequireContains(handoffText, "No Shell checkout wiring was changed.", "66G5 handoff must block Shell wiring.");
        RequireContains(handoffText, "No transport was added.", "66G5 handoff must block transport.");

        RequireContains(coreSmokeText, "Phase66G5CheckoutLocalHostProductSearchBoundaryChecks.Run();", "CoreSmoke must run 66G5.");
        RequireContains(coreSmokeText, "Phase66G4LocalHostProductLookupCheckoutReadyPayloadChecks.Run();", "CoreSmoke must still run 66G4.");

        int index66G4 = coreSmokeText.IndexOf("Phase66G4LocalHostProductLookupCheckoutReadyPayloadChecks.Run();", StringComparison.Ordinal);
        int index66G5 = coreSmokeText.IndexOf("Phase66G5CheckoutLocalHostProductSearchBoundaryChecks.Run();", StringComparison.Ordinal);

        if (index66G4 < 0 || index66G5 < 0 || index66G5 < index66G4)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. Phase 66G5 must run after Phase 66G4.");
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
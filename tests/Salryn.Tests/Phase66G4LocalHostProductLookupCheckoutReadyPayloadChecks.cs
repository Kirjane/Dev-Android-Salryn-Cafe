namespace Salryn.Tests;

public static class Phase66G4LocalHostProductLookupCheckoutReadyPayloadChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string hostContractsText = ReadRequiredFile(repoRoot, "src", "Salryn.Host.Local", "Contracts", "LocalHostOperationContractModels.cs");
        string handlerText = ReadRequiredFile(repoRoot, "src", "Salryn.Host.Local", "Handlers", "Products", "LocalHostProductLookupHandlerModels.cs");
        string appGatewayContractsText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "AppLocalHostProductLookupGatewayContracts.cs");
        string adapterText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "CompositionLocalHostProductLookupGatewayAdapter.cs");
        string shellText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string checkoutPresenterText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Checkout", "CheckoutPresenter.cs");
        string handoffText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_66G4_LOCAL_HOST_PRODUCTLOOKUP_CHECKOUT_READY_PAYLOAD.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        foreach (string requiredField in new[]
        {
            "Guid ProductId",
            "string? Sku",
            "string? Barcode",
            "string DisplayName",
            "string UnitName",
            "long SellingPriceCents",
            "long? CostPriceCents",
            "long StockOnHandX1000",
            "bool TrackStock",
            "bool AllowDiscount",
            "bool IsActive"
        })
        {
            RequireContains(hostContractsText, requiredField, $"Host.Local ProductLookup response must include {requiredField}.");
            RequireContains(appGatewayContractsText, requiredField, $"App ProductLookup product result must include {requiredField}.");
        }

        foreach (string handlerMapping in new[]
        {
            "ProductId: product.Id",
            "Sku: product.Sku",
            "Barcode: product.Barcode",
            "DisplayName: product.Name",
            "UnitName: product.UnitName",
            "SellingPriceCents: product.SellingPrice.Cents",
            "CostPriceCents: product.CostPrice?.Cents",
            "StockOnHandX1000: product.StockOnHand.ValueX1000",
            "TrackStock: product.TrackStock",
            "AllowDiscount: product.AllowDiscount",
            "IsActive: product.IsActive"
        })
        {
            RequireContains(handlerText, handlerMapping, $"Handler must map {handlerMapping}.");
        }

        foreach (string adapterMapping in new[]
        {
            "productId: response.Payload.ProductId",
            "sku: response.Payload.Sku",
            "barcode: response.Payload.Barcode",
            "displayName: response.Payload.DisplayName",
            "unitName: response.Payload.UnitName",
            "sellingPriceCents: response.Payload.SellingPriceCents",
            "costPriceCents: response.Payload.CostPriceCents",
            "stockOnHandX1000: response.Payload.StockOnHandX1000",
            "trackStock: response.Payload.TrackStock",
            "allowDiscount: response.Payload.AllowDiscount",
            "isActive: response.Payload.IsActive"
        })
        {
            RequireContains(adapterText, adapterMapping, $"Adapter must map {adapterMapping}.");
        }

        RequireContains(adapterText, "holder.Composition.Runtime.DispatchAsync<LocalHostProductLookupRequest, LocalHostProductLookupResponse>", "Adapter must remain Local Host dispatch based.");
        RequireContains(adapterText, "AppLocalHostProductLookupResult.NotFound(barcode)", "Adapter must still preserve not-found mapping.");
        RequireContains(adapterText, "AppLocalHostProductLookupResult.LocalHostUnavailable", "Adapter must still preserve unavailable mapping.");

        RequireContains(shellText, "_services.CheckoutProductSearch", "66G4 must not wire checkout to Local Host yet.");
        RequireNotContains(shellText, "_services.LocalHostProductLookupGateway", "66G4 must not pass LocalHostProductLookupGateway into Shell UI yet.");
        RequireContains(checkoutPresenterText, "ICheckoutProductSearchBoundary", "66G4 must not change CheckoutPresenter yet.");

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
            RequireNotContains(hostContractsText + handlerText + appGatewayContractsText + adapterText + shellText, forbidden, $"66G4 must not add transport marker {forbidden}.");
        }

        RequireContains(handoffText, "Status: Local Host ProductLookup checkout-ready payload only.", "66G4 handoff must mark payload-only scope.");
        RequireContains(handoffText, "No Shell checkout wiring was changed.", "66G4 handoff must block Shell wiring.");
        RequireContains(handoffText, "No transport was added.", "66G4 handoff must block transport.");

        RequireContains(coreSmokeText, "Phase66G4LocalHostProductLookupCheckoutReadyPayloadChecks.Run();", "CoreSmoke must run 66G4.");
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
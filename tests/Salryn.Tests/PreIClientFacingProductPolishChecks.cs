namespace Salryn.Tests;

/// <summary>
/// PRE-I structural proof for client-facing product polish. This validates source contracts,
/// not browser layout, runtime notifications, printer hardware, or compliance readiness.
/// </summary>
public static class PreIClientFacingProductPolishChecks
{
    public static void Run()
    {
        string root = FindRoot();
        string license = Read(root, "src", "Salryn.App.Web", "src", "pages", "LicensePage.tsx");
        string credits = Read(root, "src", "Salryn.App.Web", "src", "components", "ProductCreditsCard.tsx");
        string products = Read(root, "src", "Salryn.App.Web", "src", "pages", "ProductsPage.tsx");
        string inventory = Read(root, "src", "Salryn.App.Web", "src", "pages", "InventoryPage.tsx");
        string audit = Read(root, "src", "Salryn.App.Web", "src", "pages", "AuditPage.tsx");
        string hostLan = Read(root, "src", "Salryn.App.Web", "src", "pages", "HostLanControlPage.tsx");
        string report = Read(root, "00-HANDOFF", "PRE_I_CLIENT_FACING_PRODUCT_POLISH_REPORT.md");

        AssertCreditsAndPositioning(license, credits);
        AssertTerms(license);
        AssertProductAndStockClarity(products, inventory);
        AssertProductProfile(products);
        AssertAuditPositioning(audit, report);
        AssertTerminalAddressClarity(hostLan);
    }

    private static void AssertCreditsAndPositioning(string license, string credits)
    {
        string joined = license + "\n" + credits;
        foreach (string required in new[]
        {
            "Licensing &amp; Credits",
            "Products of Kirjane Labs: Salryn",
            "Salryn is an offline-first POS and ledger console for store operations.",
            "Built by Kirjane Labs.",
            "Paul Gamayot",
            "Kirch Ivan Balite",
            "Jesse Edwin Culas",
            "James Karl Tara",
            "Osiris Kedigadash Palac",
            "DevOps Lead",
            "© 2026 Kirjane Labs. All rights reserved.",
        }) RequireContains(joined, required, "Credits or positioning regression.");

        RequireNotContains(joined, "Repository-backed beta build", "Repository language must not be client-facing.");
    }

    private static void AssertTerms(string license)
    {
        foreach (string required in new[]
        {
            "Terms & Conditions",
            "1. Product use",
            "2. Local license",
            "3. Device binding",
            "4. Data ownership",
            "5. Backup responsibility",
            "6. Hardware limits",
            "7. Receipts and compliance",
            "8. LAN terminals",
            "9. Account and operator responsibility",
            "10. Support boundaries",
            "11. Beta limitations",
            "12. Rights reservation",
            "13. Acceptance",
            "BIR accreditation: current beta status: not included",
            "eSales transmission: current beta status: not included",
            "cloud activation is not required",
            "Windows print queue internal receipts are available only when configured and verified",
        }) RequireContains(license, required, "Terms contract regression.");

        RequireNotContains(license, "does not claim", "Terms must use current-status language.");
        RequireNotContains(license, "BIR-ready", "No BIR readiness claim.");
        RequireNotContains(license, "ESC/POS supported", "No ESC/POS support claim.");
    }

    private static void AssertProductAndStockClarity(string products, string inventory)
    {
        RequireContains(products, "Create and manage product identity.", "Catalog purpose copy.");
        RequireContains(inventory, "Change product quantity through a ledger entry.", "Stock purpose copy.");
        foreach (string source in new[] { products, inventory })
        {
            RequireContains(source, "Product Catalog defines the product. Stock Movements changes the quantity.", "Catalog/movement separation.");
        }
        RequireContains(products, "Barcode identifies the sellable product/package.", "Barcode identity guidance.");
        RequireContains(products, "Different package sizes or bundles should be", "Package record guidance.");
        RequireContains(products, "500 ml and 1.5 L", "Package-size example.");
        RequireContains(products, "6-pack", "Bundle example.");
        RequireContains(products, "future product lane", "Future packaging boundary.");
        RequireContains(inventory, "Scan or enter barcode/SKU", "Stock lookup label.");
        RequireContains(inventory, "Scan or enter the product barcode/SKU to select the product.", "Stock lookup helper.");
    }

    private static void AssertProductProfile(string products)
    {
        RequireContains(products, "Selected product", "Product profile exists.");
        RequireContains(products, "Current stock", "Current stock priority.");
        RequireContains(products, "Selling price", "Selling price priority.");
        RequireContains(products, "SKU / internal code", "SKU label clarity.");
        RequireContains(products, "More details", "Secondary details disclosure.");
        RequireContains(products, "minmax(22rem,0.8fr)", "Desktop product profile width guard.");
        RequireOrder(products, "Current stock", "More details", "Core product facts must precede secondary details.");
    }

    private static void AssertAuditPositioning(string audit, string report)
    {
        RequireContains(audit, "Audit trail records committed operator and system actions", "Audit positioning.");
        RequireContains(audit, "Download audit CSV", "Audit page must expose server-side CSV export.");
        RequireContains(audit, "current filtered audit trail, not only the visible page", "Export helper must state filtered completeness.");
        RequireContains(report, "Logged now", "Audit coverage table missing Logged now.");
        RequireContains(report, "Missing", "Audit coverage table missing Missing.");
        RequireContains(report, "Recommended next", "Audit coverage table missing recommendation.");
    }

    private static void AssertTerminalAddressClarity(string hostLan)
    {
        RequireContains(hostLan, "detectedLanListenIps", "Runtime-detected addresses must be used.");
        RequireContains(hostLan, "Recommended Host PC address", "Recommended address label.");
        RequireContains(hostLan, "Detected addresses", "Multiple-address chooser.");
        RequireContains(hostLan, "Use this terminal link on other devices:", "Terminal link instruction.");
        RequireContains(hostLan, "Ask support to check this Host PC’s network address.", "No-address fallback.");
        RequireContains(hostLan, "showTroubleshooting", "Advanced diagnostics remain collapsed.");
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

    private static string FindRoot()
    {
        DirectoryInfo? current = new(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln"))) return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException("Could not locate Salryn.sln.");
    }

    private static void RequireContains(string source, string value, string message)
    {
        if (!source.Contains(value, StringComparison.Ordinal))
            throw new InvalidOperationException(message + " Missing: " + value);
    }

    private static void RequireNotContains(string source, string value, string message)
    {
        if (source.Contains(value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(message + " Forbidden: " + value);
    }

    private static void RequireOrder(string source, string first, string second, string message)
    {
        int firstIndex = source.IndexOf(first, StringComparison.Ordinal);
        int secondIndex = source.IndexOf(second, StringComparison.Ordinal);
        if (firstIndex < 0 || secondIndex <= firstIndex)
            throw new InvalidOperationException(message + $" Expected {first} before {second}.");
    }
}

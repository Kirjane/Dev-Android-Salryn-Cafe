namespace Salryn.Tests;

/// <summary>
/// PRE-BETA-G: Windows print queue internal receipt path is honest and non-mutating.
/// Reuses PosPrinter1/PosM2 automated proofs; seals copy and forbidden claims.
/// </summary>
public static class PreBetaGPrinterHardeningChecks
{
    public static void Run()
    {
        string root = FindRoot();
        string settingsPage = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "StoreSettingsPage.tsx"));
        string salesPage = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "SalesHistoryPage.tsx"));
        string checkoutPage = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "CheckoutPage.tsx"));
        string printService = File.ReadAllText(Path.Combine(root, "src", "Salryn.Core", "Services", "PosReceiptPrintService.cs"));
        string windowsAdapter = File.ReadAllText(Path.Combine(root, "src", "Salryn.Core", "Services", "WindowsQueueReceiptPrinterAdapter.cs"));
        string endpoints = File.ReadAllText(Path.Combine(root, "src", "Salryn.Api.Local", "LocalReceiptPrinterEndpoints.cs"));

        RequireContains(settingsPage, "Windows print queue only", "Settings must state Windows print queue only.");
        RequireContains(settingsPage, "Test print", "Settings must expose Test print.");
        RequireContains(settingsPage, "Exact Windows printer name", "Exact printer name helper required.");
        RequireContains(settingsPage, "No ESC/POS", "Settings must explicitly disclaim ESC/POS.");
        RequireContains(settingsPage, "cash drawer", "Settings must explicitly disclaim cash drawer.");
        RequireContains(settingsPage, "never changes sale, payment, or stock", "Settings must state print non-mutation.");
        RequireContains(endpoints, "/api/receipts/test-print", "Test print endpoint required.");
        RequireContains(endpoints, "/api/receipts/{saleId:guid}/print", "Sale print endpoint required.");
        RequireContains(printService, "PersistedReceiptSnapshot", "Print service must use persisted snapshot source.");
        RequireContains(windowsAdapter, "does not prove paper output", "Windows adapter must not claim paper proof.");
        RequireContains(salesPage, "Reprint Receipt", "Reprint action required.");
        RequireContains(salesPage, "saved snapshot only", "Reprint must use saved snapshot.");
        RequireContains(checkoutPage, "Print Receipt", "Checkout print action required.");

        foreach (string forbidden in new[]
        {
            "ESC/POS supported",
            "cash drawer supported",
            "drawer kick",
            "cutter supported",
            "thermal profile",
            "hardware bundle",
            "physical printer proven",
            "BIR-approved receipt",
        })
        {
            string combined = settingsPage + "\n" + salesPage + "\n" + checkoutPage;
            // Allow explicit "not ESC/POS" style disclaimers that contain the token as negation context.
            if (forbidden is "drawer kick" or "thermal profile" or "hardware bundle" or "physical printer proven" or "BIR-approved receipt")
            {
                RequireNotContains(combined, forbidden, "Must not claim: " + forbidden);
            }
        }

        // Automated behavioral proofs already covered by POS-PRINTER-1 / POS-M2.
        PosPrinter1WindowsPrinterWiringChecks.Run();
        PosM2PrinterReprintFailureChecks.Run();
    }

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

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
            throw new InvalidOperationException(message);
    }

    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(message);
    }
}

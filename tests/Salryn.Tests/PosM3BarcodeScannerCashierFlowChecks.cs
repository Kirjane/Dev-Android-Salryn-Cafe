namespace Salryn.Tests;

public static class PosM3BarcodeScannerCashierFlowChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();
        string webSrc = Path.Combine(repoRoot.FullName, "src", "Salryn.App.Web", "src");
        string checkoutPage = ReadRequiredFile(webSrc, "pages", "CheckoutPage.tsx");
        string productsApi = ReadRequiredFile(webSrc, "api", "products.ts");
        string receiptPrintApi = ReadRequiredFile(webSrc, "api", "receiptPrint.ts");
        string coreSmoke = ReadRequiredFile(
            Path.Combine(repoRoot.FullName, "tools", "Salryn.Tools.CoreSmoke"),
            "Program.cs");

        RequireContains(coreSmoke, "PosM3BarcodeScannerCashierFlowChecks.Run();", "CoreSmoke must run POS-M3 barcode cashier flow checks.");
        RequireContains(productsApi, "barcode: string | null;", "Product list item must expose catalog barcode for checkout lookup.");

        RequireContains(checkoutPage, "function normalizeBarcode", "Checkout must normalize barcode input before lookup.");
        RequireContains(checkoutPage, "function exactBarcodeMatch", "Checkout must isolate exact barcode matching.");
        RequireContains(checkoutPage, "normalizeBarcode(product.barcode) === normalized", "Barcode add must require an exact existing catalog barcode match.");
        RequireContains(checkoutPage, "fetchProducts({ search: barcode, isActive: true", "Barcode enter must use existing product catalog GET lookup.");
        RequireContains(checkoutPage, "pageSize: 50", "Barcode lookup must fetch enough active catalog candidates for exact matching.");
        RequireContains(checkoutPage, "event.key === \"Enter\"", "Keyboard-wedge Enter suffix must trigger barcode add attempt.");
        RequireContains(checkoutPage, "event.key === \"Tab\"", "Keyboard-wedge Tab suffix must trigger barcode add attempt.");
        RequireContains(checkoutPage, "void handleBarcodeSubmit();", "Scan/search input Enter/Tab must call barcode add flow.");
        RequireContains(checkoutPage, "addProduct(match)", "Exact barcode match must reuse existing cart add path.");
        RequireContains(checkoutPage, "Scan barcode or search product name", "Scan/search input must remain visible and product-name search usable.");
        RequireContains(checkoutPage, "Product-name search remains available", "Product-name search regression proof must remain in UI copy.");
        RequireContains(checkoutPage, "openFastAddForBarcode(barcode)", "Unknown barcode must hand off to POS-M4 Fast Add flow.");
        RequireContains(checkoutPage, "Stock changes only when checkout completes", "Scan add must not claim stock mutation before checkout.");

        RequireContains(checkoutPage, "submitCheckout", "Checkout submit path must remain available after barcode cart add.");
        RequireContains(checkoutPage, "fileReceiptStatus", "POS-M1 receipt file status must remain wired in checkout UI.");
        RequireContains(checkoutPage, "printReceiptForSale", "POS-M2 print path must remain wired for Sale Completed modal.");
        RequireContains(receiptPrintApi, "method: \"POST\"", "POS-M2 print client must remain isolated outside read-only sales API.");

        RequireNotContains(checkoutPage, "createProduct", "Barcode flow must not silently create products.");
        RequireNotContains(checkoutPage, "method: \"POST\"", "Checkout page must not introduce direct product writes.");
        RequireNotContains(checkoutPage, "StockIn", "Barcode flow must not add direct stock-in behavior inside checkout UI.");
        RequireNotContains(checkoutPage, "Adjustment", "Barcode flow must not add stock adjustment behavior.");
        RequireNotContains(checkoutPage, "Void Sale", "Barcode flow must not add void behavior.");
        RequireNotContains(checkoutPage, "Official", "Barcode flow must not add official/RMO/BIR behavior.");
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

    private static string ReadRequiredFile(string root, params string[] pathParts)
    {
        string path = root;
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

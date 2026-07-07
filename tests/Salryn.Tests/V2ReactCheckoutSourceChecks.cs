namespace Salryn.Tests;

public static class V2ReactCheckoutSourceChecks
{
    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string webRoot = Path.Combine(projectRoot, "src", "Salryn.App.Web", "src");
        string apiRoot = Path.Combine(projectRoot, "src", "Salryn.Api.Local");

        string checkoutPage = ReadRequiredFile(Path.Combine(webRoot, "pages", "CheckoutPage.tsx"));
        string checkoutApi = ReadRequiredFile(Path.Combine(webRoot, "api", "checkout.ts"));
        string httpApi = ReadRequiredFile(Path.Combine(webRoot, "api", "http.ts"));
        string navigation = ReadRequiredFile(Path.Combine(webRoot, "navigation.ts"));
        string app = ReadRequiredFile(Path.Combine(webRoot, "App.tsx"));
        string permissions = ReadRequiredFile(Path.Combine(webRoot, "security", "permissions.ts"));
        string remotePosPage = ReadRequiredFile(Path.Combine(webRoot, "pages", "RemotePosClientPage.tsx"));
        string remoteReadOnlyApi = ReadRequiredFile(Path.Combine(webRoot, "api", "remoteReadOnlyClient.ts"));
        string coreSmoke = ReadRequiredFile(Path.Combine(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs"));

        string checkoutEndpoints = ReadRequiredFile(Path.Combine(apiRoot, "LocalHostCheckoutEndpoints.cs"));
        string remoteReadOnlyEndpoints = ReadRequiredFile(Path.Combine(apiRoot, "RemoteReadOnlyApiEndpoints.cs"));
        string hostBootstrap = ReadRequiredFile(Path.Combine(
            projectRoot,
            "src",
            "Salryn.Host.Local",
            "Bootstrap",
            "LocalHostHandlerBootstrapModels.cs"));

        AssertContains(checkoutPage, "submitCheckout", "Checkout page must call submitCheckout.");
        AssertContains(checkoutApi, "\"/api/checkout\"", "submitCheckout must POST /api/checkout.");
        AssertContains(checkoutApi, "\"/api/shift\"", "Checkout API must read shift state.");
        AssertContains(checkoutApi, "\"/api/shift/open\"", "Checkout API must open shift.");
        AssertContains(checkoutApi, "\"/api/shift/close\"", "Checkout API must close shift.");

        AssertContains(checkoutPage, "Session expired or login required.", "Checkout page must surface 401 errors.");
        AssertContains(checkoutPage, "Your role cannot perform checkout.", "Checkout page must surface 403 errors.");
        AssertContains(checkoutPage, "Open a shift before checkout.", "Checkout page must surface open-shift errors.");
        AssertContains(checkoutPage, "Stock changed or is insufficient. Refresh products before retrying.", "Checkout page must surface stock-changed errors.");
        AssertContains(checkoutPage, "Invalid cart or payment", "Checkout page must surface 400 errors.");
        AssertContains(checkoutPage, "missing or inactive", "Checkout page must surface 404 errors.");
        AssertContains(checkoutPage, "Sales.NegativeStockNotAllowed", "Checkout page must recognize negative-stock backend code.");
        AssertContains(checkoutPage, "StockChanged", "Checkout page must recognize StockChanged checkout status.");

        AssertContains(checkoutPage, "isSubmittingCheckout", "Checkout page must lock submit while in progress.");
        AssertContains(checkoutPage, "Checkout readiness", "Checkout page must keep readiness panel.");
        AssertContains(checkoutPage, "checkout receipt number", "Checkout page must label receipt as non-official checkout reference.");
        AssertNotContains(checkoutPage, "official receipt", "Checkout page must not claim official receipt output.");
        AssertNotContains(checkoutPage, "mock checkout", "Checkout page must not use mock checkout.");
        AssertNotContains(checkoutApi, "fake", "Checkout API must not fake responses.");

        AssertContains(httpApi, "checkoutStatus", "HTTP client must preserve checkout.status from backend errors.");
        AssertContains(httpApi, "checkoutMessage", "HTTP client must preserve checkout.message from backend errors.");

        AssertContains(navigation, "id: \"checkout\", label: \"POS Checkout\", enabled: true", "Checkout nav must remain enabled.");
        AssertContains(permissions, "Sales.Create", "Checkout permission must require Sales.Create.");

        AssertContains(app, "isRemoteClientBrowserLocation", "App must separate remote client entry from host console.");
        AssertContains(app, "RemoteClientApp", "Remote client flow must use dedicated remote shell.");
        AssertRemoteClientDoesNotExposeHostCheckout(app);

        AssertNotContains(remotePosPage, "\"/api/checkout\"", "Remote POS page must not call host /api/checkout.");
        AssertContains(remoteReadOnlyApi, "disabled", "Remote read-only client must keep checkout disabled.");

        AssertContains(checkoutEndpoints, "LocalApiPermissions.SalesCreate", "Host checkout endpoint must require Sales.Create.");
        AssertNotContains(remoteReadOnlyEndpoints, "MapPost(\"/api/checkout\"", "Remote read-only API must not expose host checkout mutation.");
        AssertContains(hostBootstrap, "LocalHostBootstrapNotImplementedHandler", "Placeholder Local Host handlers remain quarantined.");

        AssertContains(coreSmoke, "v2-react-checkout", "CoreSmoke must expose v2-react-checkout runner.");
    }

    private static void AssertRemoteClientDoesNotExposeHostCheckout(string appSource)
    {
        const string remoteMarker = "function RemoteClientApp()";
        const string consoleMarker = "function ConsoleApp()";

        int remoteStart = appSource.IndexOf(remoteMarker, StringComparison.Ordinal);
        int consoleStart = appSource.IndexOf(consoleMarker, StringComparison.Ordinal);

        if (remoteStart < 0 || consoleStart < 0 || consoleStart <= remoteStart)
        {
            throw new InvalidOperationException("Could not isolate RemoteClientApp for checkout boundary guard.");
        }

        string remoteSection = appSource.Substring(remoteStart, consoleStart - remoteStart);

        if (remoteSection.Contains("CheckoutPage", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Remote client shell must not expose host CheckoutPage.");
        }
    }

    private static string FindProjectRoot(string startPath)
    {
        DirectoryInfo? current = new(startPath);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not find Salryn.sln from " + startPath);
    }

    private static string ReadRequiredFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Required file not found: " + path);
        }

        return File.ReadAllText(path);
    }

    private static void AssertContains(string haystack, string needle, string message)
    {
        if (!haystack.Contains(needle, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertNotContains(string haystack, string needle, string message)
    {
        if (haystack.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message);
        }
    }
}
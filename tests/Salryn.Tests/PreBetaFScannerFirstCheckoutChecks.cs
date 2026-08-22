namespace Salryn.Tests;

/// <summary>
/// PRE-BETA-F: keyboard-wedge barcode checkout is first-class on Host and LAN terminal.
/// No hardware proof; no HID/Bluetooth claims.
/// </summary>
public static class PreBetaFScannerFirstCheckoutChecks
{
    public static void Run()
    {
        string root = FindRoot();
        string hostCheckout = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "CheckoutPage.tsx"));
        string remoteCheckout = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "RemotePosClientPage.tsx"));
        string remoteApi = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "api", "remotePosClient.ts"));
        string remoteEndpoints = File.ReadAllText(Path.Combine(root, "src", "Salryn.Api.Local", "RemotePos", "RemotePosApiEndpoints.cs"));
        string remoteService = File.ReadAllText(Path.Combine(root, "src", "Salryn.Api.Local", "RemotePos", "RemotePosCheckoutService.cs"));
        string authMatrix = File.ReadAllText(Path.Combine(root, "src", "Salryn.Api.Local", "RemotePos", "RemotePosAuthorizationMatrix.cs"));
        string textInput = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "components", "ui", "TextInput.tsx"));

        RequireContains(hostCheckout, "Scan / search", "Host checkout must expose Scan / search field.");
        RequireContains(hostCheckout, "handleBarcodeSubmit", "Host must submit barcode from scan field.");
        RequireContains(hostCheckout, "event.key === \"Enter\" || event.key === \"Tab\"", "Host must accept Enter and Tab suffix.");
        RequireContains(hostCheckout, "scanInputRef", "Host must refocus scan field after add.");
        RequireContains(hostCheckout, "exactBarcodeMatch", "Host must exact-match barcodes.");
        RequireContains(hostCheckout, "openFastAddForBarcode", "Host unknown barcode must use Fast Add flow.");
        RequireContains(hostCheckout, "keyboard-wedge", "Host copy must describe keyboard-wedge mode only.");
        RequireNotContains(hostCheckout, "navigator.usb", "Host must not use WebUSB.");
        RequireNotContains(hostCheckout, "navigator.hid", "Host must not use WebHID.");
        RequireNotContains(hostCheckout, "Bluetooth", "Host must not claim Bluetooth scanner support.");

        RequireContains(remoteCheckout, "Scan / search", "LAN checkout must expose Scan / search field.");
        RequireContains(remoteCheckout, "handleScanSubmit", "LAN must submit barcode from scan field.");
        RequireContains(remoteCheckout, "event.key === \"Enter\" || event.key === \"Tab\"", "LAN must accept Enter and Tab suffix.");
        RequireContains(remoteCheckout, "addLineByBarcode", "LAN must call barcode add path.");
        RequireContains(remoteCheckout, "Advanced: add by product ID", "Product GUID must not be the normal cashier path.");
        RequireContains(remoteApi, "add-line-by-barcode", "Remote client must call barcode endpoint.");
        RequireContains(remoteEndpoints, "/cart/add-line-by-barcode", "Remote API must expose barcode add endpoint.");
        RequireContains(remoteService, "AddLineByBarcodeAsync", "Remote service must implement barcode add.");
        RequireContains(remoteService, "FindByBarcodeAsync", "Remote service must look up barcode on host.");
        RequireContains(remoteService, "UnknownBarcode", "Unknown barcode must be a safe failure path.");
        RequireContains(authMatrix, "CartAddLineByBarcode", "Barcode add must be authorized as cart write.");
        RequireContains(textInput, "forwardRef", "TextInput must support ref for scan field focus.");

        // Forbidden overclaims in operator-facing checkout sources
        foreach (string token in new[] { "USB HID", "WebHID", "scanner SDK", "Bluetooth scanner", "device enumeration" })
        {
            RequireNotContains(hostCheckout, token, "Host checkout must not claim: " + token);
            RequireNotContains(remoteCheckout, token, "LAN checkout must not claim: " + token);
        }
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

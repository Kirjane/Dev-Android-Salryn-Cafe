namespace Salryn.Tests;

/// <summary>
/// HOST-LAN-CONTROL-M4: source guards for human-friendly Host LAN Control UX.
/// </summary>
public static class V2HostLanControlUxChecks
{
    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string webRoot = Path.Combine(projectRoot, "src", "Salryn.App.Web", "src");
        string apiRoot = Path.Combine(projectRoot, "src", "Salryn.Api.Local");

        string page = ReadRequiredFile(Path.Combine(webRoot, "pages", "HostLanControlPage.tsx"));
        string landing = ReadRequiredFile(Path.Combine(webRoot, "pages", "RemoteClientLandingPage.tsx"));
        string remotePosPage = ReadRequiredFile(Path.Combine(webRoot, "pages", "RemotePosClientPage.tsx"));
        string lifecycle = ReadRequiredFile(Path.Combine(webRoot, "utils", "terminalLifecycleUx.ts"));
        string storage = ReadRequiredFile(Path.Combine(webRoot, "utils", "lanClientStorage.ts"));
        string bindNormalize = ReadRequiredFile(Path.Combine(webRoot, "utils", "lanBindNormalize.ts"));
        string hostUrls = ReadRequiredFile(Path.Combine(apiRoot, "Services", "LocalApiHostControlUrls.cs"));
        string normalizer = ReadRequiredFile(Path.Combine(apiRoot, "Services", "LocalApiLanBindNormalizer.cs"));
        string endpoints = ReadRequiredFile(Path.Combine(apiRoot, "LocalLanControlEndpoints.cs"));
        string registry = ReadRequiredFile(Path.Combine(projectRoot, "src", "Salryn.Infrastructure", "Data", "Services", "TerminalRegistryService.cs"));
        string coreSmoke = ReadRequiredFile(Path.Combine(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs"));
        string app = ReadRequiredFile(Path.Combine(webRoot, "App.tsx"));

        AssertContains(page, "LAN Connection", "Host page must lead with LAN Connection section.");
        AssertContains(page, "Repair LAN address", "Host page must expose bind repair.");
        AssertContains(page, "Activate LAN connection", "Host page must expose activate action.");
        AssertContains(page, "Terminal help", "Internal Terminal Management must include terminal help.");
        AssertContains(page, "Connection behavior", "Internal Terminal Management must explain connection behavior.");
        AssertContains(page, "Password is never stored", "Connection behavior must say password is never stored.");
        AssertContains(page, "Revoked means re-pair", "Connection behavior must say revoked requires re-pair.");
        AssertContains(page, "canCopyClientUrl", "Copy client URL must be gated.");
        AssertNotContains(page, "http://http://", "Host page must not invent double-scheme URLs.");
        AssertNotContains(page, "0.0.0.0", "Host page must not display wildcard bind.");

        AssertContains(bindNormalize, "normalizeConfiguredLanBindInput", "Frontend bind normalizer required.");
        AssertContains(normalizer, "NormalizeConfiguredLanBindInput", "Backend bind normalizer required.");
        AssertContains(hostUrls, "LocalApiLanBindNormalizer", "BuildLanUrl must normalize bind IP.");
        AssertContains(endpoints, "/bind/repair", "Bind repair endpoint required.");
        AssertContains(endpoints, "/clear-sessions", "Clear-sessions endpoint required.");

        AssertContains(lifecycle, "canRestoreDisabledTerminal", "Restore gate required.");
        AssertContains(lifecycle, "return false", "Restore must stay disabled without backend support.");
        AssertNotContains(registry, "EnableTerminalAsync", "Registry must not fake re-enable.");
        AssertNotContains(registry, "DeleteTerminalAsync", "Registry must not hard-delete terminals without design.");

        AssertContains(landing, "Clear this device state", "Landing clear-state required.");
        AssertContains(storage, "clearAllLanClientDeviceState", "Storage full clear required.");
        AssertContains(remotePosPage, "Clear this device pairing and saved cashier session", "Remote POS clear pairing+session required.");
        AssertNotContains(storage, "password", "Storage must not include password.");

        AssertRemoteClientDoesNotExposeHostCheckout(app);
        AssertNotContains(endpoints, "0.0.0.0", "LAN control endpoints must not introduce wildcard bind.");
        AssertContains(coreSmoke, "v2-host-lan-control-ux", "CoreSmoke runner required.");
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

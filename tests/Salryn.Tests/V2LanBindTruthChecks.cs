namespace Salryn.Tests;

public static class V2LanBindTruthChecks
{
    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string apiRoot = Path.Combine(projectRoot, "src", "Salryn.Api.Local");
        string webRoot = Path.Combine(projectRoot, "src", "Salryn.App.Web", "src");

        string factory = ReadRequiredFile(Path.Combine(apiRoot, "LocalApiApplicationFactory.cs"));
        string resolver = ReadRequiredFile(Path.Combine(apiRoot, "Services", "LocalApiLanUrlResolver.cs"));
        string startupOptions = ReadRequiredFile(Path.Combine(apiRoot, "Services", "LocalApiLanBindStartupOptions.cs"));
        string networkValidator = ReadRequiredFile(Path.Combine(apiRoot, "Services", "LocalApiNetworkInterfaceValidator.cs"));
        string runtimeStatus = ReadRequiredFile(Path.Combine(apiRoot, "Services", "LocalApiLanRuntimeStatusService.cs"));
        string hostUrls = ReadRequiredFile(Path.Combine(apiRoot, "Services", "LocalApiHostControlUrls.cs"));
        string endpoints = ReadRequiredFile(Path.Combine(apiRoot, "LocalLanControlEndpoints.cs"));
        string contracts = ReadRequiredFile(Path.Combine(apiRoot, "Contracts", "RemoteReadOnlyApiContracts.cs"));
        string remotePosEndpoints = ReadRequiredFile(Path.Combine(apiRoot, "RemotePos", "RemotePosApiEndpoints.cs"));
        string page = ReadRequiredFile(Path.Combine(webRoot, "pages", "HostLanControlPage.tsx"));
        string api = ReadRequiredFile(Path.Combine(webRoot, "api", "remoteReadOnlyClient.ts"));
        string hook = ReadRequiredFile(Path.Combine(webRoot, "hooks", "useLocalLanControl.ts"));
        string app = ReadRequiredFile(Path.Combine(webRoot, "App.tsx"));
        string startScript = ReadRequiredFile(Path.Combine(projectRoot, "tools", "dev", "Start-SalrynLanDev.ps1"));
        string coreSmoke = ReadRequiredFile(Path.Combine(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs"));

        AssertContains(startupOptions, "--lan-bind", "LAN bind startup must support --lan-bind.");
        AssertContains(startupOptions, "SALRYN_LAN_BIND_IP", "LAN bind startup must support SALRYN_LAN_BIND_IP.");
        AssertContains(startupOptions, "--port", "LAN bind startup must support --port.");
        AssertContains(networkValidator, "WildcardForbidden", "LAN bind validator must reject wildcard addresses.");
        AssertContains(networkValidator, "IsAssignedToLocalInterface", "LAN bind validator must require local NIC assignment.");

        AssertContains(factory, "LocalApiLanBindStartupOptions.Parse", "Application factory must parse explicit LAN bind startup options.");
        AssertContains(factory, "LocalApiNetworkInterfaceValidator.ValidateExplicitLanBind", "Application factory must validate explicit LAN bind.");
        AssertContains(factory, "UseUrls(listenUrls.ToArray())", "Application factory must configure Kestrel listen URLs.");
        AssertNotContains(factory, "0.0.0.0", "Application factory must not introduce wildcard bind.");

        AssertContains(resolver, "ExplicitLanAddress", "LAN URL resolver must require explicit LAN bind mode.");
        AssertNotContains(resolver, "0.0.0.0", "LAN URL resolver must not introduce wildcard bind.");

        AssertContains(runtimeStatus, "ApiLoopbackOnly", "Runtime status must classify loopback-only API state.");
        AssertContains(runtimeStatus, "ClientUrlReady", "Runtime status must expose client URL readiness.");
        AssertContains(runtimeStatus, "BuildClientUrl", "Runtime status must build client URL through host URL helper.");
        AssertContains(hostUrls, "BuildClientUrl", "Host control URLs must build remote client URL.");
        AssertContains(hostUrls, "/remote-client", "Client URL must target remote client entry.");

        AssertContains(endpoints, "/runtime-status", "LAN control must expose runtime status endpoint.");
        AssertContains(contracts, "LocalLanControlRuntimeStatusResponse", "Contracts must include runtime status response.");

        AssertContains(page, "LAN server is actually listening on", "Host LAN Control must show actual listen URLs.");
        AssertContains(page, "clientUrlReady", "Host LAN Control must gate copy on runtime clientUrlReady.");
        AssertContains(page, "LoopbackOnly", "Host LAN Control must show loopback-only readiness state.");
        AssertContains(page, "runtimeStatusQuery", "Host LAN Control must query runtime status.");
        AssertNotContains(page, "0.0.0.0", "Host LAN Control must not show wildcard bind.");

        AssertContains(api, "/api/lan-control/runtime-status", "Web client must call runtime status endpoint.");
        AssertContains(hook, "fetchLocalLanControlRuntimeStatus", "LAN control hook must fetch runtime status.");

        AssertContains(startScript, "0.0.0.0", "Dev start script must reject 0.0.0.0.");
        AssertContains(startScript, "--lan-bind", "Dev start script must pass --lan-bind.");
        AssertContains(startScript, "/remote-client", "Dev start script must print remote client URL.");

        AssertNotContains(remotePosEndpoints, "/api/lan-control", "Remote POS surface must not expose /api/lan-control.");
        AssertRemoteClientDoesNotExposeHostCheckout(app);

        AssertContains(coreSmoke, "v2-lan-bind-truth", "CoreSmoke must expose v2-lan-bind-truth runner.");
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
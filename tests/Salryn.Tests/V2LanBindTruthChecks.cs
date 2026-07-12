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
        string normalizer = ReadRequiredFile(Path.Combine(apiRoot, "Services", "LocalApiLanBindNormalizer.cs"));
        string lanControlService = ReadRequiredFile(Path.Combine(apiRoot, "Services", "LocalLanControlService.cs"));
        string endpoints = ReadRequiredFile(Path.Combine(apiRoot, "LocalLanControlEndpoints.cs"));
        string contracts = ReadRequiredFile(Path.Combine(apiRoot, "Contracts", "RemoteReadOnlyApiContracts.cs"));
        string remotePosEndpoints = ReadRequiredFile(Path.Combine(apiRoot, "RemotePos", "RemotePosApiEndpoints.cs"));
        string page = ReadRequiredFile(Path.Combine(webRoot, "pages", "HostLanControlPage.tsx"));
        string bindNormalize = ReadRequiredFile(Path.Combine(webRoot, "utils", "lanBindNormalize.ts"));
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

        AssertContains(resolver, "List<string> urls = [defaultUrl]", "LAN URL resolver must always seed loopback/default URL first.");
        AssertContains(resolver, "!config.RemoteTransportEnabled", "LAN URL resolver must return loopback-only when remote transport is disabled.");
        AssertContains(resolver, "config.BindMode != LanRemoteTransportBindMode.ExplicitLanAddress", "LAN URL resolver must return loopback-only when bind mode is not explicit.");
        AssertContains(resolver, "ExplicitLanAddress", "LAN URL resolver must require explicit LAN bind mode.");
        AssertContains(resolver, "SalrynHostCommandLoopbackRules.IsForbiddenBindAddress", "LAN URL resolver must reject wildcard/forbidden persisted bind addresses.");
        AssertContains(resolver, "LocalApiNetworkInterfaceValidator.IsAssignedToLocalInterface(bindAddress)", "LAN URL resolver must not add stale persisted LAN bind addresses to Kestrel URLs.");
        AssertContains(resolver, "return urls;", "LAN URL resolver must degrade to loopback-only instead of throwing on stale persisted bind.");
        AssertContains(resolver, "urls.Add($\"http://{bindAddress}:{port}\")", "LAN URL resolver must only add explicit LAN URL after safety checks.");
        AssertNotContains(resolver, "0.0.0.0", "LAN URL resolver must not introduce wildcard bind.");

        AssertContains(runtimeStatus, "ApiLoopbackOnly", "Runtime status must classify loopback-only API state.");
        AssertContains(runtimeStatus, "ClientUrlReady", "Runtime status must expose client URL readiness.");
        AssertContains(runtimeStatus, "BuildClientUrl", "Runtime status must build client URL through host URL helper.");
        AssertContains(runtimeStatus, "ConfiguredBindMalformed", "Runtime status must detect malformed configured bind.");
        AssertContains(runtimeStatus, "ConfiguredBindAddressRepairSuggestion", "Runtime status must expose repair suggestion.");
        AssertContains(runtimeStatus, "DetectedLanListenIps", "Runtime status must expose detected LAN listener IPs.");
        AssertContains(hostUrls, "BuildClientUrl", "Host control URLs must build remote client URL.");
        AssertContains(hostUrls, "/remote-client", "Client URL must target remote client entry.");
        AssertContains(hostUrls, "LocalApiLanBindNormalizer", "Host URL helper must refuse double-scheme URL construction.");
        AssertNotContains(hostUrls, "http://{bindAddress.Trim()}:{Port}", "Host URL helper must not blindly prefix raw bind text.");

        AssertContains(normalizer, "NormalizeConfiguredLanBindInput", "Bind normalizer must exist.");
        AssertContains(normalizer, "WasRepaired", "Bind normalizer must report repairable URL inputs.");
        AssertContains(normalizer, "WildcardForbidden", "Bind normalizer must reject wildcards.");
        AssertContains(lanControlService, "RepairConfiguredBindAsync", "LAN control service must repair malformed bind.");
        AssertContains(lanControlService, "LocalApiLanBindNormalizer.NormalizeConfiguredLanBindInput", "LAN control save path must normalize bind input.");
        AssertContains(lanControlService, "Malformed (needs repair)", "Status display must not present raw URL bind as a safe IP.");

        AssertContains(endpoints, "/runtime-status", "LAN control must expose runtime status endpoint.");
        AssertContains(endpoints, "/bind/repair", "LAN control must expose bind repair endpoint.");
        AssertContains(contracts, "LocalLanControlRuntimeStatusResponse", "Contracts must include runtime status response.");
        AssertContains(contracts, "ConfiguredBindAddressRepairSuggestion", "Contracts must include bind repair suggestion.");
        AssertContains(contracts, "LocalLanControlBindRepairRequest", "Contracts must include bind repair request.");

        AssertContains(page, "LAN server is actually listening on", "Host LAN Control must show actual listen URLs.");
        AssertContains(page, "clientUrlReady", "Host LAN Control must gate copy on runtime clientUrlReady.");
        AssertContains(page, "LoopbackOnly", "Host LAN Control must show loopback-only readiness state.");
        AssertContains(page, "runtimeStatusQuery", "Host LAN Control must query runtime status.");
        AssertContains(page, "Repair LAN address", "Host LAN Control must offer repair action for malformed bind.");
        AssertContains(page, "Activate LAN connection", "Host LAN Control must offer activate action.");
        AssertContains(page, "canCopyClientUrl", "Host LAN Control must disable copy unless client URL is ready.");
        AssertContains(page, "LAN Connection", "Host LAN Control must present a LAN Connection wizard section.");
        AssertNotContains(page, "0.0.0.0", "Host LAN Control must not show wildcard bind.");
        AssertNotContains(page, "http://http://", "Host LAN Control must not construct double-scheme URLs.");

        AssertContains(bindNormalize, "normalizeConfiguredLanBindInput", "Frontend must normalize bind input.");
        AssertContains(bindNormalize, "0.0.0.0", "Frontend normalizer must reject 0.0.0.0.");

        AssertContains(api, "/api/lan-control/runtime-status", "Web client must call runtime status endpoint.");
        AssertContains(api, "/api/lan-control/bind/repair", "Web client must call bind repair endpoint.");
        AssertContains(hook, "fetchLocalLanControlRuntimeStatus", "LAN control hook must fetch runtime status.");
        AssertContains(hook, "repairBindMutation", "LAN control hook must expose bind repair mutation.");

        AssertContains(startScript, "0.0.0.0", "Dev start script must reject 0.0.0.0.");
        AssertContains(startScript, "--lan-bind", "Dev start script must pass --lan-bind.");
        AssertContains(startScript, "/remote-client", "Dev start script must print remote client URL.");

        AssertNotContains(remotePosEndpoints, "/api/lan-control", "Remote POS surface must not expose /api/lan-control.");
        AssertRemoteClientDoesNotExposeHostCheckout(app);

        AssertContains(coreSmoke, "v2-lan-bind-truth", "CoreSmoke must expose v2-lan-bind-truth runner.");
        AssertContains(coreSmoke, "v2-host-lan-control-ux", "CoreSmoke must expose v2-host-lan-control-ux runner.");
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

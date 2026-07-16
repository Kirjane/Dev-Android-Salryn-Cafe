namespace Salryn.Tests;

public static class PreI1FreshLicenseFirstFlowChecks
{
    public static void Run()
    {
        string root = FindRoot();
        string app = Read(root, "src", "Salryn.App.Web", "src", "App.tsx");
        string bootstrapPage = Read(root, "src", "Salryn.App.Web", "src", "pages", "BootstrapSetupPage.tsx");
        string licensePage = Read(root, "src", "Salryn.App.Web", "src", "pages", "LicensePage.tsx");
        string licenseEndpoints = Read(root, "src", "Salryn.Api.Local", "LocalLicenseEndpoints.cs");
        string bootstrapEndpoints = Read(root, "src", "Salryn.Api.Local", "LocalBootstrapEndpoints.cs");

        RequireContains(app, "isHostLicenseReady", "Fresh launch must evaluate Host license readiness.");
        RequireOrder(app, "if (!isHostLicenseReady", "if (bootstrapStatus?.firstRunRequired)", "License gate must run before bootstrap setup.");
        RequireContains(app, "status.licenseType === \"Host\"", "Frontend gate must require Host type.");
        RequireContains(app, "status?.machineMatched", "Frontend gate must require machine match.");
        RequireNotContains(bootstrapPage, "Salryn Dev General Merchandise", "Fresh bootstrap must not prefill a dev store.");
        RequireNotContains(bootstrapPage, "DEV Admin", "Fresh bootstrap must not prefill a dev admin label.");
        RequireNotContains(licensePage, "Allowed LAN terminals (Host)", "Normal License UI must not expose Host LAN count as a commercial control.");
        RequireContains(licensePage, "Each operational LAN terminal uses its own machine-bound Terminal license", "License UI must state one machine-bound license per terminal.");
        RequireContains(licensePage, "This Terminal license cannot unlock Host first-run setup", "Terminal license must explain Host bootstrap denial.");
        RequireContains(licenseEndpoints, "RequireLoopbackOperatorRequest", "Pre-login license endpoints must stay loopback-only.");
        RequireNotContains(EndpointBlock(licenseEndpoints, "MapGet(\"/api/license/status\""), "RequireAuthenticatedSession", "License status must be reachable before login.");
        RequireNotContains(EndpointBlock(licenseEndpoints, "MapPost(\"/api/license/install\""), "RequirePermission", "License install must be reachable before login.");
        RequireContains(EndpointBlock(licenseEndpoints, "MapPost(\"/api/license/install\""), "HasJsonContentType", "Pre-login license install must reject simple cross-origin form posts.");
        RequireContains(bootstrapEndpoints, "Bootstrap.HostLicenseRequired", "Bootstrap API must enforce a valid Host license.");
        RequireContains(bootstrapEndpoints, "license.LicenseType == LicenseType.Host", "Bootstrap API must reject Terminal license type.");
        RequireContains(bootstrapEndpoints, "license.MachineMatched", "Bootstrap API must require machine match.");
    }

    private static string EndpointBlock(string source, string marker)
    {
        int start = source.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("Missing endpoint marker: " + marker);
        int next = source.IndexOf("app.Map", start + marker.Length, StringComparison.Ordinal);
        return next < 0 ? source[start..] : source[start..next];
    }

    private static void RequireOrder(string source, string first, string second, string message)
    {
        int firstIndex = source.IndexOf(first, StringComparison.Ordinal);
        int secondIndex = source.IndexOf(second, StringComparison.Ordinal);
        if (firstIndex < 0 || secondIndex < 0 || firstIndex >= secondIndex) throw new InvalidOperationException(message);
    }

    private static void RequireContains(string source, string value, string message)
    {
        if (!source.Contains(value, StringComparison.Ordinal)) throw new InvalidOperationException(message + " Missing: " + value);
    }

    private static void RequireNotContains(string source, string value, string message)
    {
        if (source.Contains(value, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(message + " Forbidden: " + value);
    }

    private static string Read(string root, params string[] parts) => File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

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
}

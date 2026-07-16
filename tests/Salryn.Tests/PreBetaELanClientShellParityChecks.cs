namespace Salryn.Tests;

/// <summary>
/// PRE-BETA-E: LAN terminal uses Salryn shell language after login; host admin nav stays host-local.
/// </summary>
public static class PreBetaELanClientShellParityChecks
{
    public static void Run()
    {
        string root = FindRoot();
        string terminalShell = Read(root, "src", "Salryn.App.Web", "src", "components", "TerminalShell.tsx");
        string remotePos = Read(root, "src", "Salryn.App.Web", "src", "pages", "RemotePosClientPage.tsx");
        string landing = Read(root, "src", "Salryn.App.Web", "src", "pages", "RemoteClientLandingPage.tsx");
        string app = Read(root, "src", "Salryn.App.Web", "src", "App.tsx");
        string activation = Read(root, "src", "Salryn.App.Web", "src", "pages", "TerminalActivationPage.tsx");
        string hostShell = Read(root, "src", "Salryn.App.Web", "src", "components", "AppShell.tsx");
        string sessions = Read(root, "src", "Salryn.Api.Local", "RemotePos", "RemotePosOperatorSessionService.cs");

        // Terminal shell parity components
        RequireContains(terminalShell, "Terminal Mode", "Terminal shell must show Terminal Mode.");
        RequireContains(terminalShell, "POS Checkout", "Terminal shell must include POS Checkout nav.");
        RequireContains(terminalShell, "bg-navy-900", "Terminal shell must use Host navy header language.");
        RequireContains(terminalShell, "bg-navy-800", "Terminal shell must use Host navy rail language.");
        RequireNotContains(terminalShell, "host-lan-control", "Terminal shell must not include Terminal Management nav.");
        RequireNotContains(terminalShell, "Backup & Restore", "Terminal shell must not expose Backup nav label.");
        RequireNotContains(terminalShell, "Store Settings", "Terminal shell must not expose Store Settings nav.");
        RequireNotContains(terminalShell, "Terminal Management", "Terminal shell must not expose Terminal Management nav.");
        RequireContains(terminalShell, "Host admin pages are not available on LAN terminals", "Terminal shell must state host admin pages are unavailable.");

        // Logged-in remote POS uses TerminalShell
        RequireContains(remotePos, "TerminalShell", "Remote POS ready state must render TerminalShell.");
        RequireContains(remotePos, "client.isConnected", "Shell wrap must key off connected operator session.");
        RequireContains(remotePos, "title=\"POS Checkout\"", "Connected terminal must present POS Checkout title.");
        RequireNotContains(remotePos, "title=\"Remote POS client\"", "Connected terminal must not lead with diagnostic Remote POS client title.");
        RequireContains(remotePos, "TerminalEntryFrame", "Pre-login pairing must use Salryn terminal entry frame.");

        // Cashier/Manager server-side block for Admin/Owner
        RequireContains(sessions, "{ \"Cashier\", \"Manager\" }", "Remote login must allow only Cashier and Manager.");
        RequireContains(sessions, "Admin and Owner accounts are host-local", "Admin/Owner LAN block message must remain.");

        // Landing: primary terminal path; Remote Read-Only gated
        RequireContains(landing, "Open Salryn POS terminal", "Landing must open Salryn terminal as primary CTA.");
        RequireContains(landing, "Terminal Mode", "Landing must use Terminal Mode language.");
        RequireContains(landing, "Internal support surfaces", "Remote Read-Only must be under internal support surfaces.");
        RequireContains(landing, "support/diagnostic only", "Remote Read-Only must be marked support/diagnostic only.");
        RequireContains(landing, "Open Remote Read-Only (support only)", "Remote Read-Only CTA must be labeled support only.");
        int supportIndex = landing.IndexOf("Internal support surfaces", StringComparison.Ordinal);
        int readOnlyCtaIndex = landing.IndexOf("Open Remote Read-Only (support only)", StringComparison.Ordinal);
        RequireTrue(supportIndex >= 0 && readOnlyCtaIndex > supportIndex, "Remote Read-Only CTA must appear after internal support surfaces gate.");

        // App wiring
        RequireContains(app, "RemotePosClientPage onExitToLanding", "App must pass landing exit into Remote POS.");
        RequireContains(app, "Support · Remote Read-Only", "Read-only mode must be labeled support.");
        RequireContains(app, "readInstalledTerminalLicense() ? \"landing\" : \"activation\"", "Activation-first entry must remain.");

        // Lane B activation copy preserved
        RequireContains(activation, "do not need a Host License ID", "Lane B terminal copy must remain.");
        RequireNotContains(activation, "Include your store name, Host License ID", "Lane B Host License ID ask must stay removed.");
        RequireContains(activation, "Copy Terminal Machine Code", "Lane B terminal machine code copy must remain.");

        // Host shell still has admin surfaces (contrast)
        RequireContains(hostShell, "Terminal Management", "Host shell must retain Terminal Management.");
        RequireContains(hostShell, "getNavItems(session)", "Host shell must keep permission-filtered host nav.");
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

    private static string FindRoot()
    {
        DirectoryInfo? current = new(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Salryn.sln.");
    }

    private static void RequireContains(string source, string value, string message)
    {
        if (!source.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message + " Missing: " + value);
        }
    }

    private static void RequireNotContains(string source, string value, string message)
    {
        if (source.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message + " Forbidden: " + value);
        }
    }

    private static void RequireTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

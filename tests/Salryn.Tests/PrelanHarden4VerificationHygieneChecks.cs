namespace Salryn.Tests;

public static class PrelanHarden4VerificationHygieneChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string directoryBuildProps = Read(repoRoot, "Directory.Build.props");
        string webViewCsproj = Read(repoRoot, "src", "Salryn.Desktop.WebView", "Salryn.Desktop.WebView.csproj");
        string testsCsproj = Read(repoRoot, "tests", "Salryn.Tests", "Salryn.Tests.csproj");
        string coreSmokeCsproj = Read(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Salryn.Tools.CoreSmoke.csproj");
        string verificationTargets = Read(repoRoot, "00-HANDOFF", "PRELAN_HARDENING", "VERIFICATION_TARGETS.md");
        string closureDoc = Read(repoRoot, "00-HANDOFF", "PRELAN_HARDENING", "PRELAN_HARDENING_CLOSURE.md");
        string coreSmoke = Read(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string shellPresenter = Read(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "ShellPresenter.cs");

        RequireContains(coreSmoke, "PrelanHarden4VerificationHygieneChecks.Run();", "CoreSmoke must run PRELAN-HARDEN-4 guard.");

        RequireContains(directoryBuildProps, "SQLitePCLRaw.bundle_e_sqlite3", "NU1903 remediation must override SQLitePCLRaw bundle.");
        RequireContains(directoryBuildProps, "3.0.3", "NU1903 remediation must pin safe SQLitePCLRaw bundle version.");
        RequireContains(directoryBuildProps, "GHSA-2m69-gcr7-jv3q", "NU1903 remediation must document advisory id.");

        RequireContains(webViewCsproj, "MSB3277", "MSB3277 closure must be documented in WebView project.");
        RequireContains(webViewCsproj, "PRELAN-HARDEN-4", "MSB3277 closure must reference PRELAN-HARDEN-4.");

        RequireContains(testsCsproj, "net10.0-windows", "Salryn.Tests must remain Windows-targeted.");
        RequireContains(testsCsproj, "VERIFICATION_TARGETS.md", "Salryn.Tests must reference verification targets doc.");
        RequireContains(coreSmokeCsproj, "net10.0-windows", "CoreSmoke must remain Windows-targeted.");
        RequireContains(coreSmokeCsproj, "VERIFICATION_TARGETS.md", "CoreSmoke must reference verification targets doc.");

        RequireContains(verificationTargets, "intentionally Windows-targeted", "Verification targets doc must state Windows-targeted decision.");
        RequireContains(verificationTargets, "Salryn.Tests", "Verification targets doc must name Salryn.Tests.");
        RequireContains(verificationTargets, "Salryn.Tools.CoreSmoke", "Verification targets doc must name CoreSmoke.");

        RequireContains(closureDoc, "PRELAN-HARDEN-1", "Closure doc must mark PRELAN-HARDEN-1 complete.");
        RequireContains(closureDoc, "PRELAN-HARDEN-2", "Closure doc must mark PRELAN-HARDEN-2 complete.");
        RequireContains(closureDoc, "PRELAN-HARDEN-3", "Closure doc must mark PRELAN-HARDEN-3 complete.");
        RequireContains(closureDoc, "PRELAN-HARDEN-4", "Closure doc must mark PRELAN-HARDEN-4 complete.");
        RequireContains(closureDoc, "NU1903", "Closure doc must document NU1903 status.");
        RequireContains(closureDoc, "MSB3277", "Closure doc must document MSB3277 status.");
        RequireContains(closureDoc, "No LAN implementation has started", "Closure doc must confirm LAN not started.");
        RequireContains(closureDoc, "Store Host owns database", "Closure doc must preserve Store Host doctrine.");

        foreach (string marker in ForbiddenLanMarkers())
        {
            RequireNotContains(shellPresenter, marker, "PRELAN-HARDEN-4 must not introduce LAN marker: " + marker);
            RequireNotContains(directoryBuildProps, marker, "PRELAN-HARDEN-4 must not introduce LAN marker: " + marker);
        }

        foreach (string marker in ForbiddenInstallerMarkers())
        {
            RequireNotContains(shellPresenter, marker, "PRELAN-HARDEN-4 must not introduce installer marker: " + marker);
        }
    }

    private static IEnumerable<string> ForbiddenLanMarkers()
    {
        yield return "SignalR";
        yield return "TcpListener";
        yield return "HttpListener";
        yield return "WebSocket";
        yield return "pairing token";
        yield return "TerminalRegistration";
    }

    private static IEnumerable<string> ForbiddenInstallerMarkers()
    {
        yield return "installer role wizard";
        yield return "InstallerRoleWizard";
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

    private static string Read(DirectoryInfo root, params string[] parts)
    {
        string path = root.FullName;

        foreach (string part in parts)
        {
            path = Path.Combine(path, part);
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
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message);
        }
    }
}

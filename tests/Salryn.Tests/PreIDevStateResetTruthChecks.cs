namespace Salryn.Tests;

/// <summary>
/// PRE-I dev state reset truth: scripts and DbPaths must match real --dev / LocalAppData / production roots.
/// Does not rename operator machine state.
/// </summary>
public static class PreIDevStateResetTruthChecks
{
    public static void Run()
    {
        string root = FindRoot();
        string dbPaths = Read(root, "src", "Salryn.Infrastructure", "Paths", "DbPaths.cs");
        string projectScript = Read(root, "tools", "dev", "Backup-And-Reset-DevProjectState.ps1");
        string localScript = Read(root, "tools", "dev", "Backup-And-Reset-LocalAppDataState.ps1");
        string allScript = Read(root, "tools", "dev", "Backup-And-Reset-AllDevState.ps1");
        string compatScript = Read(root, "tools", "dev", "Backup-And-Reset-DevSalrynState.ps1");
        string report = Read(root, "00-HANDOFF", "PRE_I_DEV_STATE_RESET_TRUTH_REPORT.md");
        string readme = Read(root, "tools", "dev", "README-FRESH-STATE.md");

        RequireContains(dbPaths, "Path.Combine(projectRoot, \".salryn-dev\")", "CreateDevelopment must use .salryn-dev.");
        RequireContains(dbPaths, "Environment.SpecialFolder.CommonApplicationData", "CreateProduction must use CommonApplicationData.");
        RequireContains(dbPaths, "Path.Combine(programData, \"Salryn\")", "CreateProduction root is ProgramData/Salryn.");

        RequireFile(root, "tools", "dev", "Backup-And-Reset-DevProjectState.ps1");
        RequireFile(root, "tools", "dev", "Backup-And-Reset-LocalAppDataState.ps1");
        RequireFile(root, "tools", "dev", "Backup-And-Reset-AllDevState.ps1");
        RequireFile(root, "tools", "dev", "Backup-And-Reset-DevSalrynState.ps1");
        RequireFile(root, "tools", "dev", "README-FRESH-STATE.md");

        AssertProjectScript(projectScript);
        AssertLocalScript(localScript);
        AssertAllScript(allScript);
        AssertCompatWrapper(compatScript);
        AssertReportHonesty(report, readme);
    }

    private static void AssertProjectScript(string script)
    {
        RequireContains(script, ".salryn-dev", "Dev project script must target .salryn-dev.");
        RequireContains(script, "data\\salryn.db", "Dev project script must document salryn.db path.");
        RequireContains(script, "[switch]$WhatIf", "Dev project script must support -WhatIf.");
        RequireContains(script, "Rename-Item", "Dev project script must rename, not delete.");
        RequireNotContains(script, "Remove-Item", "Dev project script must not Remove-Item state.");
        RequireContains(script, "Salryn.Api.Local", "Dev project script must block/warn on running processes.");
        RequireContains(script, "LOCALAPPDATA", "Dev project script must state it does not reset LocalAppData.");
    }

    private static void AssertLocalScript(string script)
    {
        RequireContains(script, "$env:LOCALAPPDATA", "LocalAppData script must use LOCALAPPDATA.");
        RequireContains(script, "Join-Path $env:LOCALAPPDATA \"Salryn\"", "LocalAppData script must target Salryn under LOCALAPPDATA.");
        RequireContains(script, "[switch]$WhatIf", "LocalAppData script must support -WhatIf.");
        RequireContains(script, "Rename-Item", "LocalAppData script must rename, not delete.");
        RequireNotContains(script, "Remove-Item", "LocalAppData script must not Remove-Item state.");
        RequireContains(script, ".salryn-dev", "LocalAppData script must state it does not reset .salryn-dev.");
    }

    private static void AssertAllScript(string script)
    {
        RequireContains(script, ".salryn-dev", "All-dev script must include .salryn-dev.");
        RequireContains(script, "LOCALAPPDATA", "All-dev script must include LocalAppData.");
        RequireContains(script, "BOTH", "All-dev script must state BOTH roots are reset.");
        RequireContains(script, "[switch]$WhatIf", "All-dev script must support -WhatIf.");
        RequireContains(script, "Browser storage", "All-dev script must mention browser storage cleanup.");
        RequireContains(script, "localStorage", "All-dev script must mention localStorage.");
        RequireContains(script, "127.0.0.1:5299", "All-dev script must mention host site origin.");
        RequireContains(script, "Backup-And-Reset-DevProjectState.ps1", "All-dev must call project script.");
        RequireContains(script, "Backup-And-Reset-LocalAppDataState.ps1", "All-dev must call LocalAppData script.");
        RequireNotContains(script, "Remove-Item", "All-dev script must not Remove-Item state.");
    }

    private static void AssertCompatWrapper(string script)
    {
        RequireContains(script, "BOTH", "Compat wrapper must state BOTH .salryn-dev and LocalAppData.");
        RequireContains(script, ".salryn-dev", "Compat wrapper must mention .salryn-dev.");
        RequireContains(script, "Backup-And-Reset-AllDevState.ps1", "Compat wrapper must delegate to AllDevState.");
        RequireContains(script, "LocalAppData", "Compat wrapper must mention LocalAppData.");
        RequireContains(script, "[switch]$WhatIf", "Compat wrapper must support -WhatIf.");
        RequireNotContains(script, "Remove-Item", "Compat wrapper must not Remove-Item state.");
        // Must not be LocalAppData-only: old body only renamed LOCALAPPDATA without project root.
        RequireTrue(
            script.Contains("AllDevState", StringComparison.Ordinal) ||
            (script.Contains(".salryn-dev", StringComparison.Ordinal) && script.Contains("Rename-Item", StringComparison.Ordinal)),
            "Compat wrapper must not remain LocalAppData-only.");
    }

    private static void AssertReportHonesty(string report, string readme)
    {
        RequireContains(report, "DEVKIRCH", "Report must record Kirch stale DEVKIRCH result.");
        RequireContains(report, ".salryn-dev", "Report must document .salryn-dev truth.");
        RequireContains(report, "LOCALAPPDATA", "Report must document LocalAppData truth.");
        RequireContains(report, "mismatch", "Report must explain prior helper mismatch.");
        RequireContains(readme, ".salryn-dev", "Operator README must document .salryn-dev.");
        RequireContains(readme, "LOCALAPPDATA", "Operator README must document LocalAppData.");
        RequireContains(readme, "Why the old reset failed", "Operator README must explain prior failure.");
    }

    private static void RequireFile(string root, params string[] parts)
    {
        string path = Path.Combine(new[] { root }.Concat(parts).ToArray());
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("Required file missing: " + path);
        }
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

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message + " Missing: " + value);
        }
    }

    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
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

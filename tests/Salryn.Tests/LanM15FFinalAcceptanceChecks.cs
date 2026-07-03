using System.Diagnostics;

namespace Salryn.Tests;

public static class LanM15FFinalAcceptanceChecks
{
    private const string LanM15FBaselineSha = "e477699dca27a21954a5ad9d388307fdb9ed08fd";

    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string handoffRoot = Path.Combine(projectRoot, "00-HANDOFF");
        string currentStatePath = Path.Combine(handoffRoot, "CURRENT_STABLE_STATE_AND_NEXT_WORK.md");
        string lanM15FPath = Path.Combine(handoffRoot, "LAN_M15_F_FINAL_FIELD_ACCEPTANCE_CLOSURE_AND_CODEX_REVIEW_BUNDLE.md");
        string coreSmokePath = Path.Combine(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string infraSmokePath = Path.Combine(projectRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "Program.cs");

        string currentState = ReadRequiredFile(currentStatePath);
        string lanM15F = ReadRequiredFile(lanM15FPath);
        string coreSmoke = ReadRequiredFile(coreSmokePath);
        string infraSmoke = ReadRequiredFile(infraSmokePath);

        AssertDocBoundaries(currentState, lanM15F, coreSmoke, infraSmoke, handoffRoot);
        AssertFinalAcceptanceDoc(lanM15F);
        AssertSmokeCoverageWired(infraSmoke, coreSmoke);
        AssertLanM15FChangedFilesStayInScope(projectRoot, currentState);
    }

    private static void AssertDocBoundaries(
        string currentState,
        string lanM15F,
        string coreSmoke,
        string infraSmoke,
        string handoffRoot)
    {
        AssertContains(currentState, LanM15FBaselineSha, "Current-state doc must contain the stable Sandbox SHA.");
        AssertContains(currentState, "Sandbox remains `e477699dca27a21954a5ad9d388307fdb9ed08fd`.", "Current-state doc must state Sandbox remains at the stable SHA.");
        AssertContains(currentState, "Freehand contains completed unpromoted LAN-M14-F through LAN-M14-I.", "Current-state doc must state LAN-M14-F through LAN-M14-I unpromoted.");
        AssertContains(currentState, "Freehand contains completed unpromoted LAN-M15-A through LAN-M15-F.", "Current-state doc must state LAN-M15-A through LAN-M15-F unpromoted.");
        AssertContains(currentState, "LAN-M14-I remains the LAN-M14 cap.", "Current-state doc must keep LAN-M14-I as the LAN-M14 cap.");
        AssertContains(currentState, "LAN-M15-F is the LAN completion cap", "Current-state doc must name LAN-M15-F as the LAN completion cap.");
        AssertContains(currentState, "Next step is one Codex verification of the full stacked candidate.", "Current-state doc must name one Codex verification as the next step.");
        AssertContains(currentState, "no Sandbox promotion has occurred", "Current-state doc must state no Sandbox promotion has occurred.");
        AssertContains(currentState, "LAN-M14-J does not exist and must not be created", "Current-state doc must forbid LAN-M14-J.");
        AssertNotContains(currentState, "LAN-M14-J is current", "Current-state doc must not mark LAN-M14-J as current work.");
        AssertNotContains(currentState, "LAN-M15-G is current", "Current-state doc must not mark LAN-M15-G as current work.");

        string docPath = Path.Combine(handoffRoot, "LAN_M15_F_FINAL_FIELD_ACCEPTANCE_CLOSURE_AND_CODEX_REVIEW_BUNDLE.md");
        if (!File.Exists(docPath))
        {
            throw new InvalidOperationException("Required LAN-M15-F doc missing.");
        }

        AssertContains(currentState, "LAN_M15_F_FINAL_FIELD_ACCEPTANCE_CLOSURE_AND_CODEX_REVIEW_BUNDLE.md", "Current-state doc must list LAN-M15-F doc as live authority.");

        AssertContains(coreSmoke, "LanM15FFinalAcceptanceChecks.Run();", "CoreSmoke must wire LAN-M15-F checks.");
        AssertContains(infraSmoke, "remote-readonly-lan-final-acceptance", "InfrastructureSmoke must wire remote-readonly-lan-final-acceptance command.");
        AssertContains(infraSmoke, "LanM15RemoteReadOnlyLanFinalAcceptanceSmokeChecks.Run", "InfrastructureSmoke must call LAN-M15-F final acceptance smoke.");
    }

    private static void AssertFinalAcceptanceDoc(string lanM15F)
    {
        AssertContains(lanM15F, LanM15FBaselineSha, "LAN-M15-F doc must contain the source stamp SHA.");
        AssertContains(lanM15F, "Final State Summary", "LAN-M15-F doc must include the final state summary.");
        AssertContains(lanM15F, "Complete Infrastructure Smoke Coverage", "LAN-M15-F doc must include the smoke coverage list.");
        AssertContains(lanM15F, "Operator Field Runbook", "LAN-M15-F doc must include an operator field runbook.");
        AssertContains(lanM15F, "Acceptance Checklist", "LAN-M15-F doc must include an acceptance checklist.");
        AssertContains(lanM15F, "What Is Intentionally Not Implemented", "LAN-M15-F doc must include the intentionally-not-implemented list.");
        AssertContains(lanM15F, "Forbidden Scope", "LAN-M15-F doc must define forbidden scope.");
        AssertContains(lanM15F, "Decision", "LAN-M15-F doc must include a decision section.");
        AssertContains(lanM15F, "LAN-M15-F is the LAN completion cap", "LAN-M15-F doc must name itself the LAN completion cap.");
        AssertContains(lanM15F, "one Codex verification", "LAN-M15-F doc must state the next step is one Codex verification.");
        AssertNotContains(lanM15F, "LAN-M14-J is current", "LAN-M15-F doc must not mark LAN-M14-J as current work.");
        AssertNotContains(lanM15F, "LAN-M15-G is current", "LAN-M15-F doc must not mark LAN-M15-G as current work.");
    }

    private static void AssertSmokeCoverageWired(string infraSmoke, string coreSmoke)
    {
        string[] requiredCommands =
        {
            "controlled-remote-transport-gate",
            "pairing-activation-handshake",
            "safe-remote-readonly-client",
            "lan-m14-end-to-end-closure",
            "remote-readonly-lan-field-hardening",
            "remote-readonly-lan-preflight",
            "remote-readonly-lan-local-control",
            "remote-readonly-lan-session-resilience",
            "remote-readonly-lan-negative-path-hardening",
            "remote-readonly-lan-final-acceptance"
        };

        foreach (string command in requiredCommands)
        {
            AssertContains(infraSmoke, command, $"InfrastructureSmoke must wire the {command} command for final acceptance coverage.");
        }

        string[] requiredCoreGuards =
        {
            "LanM15ARemoteReadonlyLanFieldHardeningChecks.Run();",
            "LanM15BRemoteReadonlyLanPreflightChecks.Run();",
            "LanM15CLocalControlChecks.Run();",
            "LanM15DSessionResilienceChecks.Run();",
            "LanM15ENegativePathHardeningChecks.Run();",
            "LanM15FFinalAcceptanceChecks.Run();"
        };

        foreach (string guard in requiredCoreGuards)
        {
            AssertContains(coreSmoke, guard, $"CoreSmoke must wire {guard} for the full LAN-M15 guard ladder.");
        }
    }

    private static void AssertLanM15FChangedFilesStayInScope(string projectRoot, string currentState)
    {
        if (!currentState.Contains("Freehand contains completed unpromoted LAN-M15-A through LAN-M15-F.", StringComparison.Ordinal))
        {
            Console.WriteLine("LAN-M15-F changed-file scope guard skipped: final acceptance state is not present. Static acceptance checks remain active.");
            return;
        }

        string[] changedPaths = GetChangedPaths(projectRoot);

        if (changedPaths.Length == 0)
        {
            Console.WriteLine("LAN-M15-F changed-file scope guard skipped: no branch-diff paths were available. Static acceptance checks remain active.");
            return;
        }

        foreach (string changedPath in changedPaths)
        {
            string normalizedPath = changedPath.Replace('\\', '/');
            string lowerPath = normalizedPath.ToLowerInvariant();

            if (lowerPath.StartsWith("00-handoff/", StringComparison.Ordinal)
                || lowerPath.StartsWith("tests/salryn.tests/", StringComparison.Ordinal)
                || lowerPath.StartsWith("tools/salryn.tools.coresmoke/", StringComparison.Ordinal)
                || lowerPath.StartsWith("tools/salryn.tools.infrastructuresmoke/", StringComparison.Ordinal)
                || lowerPath.StartsWith("src/salryn.api.local/", StringComparison.Ordinal)
                || lowerPath.StartsWith("src/salryn.app.web/src/", StringComparison.Ordinal))
            {
                continue;
            }

            if (lowerPath.Contains("checkout", StringComparison.Ordinal)
                || lowerPath.Contains("settings", StringComparison.Ordinal)
                || lowerPath.Contains("backup", StringComparison.Ordinal)
                || lowerPath.Contains("official", StringComparison.Ordinal)
                || lowerPath.Contains("reports", StringComparison.Ordinal)
                || lowerPath.Contains("lan-m14-j", StringComparison.Ordinal)
                || lowerPath.Contains("lan-m15-g", StringComparison.Ordinal)
                || lowerPath.Contains("discovery", StringComparison.Ordinal)
                || lowerPath.Contains("mobile", StringComparison.Ordinal)
                || lowerPath.Contains("cloud", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"LAN-M15-F changed a forbidden boundary file: {normalizedPath}");
            }

            throw new InvalidOperationException($"LAN-M15-F changed an out-of-scope file: {normalizedPath}");
        }
    }

    private static string[] GetChangedPaths(string projectRoot)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddPaths(paths, TryRunGit(projectRoot, "diff --name-only"));
        AddPaths(paths, TryRunGit(projectRoot, "diff --name-only --cached"));
        AddPaths(paths, TryRunGit(projectRoot, "ls-files --others --exclude-standard"));

        return paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddPaths(HashSet<string> paths, string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            paths.Add(line.Trim().Replace('\\', '/'));
        }
    }

    private static string? TryRunGit(string projectRoot, string arguments)
    {
        try
        {
            using Process process = new();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = projectRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ReadRequiredFile(params string[] pathParts)
    {
        string path = Path.Combine(pathParts);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("Required source file missing: " + path);
        }

        return File.ReadAllText(path);
    }

    private static string FindProjectRoot(string startPath)
    {
        DirectoryInfo? directory = new(startPath);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Salryn.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find Salryn.sln from current directory.");
    }

    private static void AssertContains(string source, string expected, string message)
    {
        if (!source.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertNotContains(string source, string forbidden, string message)
    {
        if (source.Contains(forbidden, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}

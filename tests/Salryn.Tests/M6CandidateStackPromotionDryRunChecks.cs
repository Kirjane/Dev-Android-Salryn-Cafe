using System.Diagnostics;

namespace Salryn.Tests;

public static class M6CandidateStackPromotionDryRunChecks
{
    private const string StableSandboxSha = "0ea9790d5c86bba350c9db800c101195fa658c0c";
    private const string M6BaselineSha = "b9edccddbd1d5754c4dc05ac79554591aa31905e";
    private const string RemoteM6CandidateSha = "d01f9b225895080d5bdf0aa52f45c54658943d34";

    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string handoffRoot = Path.Combine(projectRoot, "00-HANDOFF");
        string checklistPath = Path.Combine(handoffRoot, "CANDIDATE_STACK_PROMOTION_DRY_RUN_CHECKLIST.md");
        string ledgerPath = Path.Combine(handoffRoot, "VERIFIED_CANDIDATE_STACK_LEDGER.md");
        string currentStatePath = Path.Combine(handoffRoot, "CURRENT_STABLE_STATE_AND_NEXT_WORK.md");
        string coreSmokePath = Path.Combine(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        string checklist = ReadRequiredFile(checklistPath);
        string ledger = ReadRequiredFile(ledgerPath);
        string currentState = ReadRequiredFile(currentStatePath);
        string coreSmoke = ReadRequiredFile(coreSmokePath);

        if (IsPostMSeriesPromotionCurrentState(currentState))
        {
            AssertContains(currentState, "Stable Sandbox Stack", "Current-state doc must keep a stable stack after M-series promotion.");
            AssertContains(currentState, "LAN-M14-A", "Current-state doc must keep LAN-M14-A in the post-promotion LAN stack.");
            AssertContains(currentState, "LAN-M14-", "Current-state doc must mark the active LAN-M14 candidate boundary.");
            AssertContains(currentState, "M-series docs remain useful historical context", "Current-state doc must demote M-series docs to historical context after promotion.");
            AssertContains(checklist, "Promotion still requires explicit Kirch command", "Dry-run checklist must remain available as historical promotion context.");
            AssertContains(coreSmoke, "M6CandidateStackPromotionDryRunChecks.Run();", "M6 checks must remain explicitly wired in CoreSmoke.");
            AssertM6ChangedFilesStayInScope(projectRoot, currentState);
            return;
        }

        AssertContains(checklist, StableSandboxSha, "Dry-run checklist must contain exact stable Sandbox SHA.");
        AssertContains(checklist, M6BaselineSha, "Dry-run checklist must contain exact Freehand SHA before M6.");
        AssertContains(checklist, RemoteM6CandidateSha, "Dry-run checklist must contain exact remote M6 candidate SHA before local verification.");
        AssertContains(checklist, "Promotion status: parked", "Dry-run checklist must state promotion is parked.");
        AssertContains(checklist, "Kirch explicitly says promote", "Dry-run checklist must require Kirch's explicit promotion command.");
        AssertContains(checklist, "Freehand must be ahead-only and not behind before future promotion", "Dry-run checklist must require ahead-only Freehand state.");
        AssertContains(checklist, "build/test/CoreSmoke/InfrastructureSmoke/npm all pass", "Dry-run checklist must require full verification pass.");
        AssertContains(checklist, "no schema/seed/RMO/printer/LAN mutation/feature-expansion drift is allowed", "Dry-run checklist must prohibit unsafe drift.");
        AssertContains(checklist, "ROLE-M4", "Dry-run checklist must list ROLE-M4 as verified candidate stack.");
        AssertContains(checklist, "M5", "Dry-run checklist must list M5 as verified candidate stack.");
        if (currentState.Contains("M8 is current candidate work", StringComparison.Ordinal))
        {
            AssertContains(checklist, "M6", "Dry-run checklist must keep M6 in the verified candidate stack when M8 is current.");
            AssertContains(checklist, "M7", "Dry-run checklist must keep M7 in the verified candidate stack when M8 is current.");
            AssertContains(checklist, "M8 is current candidate work and unverified until Codex verifies", "Dry-run checklist must mark M8 unverified until Codex verifies.");
            AssertContains(checklist, "M9 is not started", "Dry-run checklist must state M9 is not started when M8 is current.");
        }
        else if (currentState.Contains("M7 is current candidate work", StringComparison.Ordinal))
        {
            AssertContains(checklist, "M6", "Dry-run checklist must keep M6 in the verified candidate stack when M7 is current.");
            AssertContains(checklist, "M7 is current candidate work and unverified until Codex verifies", "Dry-run checklist must mark M7 unverified until Codex verifies.");
            AssertContains(checklist, "M8 is not started", "Dry-run checklist must state M8 is not started when M7 is current.");
        }
        else
        {
            AssertContains(checklist, "M6 is current candidate work and unverified until Codex verifies", "Dry-run checklist must mark M6 unverified until Codex verifies.");
            AssertContains(checklist, "M7 is not started", "Dry-run checklist must state M7 is not started.");
        }
        AssertContains(checklist, "LAN-M14 is not started", "Dry-run checklist must state LAN-M14 is not started.");
        AssertContains(checklist, "CANDIDATE_STACK_PROMOTION_DRY_RUN_CHECKLIST.md", "Dry-run checklist must list itself as an M6 file.");
        AssertContains(checklist, "tests/Salryn.Tests/M6CandidateStackPromotionDryRunChecks.cs", "Dry-run checklist must list the M6 guard file.");

        AssertContains(ledger, "## Candidate Stack File Manifest", "Ledger must contain a Candidate Stack File Manifest.");
        AssertContains(ledger, RemoteM6CandidateSha, "Ledger must contain exact remote M6 candidate SHA before local verification.");
        AssertContains(ledger, "Files introduced by ROLE-M4", "Ledger manifest must distinguish ROLE-M4 files.");
        AssertContains(ledger, "Files introduced by M5", "Ledger manifest must distinguish M5 files.");
        AssertContains(ledger, "Files introduced by M6", "Ledger manifest must distinguish M6 files.");
        AssertContains(ledger, "CANDIDATE_STACK_PROMOTION_DRY_RUN_CHECKLIST.md", "Ledger manifest must list M6 checklist file.");
        if (currentState.Contains("M8 is current candidate work", StringComparison.Ordinal))
        {
            AssertContains(ledger, "### M6 - candidate stack verification replay and promotion-readiness dry run", "Ledger must keep M6 in the verified candidate stack when M8 is current.");
            AssertContains(ledger, "### M7 - candidate stack limit guard and promotion debt meter", "Ledger must keep M7 in the verified candidate stack when M8 is current.");
            AssertContains(ledger, "M8 is current candidate work and is not verified until Codex verifies it", "Ledger must mark M8 unverified until Codex verifies.");
            AssertContains(ledger, "M9 is not started", "Ledger must state M9 is not started when M8 is current.");
        }
        else if (currentState.Contains("M7 is current candidate work", StringComparison.Ordinal))
        {
            AssertContains(ledger, "### M6 - candidate stack verification replay and promotion-readiness dry run", "Ledger must keep M6 in the verified candidate stack when M7 is current.");
            AssertContains(ledger, "M7 is current candidate work and is not verified until Codex verifies it", "Ledger must mark M7 unverified until Codex verifies.");
            AssertContains(ledger, "M8 is not started", "Ledger must state M8 is not started when M7 is current.");
        }
        else
        {
            AssertContains(ledger, "M6 is current candidate work and is not verified until Codex verifies it", "Ledger must mark M6 unverified until Codex verifies.");
            AssertContains(ledger, "M7 is not started", "Ledger must state M7 is not started.");
        }
        AssertContains(ledger, "LAN-M14 is not started", "Ledger must state LAN-M14 is not started.");

        AssertContains(currentState, "Stable as of `0ea9790d5c86bba350c9db800c101195fa658c0c`", "Current-state doc must preserve stable Sandbox SHA.");
        AssertContains(currentState, RemoteM6CandidateSha, "Current-state doc must contain exact remote M6 candidate SHA before local verification.");
        AssertContains(currentState, "ROLE-M4 is verified candidate work", "Current-state doc must mark ROLE-M4 as verified candidate.");
        AssertContains(currentState, "M5 is verified candidate work", "Current-state doc must mark M5 as verified candidate.");
        if (currentState.Contains("M8 is current candidate work", StringComparison.Ordinal))
        {
            AssertContains(currentState, "M6 is verified candidate work", "Current-state doc must mark M6 verified candidate when M8 is current.");
            AssertContains(currentState, "M7 is verified candidate work", "Current-state doc must mark M7 verified candidate when M8 is current.");
            AssertContains(currentState, "M8 is current candidate work and is not verified until Codex verifies it", "Current-state doc must mark M8 current and unverified.");
            AssertContains(currentState, "M9 is not started", "Current-state doc must state M9 is not started when M8 is current.");
        }
        else if (currentState.Contains("M7 is current candidate work", StringComparison.Ordinal))
        {
            AssertContains(currentState, "M6 is verified candidate work", "Current-state doc must mark M6 verified candidate when M7 is current.");
            AssertContains(currentState, "M7 is current candidate work and is not verified until Codex verifies it", "Current-state doc must mark M7 current and unverified.");
            AssertContains(currentState, "M8 is not started", "Current-state doc must state M8 is not started when M7 is current.");
        }
        else
        {
            AssertContains(currentState, "M6 is current candidate work and is not verified until Codex verifies it", "Current-state doc must mark M6 current and unverified.");
            AssertContains(currentState, "M7 is not started", "Current-state doc must state M7 is not started.");
        }
        AssertContains(currentState, "Promotion is parked", "Current-state doc must state promotion is parked.");
        AssertContains(currentState, "Freehand is intentionally ahead of Sandbox", "Current-state doc must state Freehand is intentionally ahead.");
        AssertContains(currentState, "LAN-M14 is not started", "Current-state doc must state LAN-M14 is not started.");
        AssertContains(currentState, "VERIFIED_CANDIDATE_STACK_LEDGER.md", "Current-state doc must point to ledger as live coordination doc.");
        AssertContains(currentState, "CANDIDATE_STACK_PROMOTION_DRY_RUN_CHECKLIST.md", "Current-state doc must point to dry-run checklist as live coordination doc.");
        AssertNotContains(currentState, "M6 is stable", "Current-state doc must not mark M6 stable.");
        if (!currentState.Contains("M7 is current candidate work", StringComparison.Ordinal))
        {
            AssertNotContains(currentState, "M7 is current candidate work", "Current-state doc must not start M7 before M7 begins.");
        }

        AssertContains(coreSmoke, "RoleM4PermissionBoundaryRegressionMatrixChecks.RunAsync().GetAwaiter().GetResult();", "M4 checks must remain wired in CoreSmoke.");
        AssertContains(coreSmoke, "M5VerifiedCandidateStackLedgerChecks.Run();", "M5 checks must remain wired in CoreSmoke.");
        AssertContains(coreSmoke, "M6CandidateStackPromotionDryRunChecks.Run();", "M6 checks must be explicitly wired in CoreSmoke.");

        AssertM6ChangedFilesStayInScope(projectRoot, currentState);
    }

    private static void AssertM6ChangedFilesStayInScope(string projectRoot, string currentState)
    {
        if (IsPostMSeriesPromotionCurrentState(currentState))
        {
            Console.WriteLine("M6 changed-file scope guard skipped: M6 has been promoted. Static dry-run checks remain active.");
            return;
        }

        if (!currentState.Contains("M6 is current candidate work", StringComparison.Ordinal))
        {
            Console.WriteLine("M6 changed-file scope guard skipped: M6 is no longer current candidate work. Static dry-run checks remain active.");
            return;
        }

        string[] changedPaths = GetM6ChangedPaths(projectRoot);

        if (changedPaths.Length == 0)
        {
            Console.WriteLine("M6 changed-file scope guard skipped: no branch-diff paths were available. Static dry-run checks remain active.");
            return;
        }

        HashSet<string> allowedPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "00-HANDOFF/CANDIDATE_STACK_PROMOTION_DRY_RUN_CHECKLIST.md",
            "00-HANDOFF/VERIFIED_CANDIDATE_STACK_LEDGER.md",
            "00-HANDOFF/CURRENT_STABLE_STATE_AND_NEXT_WORK.md",
            "tests/Salryn.Tests/M5VerifiedCandidateStackLedgerChecks.cs",
            "tests/Salryn.Tests/M6CandidateStackPromotionDryRunChecks.cs",
            "tools/Salryn.Tools.CoreSmoke/Program.cs"
        };

        foreach (string changedPath in changedPaths)
        {
            string normalizedPath = changedPath.Replace('\\', '/');

            if (!allowedPaths.Contains(normalizedPath))
            {
                throw new InvalidOperationException($"M6 changed an out-of-scope file: {normalizedPath}");
            }

            string lowerPath = normalizedPath.ToLowerInvariant();

            if (lowerPath.Contains("migrations", StringComparison.Ordinal)
                || lowerPath.Contains("seeding", StringComparison.Ordinal)
                || lowerPath.Contains("checkout", StringComparison.Ordinal)
                || lowerPath.Contains("settings", StringComparison.Ordinal)
                || lowerPath.Contains("backup", StringComparison.Ordinal)
                || lowerPath.Contains("official", StringComparison.Ordinal)
                || lowerPath.Contains("reports", StringComparison.Ordinal)
                || lowerPath.Contains("rmo", StringComparison.Ordinal)
                || lowerPath.Contains("pdf", StringComparison.Ordinal)
                || lowerPath.Contains("crystal", StringComparison.Ordinal)
                || lowerPath.Contains("escpos", StringComparison.Ordinal)
                || lowerPath.Contains("esc-pos", StringComparison.Ordinal)
                || lowerPath.Contains("printer", StringComparison.Ordinal)
                || lowerPath.Contains("receipt", StringComparison.Ordinal)
                || lowerPath.Contains("bind", StringComparison.Ordinal)
                || lowerPath.Contains("transport", StringComparison.Ordinal)
                || lowerPath.Contains("dispatch", StringComparison.Ordinal)
                || lowerPath.Contains("discovery", StringComparison.Ordinal)
                || lowerPath.Contains("mobile", StringComparison.Ordinal)
                || lowerPath.Contains("cloud", StringComparison.Ordinal)
                || lowerPath.Contains("roleeditor", StringComparison.Ordinal)
                || lowerPath.Contains("permissioneditor", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"M6 changed a forbidden boundary file: {normalizedPath}");
            }
        }
    }

    private static string[] GetM6ChangedPaths(string projectRoot)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddPaths(paths, TryRunGit(projectRoot, "diff --name-only " + M6BaselineSha + "...HEAD"));
        AddPaths(paths, TryRunGit(projectRoot, "diff --name-only"));
        AddPaths(paths, TryRunGit(projectRoot, "diff --name-only --cached"));
        AddPaths(paths, TryRunGit(projectRoot, "ls-files --others --exclude-standard"));

        return paths.Order(StringComparer.OrdinalIgnoreCase).ToArray();
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

    private static bool IsPostMSeriesPromotionCurrentState(string currentState)
    {
        return (currentState.Contains("M-series is stable on Sandbox", StringComparison.Ordinal)
                || currentState.Contains("LAN-M14-A is stable", StringComparison.Ordinal)
                || currentState.Contains("LAN-M14-B is stable", StringComparison.Ordinal))
            && currentState.Contains("LAN-M14-", StringComparison.Ordinal);
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

    private static string ReadRequiredFile(string path)
    {
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

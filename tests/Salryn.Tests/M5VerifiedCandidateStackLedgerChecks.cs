using System.Diagnostics;

namespace Salryn.Tests;

public static class M5VerifiedCandidateStackLedgerChecks
{
    private const string StableSandboxSha = "0ea9790d5c86bba350c9db800c101195fa658c0c";
    private const string VerifiedFreehandCandidateSha = "cacc2ca44cfddf4b067c010d73917808fbfdaa7f";
    private const string RemoteM5CandidateSha = "a87d1d2b47e70c3f939b7f738cf18573906cfcf4";

    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string handoffRoot = Path.Combine(projectRoot, "00-HANDOFF");
        string ledgerPath = Path.Combine(handoffRoot, "VERIFIED_CANDIDATE_STACK_LEDGER.md");
        string currentStatePath = Path.Combine(handoffRoot, "CURRENT_STABLE_STATE_AND_NEXT_WORK.md");
        string coreSmokePath = Path.Combine(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        string ledger = ReadRequiredFile(ledgerPath);
        string currentState = ReadRequiredFile(currentStatePath);
        string coreSmoke = ReadRequiredFile(coreSmokePath);

        if (IsPostMSeriesPromotionCurrentState(currentState))
        {
            AssertContains(currentState, "Stable Sandbox Stack", "Current-state doc must keep a stable stack after M-series promotion.");
            AssertContains(currentState, "LAN-M14-A", "Current-state doc must keep LAN-M14-A in the post-promotion LAN stack.");
            AssertContains(currentState, "LAN-M14-", "Current-state doc must mark the active LAN-M14 candidate boundary.");
            AssertContains(currentState, "M-series docs remain useful historical context", "Current-state doc must demote M-series docs to historical context after promotion.");
            if (currentState.Contains("LAN-M14-I is the LAN-M14 cap", StringComparison.Ordinal))
            {
                AssertContains(currentState, "LAN-M14-F through LAN-M14-I are candidate work completed", "Current-state doc must mark LAN-M14-F through LAN-M14-I as candidate work completed.");
                AssertContains(currentState, "Hybrid bridge is implemented", "Current-state doc must state hybrid bridge is implemented.");
            }
            AssertContains(ledger, "## Candidate Stack File Manifest", "Ledger must remain available as historical candidate-stack context after promotion.");
            AssertContains(coreSmoke, "M5VerifiedCandidateStackLedgerChecks.Run();", "M5 must remain explicitly wired into CoreSmoke.");
            AssertM5ChangedFilesStayInScope(projectRoot, currentState);
            return;
        }

        AssertContains(ledger, StableSandboxSha, "Ledger must contain exact stable Sandbox SHA.");
        AssertContains(ledger, RemoteM5CandidateSha, "Ledger must contain exact remote M5 candidate SHA before local verification.");

        if (SandboxAndFreehandAreIdentical(projectRoot))
        {
            Console.WriteLine("M5 Freehand candidate SHA exact check skipped: Sandbox and Freehand appear identical after promotion. Stable/candidate wording checks remain active.");
        }
        else
        {
            AssertContains(ledger, VerifiedFreehandCandidateSha, "Ledger must contain exact verified Freehand candidate SHA.");
        }

        AssertContains(ledger, "Promotion status: parked", "Ledger must state promotion is parked.");
        AssertContains(ledger, "Promotion is intentionally deferred", "Ledger must explain parked promotion policy.");
        AssertContains(ledger, "Freehand may remain ahead of Sandbox", "Ledger must allow parked Freehand-ahead state.");
        AssertContains(ledger, "Do not reset Freehand unless Kirch explicitly commands it", "Ledger must block accidental Freehand reset.");
        AssertContains(ledger, "Do not promote unless Kirch explicitly commands it", "Ledger must block accidental promotion.");
        AssertContains(ledger, "ROLE-M4", "Ledger must record ROLE-M4.");
        AssertContains(ledger, "verified candidate", "Ledger must mark ROLE-M4 as verified candidate.");
        AssertContains(ledger, "not stable until promotion", "Ledger must not mark ROLE-M4 stable.");
        if (currentState.Contains("M5 is current candidate work", StringComparison.Ordinal))
        {
            AssertContains(ledger, "M5 is being added now and is not verified until Codex verifies", "Ledger must mark M5 unverified until Codex verification.");
        }
        else
        {
            AssertContains(ledger, "### M5 - current-state drift guard and verified candidate stack ledger", "Ledger must keep M5 in the verified candidate stack after M5 verification.");
            AssertContains(ledger, "Status: verified candidate on Freehand", "Ledger must mark verified candidate stack entries as verified.");
        }
        if (currentState.Contains("M8 is current candidate work", StringComparison.Ordinal))
        {
            AssertContains(ledger, "### M6 - candidate stack verification replay and promotion-readiness dry run", "Ledger must keep M6 in the verified candidate stack when M8 is current.");
            AssertContains(ledger, "### M7 - candidate stack limit guard and promotion debt meter", "Ledger must keep M7 in the verified candidate stack when M8 is current.");
            AssertContains(ledger, "M8 is current candidate work and is not verified until Codex verifies it", "Ledger must mark M8 current but unverified when M8 is stacked above M5.");
            AssertContains(ledger, "M9 is not started", "Ledger must state M9 is not started when M8 is current.");
        }
        else if (currentState.Contains("M7 is current candidate work", StringComparison.Ordinal))
        {
            AssertContains(ledger, "### M6 - candidate stack verification replay and promotion-readiness dry run", "Ledger must keep M6 in the verified candidate stack when M7 is current.");
            AssertContains(ledger, "M7 is current candidate work and is not verified until Codex verifies it.", "Ledger must mark M7 current but unverified when M7 is stacked above M5.");
            AssertContains(ledger, "M8 is not started", "Ledger must state M8 is not started when M7 is current.");
        }
        else if (currentState.Contains("M6 is current candidate work", StringComparison.Ordinal))
        {
            AssertContains(ledger, "M6 is current candidate work and is not verified until Codex verifies it.", "Ledger must mark M6 current but unverified when M6 is stacked above M5.");
            AssertContains(ledger, "M7 is not started", "Ledger must state M7 is not started when M6 is current.");
        }
        else
        {
            AssertContains(ledger, "M6 is not started", "Ledger must state M6 is not started before M6 begins.");
        }
        AssertContains(ledger, "LAN-M14 is not started", "Ledger must state LAN-M14 is not started.");
        AssertContains(ledger, "`00-HANDOFF/CURRENT_STABLE_STATE_AND_NEXT_WORK.md`", "Ledger must define current-state doc as live coordination authority.");
        AssertContains(ledger, "`00-HANDOFF/VERIFIED_CANDIDATE_STACK_LEDGER.md`", "Ledger must define stack ledger as live coordination authority.");
        if (currentState.Contains("M8 is current candidate work", StringComparison.Ordinal))
        {
            AssertContains(ledger, "`00-HANDOFF/CANDIDATE_STACK_PROMOTION_DRY_RUN_CHECKLIST.md`", "Ledger must include the M6 dry-run checklist as live coordination authority when M8 is current.");
            AssertContains(ledger, "`00-HANDOFF/CANDIDATE_STACK_PROMOTION_DEBT_METER.md`", "Ledger must include the M7 debt meter as live coordination authority when M8 is current.");
            AssertContains(ledger, "`00-HANDOFF/M_SERIES_COMPLETION_CRITERIA_AND_LAN_TRANSITION_GATE.md`", "Ledger must include the M8 completion gate as live coordination authority when M8 is current.");
        }
        else if (currentState.Contains("M7 is current candidate work", StringComparison.Ordinal))
        {
            AssertContains(ledger, "`00-HANDOFF/CANDIDATE_STACK_PROMOTION_DRY_RUN_CHECKLIST.md`", "Ledger must include the M6 dry-run checklist as live coordination authority when M7 is current.");
            AssertContains(ledger, "`00-HANDOFF/CANDIDATE_STACK_PROMOTION_DEBT_METER.md`", "Ledger must include the M7 debt meter as live coordination authority when M7 is current.");
        }
        else if (currentState.Contains("M6 is current candidate work", StringComparison.Ordinal))
        {
            AssertContains(ledger, "`00-HANDOFF/CANDIDATE_STACK_PROMOTION_DRY_RUN_CHECKLIST.md`", "Ledger must include the M6 dry-run checklist as live coordination authority when M6 is current.");
        }
        else
        {
            AssertContains(ledger, "Only `00-HANDOFF/CURRENT_STABLE_STATE_AND_NEXT_WORK.md` and `00-HANDOFF/VERIFIED_CANDIDATE_STACK_LEDGER.md` are live coordination docs", "Ledger must define live coordination docs before M6 adds the checklist.");
        }
        AssertContains(ledger, "Do not follow stale reports without checking GitHub compare", "Ledger must warn against stale reports.");

        AssertContains(currentState, "Stable as of `0ea9790d5c86bba350c9db800c101195fa658c0c`", "Current-state doc must preserve Sandbox stable SHA.");
        AssertContains(currentState, RemoteM5CandidateSha, "Current-state doc must contain exact remote M5 candidate SHA before local verification.");
        AssertContains(currentState, "ROLE-M4 is verified candidate work on `KIRCH-FREEHAND`, not stable Sandbox work", "Current-state doc must not falsely mark ROLE-M4 stable.");
        if (currentState.Contains("M5 is current candidate work", StringComparison.Ordinal))
        {
            AssertContains(currentState, "M5 is current candidate work and is not verified until Codex verifies it", "Current-state doc must mark M5 current but unverified.");
        }
        else
        {
            AssertContains(currentState, "M5 is verified candidate work on `KIRCH-FREEHAND`, not stable Sandbox work.", "Current-state doc must mark M5 verified candidate after M5 verification.");
        }
        AssertContains(currentState, "Promotion is parked", "Current-state doc must state parked promotion.");
        AssertContains(currentState, "Freehand is intentionally ahead of Sandbox", "Current-state doc must warn about parked Freehand-ahead state.");
        if (currentState.Contains("M8 is current candidate work", StringComparison.Ordinal))
        {
            AssertContains(currentState, "M6 is verified candidate work on `KIRCH-FREEHAND`, not stable Sandbox work.", "Current-state doc must mark M6 verified candidate when M8 is current.");
            AssertContains(currentState, "M7 is verified candidate work on `KIRCH-FREEHAND`, not stable Sandbox work.", "Current-state doc must mark M7 verified candidate when M8 is current.");
            AssertContains(currentState, "M8 is current candidate work and is not verified until Codex verifies it.", "Current-state doc must mark M8 current but unverified when M8 is stacked above M5.");
            AssertContains(currentState, "M9 is not started", "Current-state doc must state M9 is not started when M8 is current.");
        }
        else if (currentState.Contains("M7 is current candidate work", StringComparison.Ordinal))
        {
            AssertContains(currentState, "M6 is verified candidate work on `KIRCH-FREEHAND`, not stable Sandbox work.", "Current-state doc must mark M6 verified candidate when M7 is current.");
            AssertContains(currentState, "M7 is current candidate work and is not verified until Codex verifies it.", "Current-state doc must mark M7 current but unverified when M7 is stacked above M5.");
            AssertContains(currentState, "M8 is not started", "Current-state doc must state M8 is not started when M7 is current.");
        }
        else if (currentState.Contains("M6 is current candidate work", StringComparison.Ordinal))
        {
            AssertContains(currentState, "M6 is current candidate work and is not verified until Codex verifies it.", "Current-state doc must mark M6 current but unverified when M6 is stacked above M5.");
            AssertContains(currentState, "M7 is not started", "Current-state doc must state M7 is not started when M6 is current.");
        }
        else
        {
            AssertContains(currentState, "M6 is not started", "Current-state doc must state M6 is not started before M6 begins.");
        }
        AssertContains(currentState, "LAN-M14 is not started", "Current-state doc must state LAN-M14 is not started.");
        AssertContains(currentState, "VERIFIED_CANDIDATE_STACK_LEDGER.md", "Current-state doc must point to the live ledger.");
        AssertNotContains(currentState, "ROLE-M4 is stable", "Current-state doc must not say ROLE-M4 is stable.");
        AssertNotContains(currentState, "M5 is stable", "Current-state doc must not say M5 is stable.");

        AssertContains(coreSmoke, "M5VerifiedCandidateStackLedgerChecks.Run();", "M5 must be explicitly wired into CoreSmoke.");
        AssertM5ChangedFilesStayInScope(projectRoot, currentState);
    }

    private static void AssertM5ChangedFilesStayInScope(string projectRoot, string currentState)
    {
        if (IsPostMSeriesPromotionCurrentState(currentState))
        {
            Console.WriteLine("M5 changed-file scope guard skipped: M5 has been promoted. Static historical ledger checks remain active.");
            return;
        }

        if (!currentState.Contains("M5 is current candidate work", StringComparison.Ordinal))
        {
            Console.WriteLine("M5 changed-file scope guard skipped: M5 is no longer the current candidate work. Static ledger checks remain active.");
            return;
        }

        string[] changedPaths = GetM5ChangedPaths(projectRoot);

        if (changedPaths.Length == 0)
        {
            Console.WriteLine("M5 changed-file scope guard skipped: no branch-diff paths were available. Static ledger checks remain active.");
            return;
        }

        HashSet<string> allowedPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "00-HANDOFF/VERIFIED_CANDIDATE_STACK_LEDGER.md",
            "00-HANDOFF/CURRENT_STABLE_STATE_AND_NEXT_WORK.md",
            "tests/Salryn.Tests/M5VerifiedCandidateStackLedgerChecks.cs",
            "tests/Salryn.Tests/RoleM4PermissionBoundaryRegressionMatrixChecks.cs",
            "tools/Salryn.Tools.CoreSmoke/Program.cs"
        };

        foreach (string changedPath in changedPaths)
        {
            string normalizedPath = changedPath.Replace('\\', '/');

            if (!allowedPaths.Contains(normalizedPath))
            {
                throw new InvalidOperationException($"M5 changed an out-of-scope file: {normalizedPath}");
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
                throw new InvalidOperationException($"M5 changed a forbidden boundary file: {normalizedPath}");
            }
        }
    }

    private static bool SandboxAndFreehandAreIdentical(string projectRoot)
    {
        string? output = TryRunGit(projectRoot, "rev-list --left-right --count origin/KIRCH-SANDBOX...origin/KIRCH-FREEHAND");
        if (string.IsNullOrWhiteSpace(output))
        {
            output = TryRunGit(projectRoot, "rev-list --left-right --count KIRCH-SANDBOX...KIRCH-FREEHAND");
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        string[] parts = output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            && string.Equals(parts[0], "0", StringComparison.Ordinal)
            && string.Equals(parts[1], "0", StringComparison.Ordinal);
    }

    private static string[] GetM5ChangedPaths(string projectRoot)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddPaths(paths, TryRunGit(projectRoot, "diff --name-only " + VerifiedFreehandCandidateSha + "...HEAD"));
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
        if (currentState.Contains("Stable Sandbox Stack", StringComparison.Ordinal)
            && currentState.Contains("M-series docs remain useful historical context", StringComparison.Ordinal)
            && currentState.Contains("LAN-M14-", StringComparison.Ordinal))
        {
            return true;
        }

        return (currentState.Contains("M-series is stable on Sandbox", StringComparison.Ordinal)
                || currentState.Contains("LAN-M14-A is stable", StringComparison.Ordinal)
                || currentState.Contains("LAN-M14-B is stable", StringComparison.Ordinal)
                || currentState.Contains("LAN-M14-C is stable", StringComparison.Ordinal))
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

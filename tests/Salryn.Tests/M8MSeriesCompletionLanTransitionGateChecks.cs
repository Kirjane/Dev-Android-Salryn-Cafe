using System.Diagnostics;

namespace Salryn.Tests;

public static class M8MSeriesCompletionLanTransitionGateChecks
{
    private const string StableSandboxSha = "0ea9790d5c86bba350c9db800c101195fa658c0c";
    private const string M8BaselineSha = "a048a1d847cfa1f435da23b2cb128cd7f360719f";

    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string handoffRoot = Path.Combine(projectRoot, "00-HANDOFF");
        string mSeriesGatePath = Path.Combine(handoffRoot, "M_SERIES_COMPLETION_CRITERIA_AND_LAN_TRANSITION_GATE.md");
        string debtMeterPath = Path.Combine(handoffRoot, "CANDIDATE_STACK_PROMOTION_DEBT_METER.md");
        string dryRunPath = Path.Combine(handoffRoot, "CANDIDATE_STACK_PROMOTION_DRY_RUN_CHECKLIST.md");
        string ledgerPath = Path.Combine(handoffRoot, "VERIFIED_CANDIDATE_STACK_LEDGER.md");
        string currentStatePath = Path.Combine(handoffRoot, "CURRENT_STABLE_STATE_AND_NEXT_WORK.md");
        string coreSmokePath = Path.Combine(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        string mSeriesGate = ReadRequiredFile(mSeriesGatePath);
        string debtMeter = ReadRequiredFile(debtMeterPath);
        string dryRunChecklist = ReadRequiredFile(dryRunPath);
        string ledger = ReadRequiredFile(ledgerPath);
        string currentState = ReadRequiredFile(currentStatePath);
        string coreSmoke = ReadRequiredFile(coreSmokePath);

        if (IsCurrentLanM16QaBState(currentState))
        {
            AssertContains(currentState, "Stable as of `e477699dca27a21954a5ad9d388307fdb9ed08fd`", "Current-state doc must preserve the current stable Sandbox SHA.");
            AssertContains(currentState, "LAN-M16-QA-B", "Current-state doc must mark LAN-M16-QA-B as the current candidate work.");
            AssertContains(currentState, "Current live coordination authority:", "Current-state doc must keep live coordination authority visible.");
            AssertContains(currentState, "Historical M-series and ROLE docs remain context only and must not override current source truth.", "Current-state doc must keep older M-series docs demoted to historical context.");

            AssertContains(mSeriesGate, "## LAN Transition Gate", "M8 gate doc must remain available as historical LAN transition context.");
            AssertContains(coreSmoke, "M8MSeriesCompletionLanTransitionGateChecks.Run();", "M8 checks must remain explicitly wired in CoreSmoke.");
            return;
        }

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
            AssertContains(mSeriesGate, "## LAN Transition Gate", "M8 gate doc must remain available as historical LAN transition context.");
            AssertContains(coreSmoke, "M8MSeriesCompletionLanTransitionGateChecks.Run();", "M8 checks must remain explicitly wired in CoreSmoke.");
            AssertM8ChangedFilesStayInScope(projectRoot, currentState);
            return;
        }

        AssertContains(mSeriesGate, StableSandboxSha, "M8 gate doc must contain exact stable Sandbox SHA.");
        AssertContains(mSeriesGate, M8BaselineSha, "M8 gate doc must contain exact Freehand SHA before M8.");
        AssertContains(mSeriesGate, "Promotion status: parked until full M-series completion", "M8 gate doc must state promotion is parked until full M-series completion.");
        AssertContains(mSeriesGate, "ROLE-M4", "M8 gate doc must list ROLE-M4 as verified candidate.");
        AssertContains(mSeriesGate, "M5", "M8 gate doc must list M5 as verified candidate.");
        AssertContains(mSeriesGate, "M6", "M8 gate doc must list M6 as verified candidate.");
        AssertContains(mSeriesGate, "M7", "M8 gate doc must list M7 as verified candidate.");
        AssertContains(mSeriesGate, "M8 is current candidate work and unverified until Codex verifies it", "M8 gate doc must mark M8 unverified until Codex verifies.");
        AssertContains(mSeriesGate, "M9 is not started", "M8 gate doc must state M9 is not started.");
        AssertContains(mSeriesGate, "LAN-M14 is not started", "M8 gate doc must state LAN-M14 is not started.");
        AssertContains(mSeriesGate, "LAN completion series is not started", "M8 gate doc must state LAN completion is not started.");
        AssertContains(mSeriesGate, "## M-Series Completion Criteria", "M8 gate doc must define M-series completion criteria.");
        AssertContains(mSeriesGate, "Final M-series closure review is completed", "M8 gate doc must require final M-series closure review.");
        AssertContains(mSeriesGate, "Kirch explicitly declares M-series complete", "M8 gate doc must require explicit Kirch completion declaration.");
        AssertContains(mSeriesGate, "Kirch explicitly authorizes final promotion", "M8 gate doc must require explicit Kirch final promotion authorization.");
        AssertContains(mSeriesGate, "## LAN Transition Gate", "M8 gate doc must define LAN Transition Gate.");
        AssertContains(mSeriesGate, "LAN completion series may begin only after", "M8 gate doc must gate LAN completion after promotion prerequisites.");
        AssertContains(mSeriesGate, "M-series is promoted into `KIRCH-SANDBOX`", "M8 gate doc must require M-series promotion before LAN completion.");
        AssertContains(mSeriesGate, "`KIRCH-FREEHAND` is reset/aligned to the promoted Sandbox baseline", "M8 gate doc must require Freehand alignment after promotion before LAN completion.");
        AssertContains(mSeriesGate, "Sandbox and Freehand are identical after promotion", "M8 gate doc must require branch identity after promotion.");
        AssertContains(mSeriesGate, "LAN completion series is not started by M8", "M8 gate doc must state M8 does not start LAN completion.");
        AssertContains(mSeriesGate, "LAN-M14 is not started by M8", "M8 gate doc must state M8 does not start LAN-M14.");
        AssertContains(mSeriesGate, "No LAN bind activation happens in M8", "M8 gate doc must prohibit LAN bind activation.");
        AssertContains(mSeriesGate, "No live remote transport dispatch happens in M8", "M8 gate doc must prohibit live transport dispatch.");
        AssertContains(mSeriesGate, "No LAN mutation happens in M8", "M8 gate doc must prohibit LAN mutation.");

        AssertContains(currentState, "M_SERIES_COMPLETION_CRITERIA_AND_LAN_TRANSITION_GATE.md", "Current-state doc must link M8 gate doc as live coordination authority.");
        AssertContains(currentState, "ROLE-M4 is verified candidate work", "Current-state doc must state ROLE-M4 is verified candidate.");
        AssertContains(currentState, "M5 is verified candidate work", "Current-state doc must state M5 is verified candidate.");
        AssertContains(currentState, "M6 is verified candidate work", "Current-state doc must state M6 is verified candidate.");
        AssertContains(currentState, "M7 is verified candidate work", "Current-state doc must state M7 is verified candidate.");
        AssertContains(currentState, "M8 is current candidate work and is not verified until Codex verifies it", "Current-state doc must state M8 is current candidate pending Codex verification.");
        AssertContains(currentState, "Promotion is parked until full M-series completion", "Current-state doc must state promotion is parked until full M-series completion.");
        AssertContains(currentState, "M-series is not complete until M8 is verified", "Current-state doc must state M-series completion boundary.");
        AssertContains(currentState, "Promotion debt is `HIGH`", "Current-state doc must state current HIGH promotion debt.");
        AssertContains(currentState, "M9 is not started", "Current-state doc must state M9 is not started.");
        AssertContains(currentState, "LAN-M14 is not started", "Current-state doc must state LAN-M14 is not started.");
        AssertContains(currentState, "LAN completion series is not started", "Current-state doc must state LAN completion is not started.");
        AssertNotContains(currentState, "M8 is stable", "Current-state doc must not mark M8 stable.");
        AssertNotContains(currentState, "M9 is current candidate work", "Current-state doc must not start M9.");

        AssertContains(ledger, "M8 - M-Series Completion Criteria + LAN Transition Gate", "Ledger must reference M8 as current candidate work.");
        AssertContains(ledger, "M8 is current candidate work and is not verified until Codex verifies it", "Ledger must mark M8 pending Codex verification.");
        AssertContains(ledger, "M9 is not started", "Ledger must state M9 is not started.");
        AssertContains(ledger, "LAN-M14 is not started", "Ledger must state LAN-M14 is not started.");
        AssertContains(ledger, "LAN completion series is not started", "Ledger must state LAN completion is not started.");
        AssertContains(ledger, "Promotion only happens after full M-series completion", "Ledger must state promotion only after M-series completion.");
        AssertContains(ledger, "LAN completion only starts after M-series promotion", "Ledger must state LAN completion only after promotion.");
        AssertContains(ledger, "Do not use LAN docs or LAN plans before the LAN transition gate passes", "Ledger must reject LAN docs/plans before transition gate.");

        AssertContains(debtMeter, "Current stack after M7 / before or during M8: `HIGH`", "Debt meter must acknowledge HIGH stack after M7 / before or during M8.");
        AssertContains(debtMeter, "HIGH does not force promotion because Kirch explicitly parked promotion until M-series completion", "Debt meter must explain why HIGH does not force promotion.");
        AssertContains(debtMeter, "HIGH blocks feature work and LAN work", "Debt meter must block feature and LAN work at HIGH.");
        AssertContains(debtMeter, "HIGH permits only narrow M-series completion/closure discipline work", "Debt meter must allow only narrow M-series discipline work.");

        AssertContains(dryRunChecklist, "Final promotion dry-run must check `M_SERIES_COMPLETION_CRITERIA_AND_LAN_TRANSITION_GATE.md`", "Dry-run checklist must reference M-series completion criteria.");
        AssertContains(dryRunChecklist, "LAN transition cannot begin before promoted M-series baseline exists", "Dry-run checklist must block LAN transition before promoted baseline.");
        AssertContains(dryRunChecklist, "Stale LAN assumptions must be rejected", "Dry-run checklist must reject stale LAN assumptions.");

        AssertContains(coreSmoke, "RoleM4PermissionBoundaryRegressionMatrixChecks.RunAsync().GetAwaiter().GetResult();", "M4 checks must remain wired in CoreSmoke.");
        AssertContains(coreSmoke, "M5VerifiedCandidateStackLedgerChecks.Run();", "M5 checks must remain wired in CoreSmoke.");
        AssertContains(coreSmoke, "M6CandidateStackPromotionDryRunChecks.Run();", "M6 checks must remain wired in CoreSmoke.");
        AssertContains(coreSmoke, "M7CandidateStackPromotionDebtMeterChecks.Run();", "M7 checks must remain wired in CoreSmoke.");
        AssertContains(coreSmoke, "M8MSeriesCompletionLanTransitionGateChecks.Run();", "M8 checks must be explicitly wired in CoreSmoke.");

        AssertM8ChangedFilesStayInScope(projectRoot, currentState);
    }

    private static void AssertM8ChangedFilesStayInScope(string projectRoot, string currentState)
    {
        if (IsPostMSeriesPromotionCurrentState(currentState))
        {
            Console.WriteLine("M8 changed-file scope guard skipped: M8 has been promoted. Static M-series gate checks remain active.");
            return;
        }

        if (!currentState.Contains("M8 is current candidate work", StringComparison.Ordinal))
        {
            Console.WriteLine("M8 changed-file scope guard skipped: M8 is no longer current candidate work. Static M-series gate checks remain active.");
            return;
        }

        string[] changedPaths = GetM8ChangedPaths(projectRoot);

        if (changedPaths.Length == 0)
        {
            Console.WriteLine("M8 changed-file scope guard skipped: no branch-diff paths were available. Static M-series gate checks remain active.");
            return;
        }

        HashSet<string> allowedPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "00-HANDOFF/M_SERIES_COMPLETION_CRITERIA_AND_LAN_TRANSITION_GATE.md",
            "00-HANDOFF/CURRENT_STABLE_STATE_AND_NEXT_WORK.md",
            "00-HANDOFF/VERIFIED_CANDIDATE_STACK_LEDGER.md",
            "00-HANDOFF/CANDIDATE_STACK_PROMOTION_DEBT_METER.md",
            "00-HANDOFF/CANDIDATE_STACK_PROMOTION_DRY_RUN_CHECKLIST.md",
            "tests/Salryn.Tests/M5VerifiedCandidateStackLedgerChecks.cs",
            "tests/Salryn.Tests/M6CandidateStackPromotionDryRunChecks.cs",
            "tests/Salryn.Tests/M7CandidateStackPromotionDebtMeterChecks.cs",
            "tests/Salryn.Tests/M8MSeriesCompletionLanTransitionGateChecks.cs",
            "tools/Salryn.Tools.CoreSmoke/Program.cs"
        };

        foreach (string changedPath in changedPaths)
        {
            string normalizedPath = changedPath.Replace('\\', '/');

            if (!allowedPaths.Contains(normalizedPath))
            {
                throw new InvalidOperationException($"M8 changed an out-of-scope file: {normalizedPath}");
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
                || lowerPath.Contains("permissioneditor", StringComparison.Ordinal)
                || lowerPath.Contains("lan-m14", StringComparison.Ordinal)
                || lowerPath.Contains("lanm14", StringComparison.Ordinal)
                || lowerPath.Contains("lancompletion", StringComparison.Ordinal)
                || lowerPath.Contains("lan-completion", StringComparison.Ordinal)
                || lowerPath.StartsWith("src/salryn.api.local/", StringComparison.Ordinal)
                || lowerPath.StartsWith("src/salryn.app.web/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"M8 changed a forbidden boundary file: {normalizedPath}");
            }
        }
    }

    private static string[] GetM8ChangedPaths(string projectRoot)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddPaths(paths, TryRunGit(projectRoot, "diff --name-only " + M8BaselineSha + "...HEAD"));
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

    private static bool IsCurrentLanM16QaBState(string currentState)
    {
        return currentState.Contains("LAN-M16-QA-B", StringComparison.Ordinal)
            && currentState.Contains("Historical M-series and ROLE docs remain context only and must not override current source truth.", StringComparison.Ordinal)
            && currentState.Contains("Current live coordination authority:", StringComparison.Ordinal);
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

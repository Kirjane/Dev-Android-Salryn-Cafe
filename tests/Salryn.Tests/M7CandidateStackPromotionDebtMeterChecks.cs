using System.Diagnostics;

namespace Salryn.Tests;

public static class M7CandidateStackPromotionDebtMeterChecks
{
    private const string StableSandboxSha = "0ea9790d5c86bba350c9db800c101195fa658c0c";
    private const string M7BaselineSha = "daccf2970c4773c8be6314febeb246bc41ccb67a";

    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string handoffRoot = Path.Combine(projectRoot, "00-HANDOFF");
        string debtMeterPath = Path.Combine(handoffRoot, "CANDIDATE_STACK_PROMOTION_DEBT_METER.md");
        string ledgerPath = Path.Combine(handoffRoot, "VERIFIED_CANDIDATE_STACK_LEDGER.md");
        string currentStatePath = Path.Combine(handoffRoot, "CURRENT_STABLE_STATE_AND_NEXT_WORK.md");
        string dryRunPath = Path.Combine(handoffRoot, "CANDIDATE_STACK_PROMOTION_DRY_RUN_CHECKLIST.md");
        string coreSmokePath = Path.Combine(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        string debtMeter = ReadRequiredFile(debtMeterPath);
        string ledger = ReadRequiredFile(ledgerPath);
        string currentState = ReadRequiredFile(currentStatePath);
        string dryRunChecklist = ReadRequiredFile(dryRunPath);
        string coreSmoke = ReadRequiredFile(coreSmokePath);

        if (IsCurrentLanM16QaBState(currentState))
        {
            AssertContains(currentState, "Stable as of `e477699dca27a21954a5ad9d388307fdb9ed08fd`", "Current-state doc must preserve the current stable Sandbox SHA.");
            AssertContains(currentState, "LAN-M16-QA-B", "Current-state doc must mark LAN-M16-QA-B as the current candidate work.");
            AssertContains(currentState, "Current live coordination authority:", "Current-state doc must keep live coordination authority visible.");
            AssertContains(currentState, "Historical M-series and ROLE docs remain context only and must not override current source truth.", "Current-state doc must keep older M-series docs demoted to historical context.");

            AssertContains(debtMeter, "### HIGH", "Debt meter must remain available as historical promotion-debt context.");
            AssertContains(coreSmoke, "M7CandidateStackPromotionDebtMeterChecks.Run();", "M7 checks must remain explicitly wired in CoreSmoke.");
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
            AssertContains(debtMeter, "### HIGH", "Debt meter must remain available as historical promotion-debt context.");
            AssertContains(coreSmoke, "M7CandidateStackPromotionDebtMeterChecks.Run();", "M7 checks must remain explicitly wired in CoreSmoke.");
            AssertM7ChangedFilesStayInScope(projectRoot, currentState);
            return;
        }

        AssertContains(debtMeter, StableSandboxSha, "Debt meter must contain exact stable Sandbox SHA.");
        AssertContains(debtMeter, M7BaselineSha, "Debt meter must contain exact Freehand SHA before M7.");
        AssertContains(debtMeter, "Promotion status: parked", "Debt meter must state promotion is parked.");
        AssertContains(debtMeter, "### LOW", "Debt meter must define LOW classification.");
        AssertContains(debtMeter, "### MODERATE", "Debt meter must define MODERATE classification.");
        AssertContains(debtMeter, "### HIGH", "Debt meter must define HIGH classification.");
        AssertContains(debtMeter, "### STOP / REVIEW", "Debt meter must define STOP / REVIEW classification.");
        bool isM8Current = currentState.Contains("M8 is current candidate work", StringComparison.Ordinal);
        if (isM8Current)
        {
            AssertContains(debtMeter, "Current stack after M7 / before or during M8: `HIGH`", "Debt meter must classify M8-era stack as HIGH.");
            AssertContains(debtMeter, "M8 is current candidate work and unverified until Codex verifies it", "Debt meter must mark M8 unverified until Codex verifies.");
            AssertContains(debtMeter, "M9 is not started", "Debt meter must state M9 is not started when M8 is current.");
        }
        else
        {
            AssertContains(debtMeter, "Current state before M7: `MODERATE`", "Debt meter must classify pre-M7 state as MODERATE.");
            AssertContains(debtMeter, "M7 is current candidate work and unverified until Codex verifies it", "Debt meter must mark M7 unverified until Codex verifies.");
            AssertContains(debtMeter, "M8 is not started", "Debt meter must state M8 is not started.");
        }
        AssertContains(debtMeter, "LAN-M14 is not started", "Debt meter must state LAN-M14 is not started.");
        AssertContains(debtMeter, "Promotion requires explicit Kirch command", "Debt meter must state promotion requires explicit Kirch command.");
        AssertContains(debtMeter, isM8Current ? "M8 must not force promotion" : "M7 must not force promotion", "Debt meter must prohibit auto-promotion behavior.");

        AssertContains(ledger, "## Candidate Stack Limit Policy", "Ledger must contain Candidate Stack Limit Policy.");
        AssertContains(ledger, "Do not stack unverified candidate on top of unverified candidate", "Ledger must prohibit stacking unverified on unverified.");
        AssertContains(ledger, "If ahead count enters HIGH, produce promotion planning or explicit stop/review before further feature work", "Ledger must require planning/review when HIGH.");
        AssertContains(ledger, "If STOP / REVIEW condition appears, stop sprinting and report", "Ledger must require stop/report for STOP / REVIEW conditions.");
        AssertContains(ledger, "Promotion remains parked unless Kirch explicitly says promote", "Ledger must keep promotion parked.");
        AssertContains(ledger, "Sandbox remains stable branch and must not be touched", "Ledger must protect Sandbox.");
        AssertContains(ledger, "M7 - candidate stack limit guard and promotion debt meter", "Ledger must record M7 candidate work.");
        if (isM8Current)
        {
            AssertContains(ledger, "M8 is current candidate work and is not verified until Codex verifies it", "Ledger must mark M8 unverified when M8 is current.");
            AssertContains(ledger, "M9 is not started", "Ledger must state M9 is not started when M8 is current.");
        }
        else
        {
            AssertContains(ledger, "M7 is current candidate work and is not verified until Codex verifies it", "Ledger must mark M7 unverified.");
            AssertContains(ledger, "M8 is not started", "Ledger must state M8 is not started.");
        }
        AssertContains(ledger, "LAN-M14 is not started", "Ledger must state LAN-M14 is not started.");

        AssertContains(currentState, "ROLE-M4 is verified candidate work", "Current-state doc must state ROLE-M4 is verified candidate.");
        AssertContains(currentState, "M5 is verified candidate work", "Current-state doc must state M5 is verified candidate.");
        AssertContains(currentState, "M6 is verified candidate work", "Current-state doc must state M6 is verified candidate.");
        if (isM8Current)
        {
            AssertContains(currentState, "M7 is verified candidate work", "Current-state doc must state M7 is verified candidate when M8 is current.");
            AssertContains(currentState, "M8 is current candidate work and is not verified until Codex verifies it", "Current-state doc must state M8 is current candidate pending Codex verification.");
            AssertContains(currentState, "Promotion debt is `HIGH`", "Current-state doc must state HIGH promotion debt classification when M8 is current.");
        }
        else
        {
            AssertContains(currentState, "M7 is current candidate work and is not verified until Codex verifies it", "Current-state doc must state M7 is current candidate pending Codex verification.");
            AssertContains(currentState, "Promotion debt is currently `MODERATE` before M7", "Current-state doc must state current promotion debt classification.");
        }
        AssertContains(currentState, "CANDIDATE_STACK_PROMOTION_DEBT_METER.md", "Current-state doc must link debt meter as live coordination authority.");
        AssertContains(currentState, isM8Current ? "M9 is not started" : "M8 is not started", "Current-state doc must state the next M-series item is not started.");
        AssertContains(currentState, "LAN-M14 is not started", "Current-state doc must state LAN-M14 is not started.");
        AssertNotContains(currentState, "M7 is stable", "Current-state doc must not mark M7 stable.");
        if (!isM8Current)
        {
            AssertNotContains(currentState, "M8 is current candidate work", "Current-state doc must not start M8.");
        }

        AssertContains(dryRunChecklist, "Future promotion planning should review `CANDIDATE_STACK_PROMOTION_DEBT_METER.md`", "Dry-run checklist must reference the promotion debt meter.");
        AssertContains(dryRunChecklist, "HIGH or STOP / REVIEW conditions must be resolved before feature sprinting continues", "Dry-run checklist must require resolving HIGH or STOP / REVIEW before feature sprinting.");
        AssertContains(dryRunChecklist, "Promotion still requires explicit Kirch command", "Dry-run checklist must keep explicit promotion command requirement.");

        AssertContains(coreSmoke, "RoleM4PermissionBoundaryRegressionMatrixChecks.RunAsync().GetAwaiter().GetResult();", "M4 checks must remain wired in CoreSmoke.");
        AssertContains(coreSmoke, "M5VerifiedCandidateStackLedgerChecks.Run();", "M5 checks must remain wired in CoreSmoke.");
        AssertContains(coreSmoke, "M6CandidateStackPromotionDryRunChecks.Run();", "M6 checks must remain wired in CoreSmoke.");
        AssertContains(coreSmoke, "M7CandidateStackPromotionDebtMeterChecks.Run();", "M7 checks must be explicitly wired in CoreSmoke.");

        AssertM7ChangedFilesStayInScope(projectRoot, currentState);
    }

    private static void AssertM7ChangedFilesStayInScope(string projectRoot, string currentState)
    {
        if (IsPostMSeriesPromotionCurrentState(currentState))
        {
            Console.WriteLine("M7 changed-file scope guard skipped: M7 has been promoted. Static debt-meter checks remain active.");
            return;
        }

        if (!currentState.Contains("M7 is current candidate work", StringComparison.Ordinal))
        {
            Console.WriteLine("M7 changed-file scope guard skipped: M7 is no longer current candidate work. Static debt-meter checks remain active.");
            return;
        }

        string[] changedPaths = GetM7ChangedPaths(projectRoot);

        if (changedPaths.Length == 0)
        {
            Console.WriteLine("M7 changed-file scope guard skipped: no branch-diff paths were available. Static debt-meter checks remain active.");
            return;
        }

        HashSet<string> allowedPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "00-HANDOFF/CANDIDATE_STACK_PROMOTION_DEBT_METER.md",
            "00-HANDOFF/VERIFIED_CANDIDATE_STACK_LEDGER.md",
            "00-HANDOFF/CURRENT_STABLE_STATE_AND_NEXT_WORK.md",
            "00-HANDOFF/CANDIDATE_STACK_PROMOTION_DRY_RUN_CHECKLIST.md",
            "tests/Salryn.Tests/M5VerifiedCandidateStackLedgerChecks.cs",
            "tests/Salryn.Tests/M6CandidateStackPromotionDryRunChecks.cs",
            "tests/Salryn.Tests/M7CandidateStackPromotionDebtMeterChecks.cs",
            "tools/Salryn.Tools.CoreSmoke/Program.cs"
        };

        foreach (string changedPath in changedPaths)
        {
            string normalizedPath = changedPath.Replace('\\', '/');

            if (!allowedPaths.Contains(normalizedPath))
            {
                throw new InvalidOperationException($"M7 changed an out-of-scope file: {normalizedPath}");
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
                || lowerPath.StartsWith("src/salryn.api.local/", StringComparison.Ordinal)
                || lowerPath.StartsWith("src/salryn.app.web/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"M7 changed a forbidden boundary file: {normalizedPath}");
            }
        }
    }

    private static string[] GetM7ChangedPaths(string projectRoot)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddPaths(paths, TryRunGit(projectRoot, "diff --name-only " + M7BaselineSha + "...HEAD"));
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

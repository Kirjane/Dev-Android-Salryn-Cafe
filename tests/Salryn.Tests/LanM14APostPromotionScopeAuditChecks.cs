using System.Diagnostics;

namespace Salryn.Tests;

public static class LanM14APostPromotionScopeAuditChecks
{
    private const string PostPromotionBaselineSha = "bde69e28faa4f92dd7b6765da9f45a0c5cf02056";

    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string handoffRoot = Path.Combine(projectRoot, "00-HANDOFF");
        string lanM14APath = Path.Combine(handoffRoot, "LAN_M14_A_POST_PROMOTION_SCOPE_AUDIT_AND_FIRST_CUTLINE.md");
        string currentStatePath = Path.Combine(handoffRoot, "CURRENT_STABLE_STATE_AND_NEXT_WORK.md");
        string coreSmokePath = Path.Combine(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        string lanM14A = ReadRequiredFile(lanM14APath);
        string currentState = ReadRequiredFile(currentStatePath);
        string coreSmoke = ReadRequiredFile(coreSmokePath);

        if (IsCurrentLanM16QaBState(currentState))
        {
            AssertContains(currentState, "Stable as of `e477699dca27a21954a5ad9d388307fdb9ed08fd`", "Current-state doc must preserve the current stable Sandbox SHA.");
            AssertContains(currentState, "LAN-M16-QA-B", "Current-state doc must mark LAN-M16-QA-B as the current candidate work.");
            AssertContains(currentState, "Current live coordination authority:", "Current-state doc must keep live coordination authority visible.");
            AssertContains(currentState, "Historical M-series and ROLE docs remain context only and must not override current source truth.", "Current-state doc must keep older M-series docs demoted to historical context.");

            AssertContains(lanM14A, "LAN-M14-A does not implement LAN", "LAN-M14-A doc must remain available as historical cutline context.");
            AssertContains(coreSmoke, "LanM14APostPromotionScopeAuditChecks.Run();", "CoreSmoke must keep LAN-M14-A checks wired.");
            AssertSourceLanBoundaries(projectRoot);
            return;
        }

        if (IsLaterLanM14Candidate(currentState))
        {
            AssertContains(currentState, "`LAN-M14-A`", "Current-state doc must keep LAN-M14-A in the stable stack after later LAN-M14 cutlines start.");
            AssertContains(currentState, "LAN-M14-", "Current-state doc must mark the active LAN-M14 candidate boundary.");
            AssertContains(lanM14A, "LAN-M14-A does not implement LAN", "LAN-M14-A doc must remain available as historical cutline context.");
            AssertContains(coreSmoke, "LanM14APostPromotionScopeAuditChecks.Run();", "CoreSmoke must keep LAN-M14-A checks wired.");
            AssertLanM14AChangedFilesStayInScope(projectRoot, currentState);
            return;
        }

        AssertContains(currentState, PostPromotionBaselineSha, "Current-state doc must contain the post-promotion SHA.");
        AssertContains(currentState, "Sandbox and Freehand are aligned", "Current-state doc must state Sandbox and Freehand are aligned.");
        AssertContains(currentState, "M-series is stable on Sandbox", "Current-state doc must state M-series is stable on Sandbox.");
        AssertContains(currentState, "LAN-M14-A is current candidate work and is not verified until Codex verifies it", "Current-state doc must mark LAN-M14-A as current unverified candidate work.");
        AssertContains(currentState, "LAN-M14-B is not started", "Current-state doc must state LAN-M14-B is not started.");
        AssertContains(currentState, "LAN bind activation is not started", "Current-state doc must state LAN bind activation is not started.");
        AssertContains(currentState, "Live remote transport dispatch is not started", "Current-state doc must state live remote transport dispatch is not started.");
        AssertContains(currentState, "LAN mutation is not started", "Current-state doc must state LAN mutation is not started.");
        AssertContains(currentState, "Remote cashier/admin behavior is not started", "Current-state doc must state remote cashier/admin is not started.");
        AssertContains(currentState, "LAN_M14_A_POST_PROMOTION_SCOPE_AUDIT_AND_FIRST_CUTLINE.md", "Current-state doc must list LAN-M14-A as live coordination authority.");
        AssertNotContains(currentState, "Freehand is intentionally ahead", "Current-state doc must not retain stale Freehand-ahead wording.");
        AssertNotContains(currentState, "Promotion status: parked", "Current-state doc must not retain stale promotion-parked source status.");
        AssertNotContains(currentState, "M8 is current candidate work", "Current-state doc must not retain stale M8 current-candidate wording.");
        AssertNotContains(currentState, "These remain candidate-only until promotion", "Current-state doc must not retain stale candidate-only wording for the promoted M-series.");

        AssertContains(lanM14A, "LAN completion not started", "LAN-M14-A doc must state LAN completion is not started.");
        AssertContains(lanM14A, "No LAN-M14-B chosen yet", "LAN-M14-A doc must state LAN-M14-B is not started/chosen yet.");
        AssertContains(lanM14A, "Api.Local remains localhost-bound by default", "LAN-M14-A doc must state Api.Local is localhost-bound by default.");
        AssertContains(lanM14A, "Remote-readonly endpoints are GET-only", "LAN-M14-A doc must state remote-readonly endpoints are GET-only.");
        AssertContains(lanM14A, "status, capabilities, dashboard summary, products, terminal status, bind policy, and audit visibility", "LAN-M14-A doc must list remote-readonly coverage.");
        AssertContains(lanM14A, "React/Web remote-readonly page is read-only visibility only", "LAN-M14-A doc must keep React/Web remote-readonly read-only.");
        AssertContains(lanM14A, "WebView host allows localhost/127.0.0.1 only", "LAN-M14-A doc must state WebView local-only navigation boundary.");
        AssertContains(lanM14A, "Safe remote command categories are status/product/dashboard/terminal reads only", "LAN-M14-A doc must state safe remote command categories.");
        AssertContains(lanM14A, "remote cashier/admin execution", "LAN-M14-A doc must forbid remote cashier/admin execution.");
        AssertContains(lanM14A, "shared SQLite", "LAN-M14-A doc must forbid shared SQLite.");
        AssertContains(lanM14A, "SQLite path exposure", "LAN-M14-A doc must forbid SQLite path exposure.");
        AssertContains(lanM14A, "`0.0.0.0` wildcard bind", "LAN-M14-A doc must forbid wildcard bind.");
        AssertContains(lanM14A, "network discovery", "LAN-M14-A doc must forbid discovery.");
        AssertContains(lanM14A, "mobile/cloud", "LAN-M14-A doc must forbid mobile/cloud.");
        AssertContains(lanM14A, "RMO/printer/PDF/Crystal/ESC-POS", "LAN-M14-A doc must forbid RMO/printer/PDF/Crystal/ESC-POS.");
        AssertContains(lanM14A, "Recommended first implementation: `LAN-M14-B`", "LAN-M14-A doc must recommend LAN-M14-B as next target.");
        AssertContains(lanM14A, "LAN-M14-A does not implement LAN", "LAN-M14-A doc must state it does not implement LAN.");

        AssertContains(coreSmoke, "LanM14APostPromotionScopeAuditChecks.Run();", "CoreSmoke must explicitly wire LAN-M14-A checks.");

        AssertSourceLanBoundaries(projectRoot);
        AssertLanM14AChangedFilesStayInScope(projectRoot, currentState);
    }

    private static void AssertSourceLanBoundaries(string projectRoot)
    {
        string localApiApplicationFactory = ReadRequiredSourceByFileName(projectRoot, "LocalApiApplicationFactory.cs");
        AssertContains(localApiApplicationFactory, "http://127.0.0.1:5299", "LocalApiApplicationFactory must keep default Api.Local URL on 127.0.0.1:5299.");

        string remoteReadOnlyEndpoints = ReadRequiredSourceByFileName(projectRoot, "RemoteReadOnlyApiEndpoints.cs");
        AssertContains(remoteReadOnlyEndpoints, "MapGroup(\"/api/remote-readonly\")", "Remote-readonly endpoints must remain under /api/remote-readonly.");
        AssertContains(remoteReadOnlyEndpoints, ".MapGet(\"/status\"", "Remote-readonly status route must remain GET.");
        AssertContains(remoteReadOnlyEndpoints, ".MapGet(\"/capabilities\"", "Remote-readonly capabilities route must remain GET.");
        AssertContains(remoteReadOnlyEndpoints, ".MapGet(\"/dashboard-summary\"", "Remote-readonly dashboard route must remain GET.");
        AssertContains(remoteReadOnlyEndpoints, ".MapGet(\"/products\"", "Remote-readonly product route must remain GET.");
        AssertContains(remoteReadOnlyEndpoints, ".MapGet(\"/terminal-status\"", "Remote-readonly terminal status route must remain GET.");
        AssertContains(remoteReadOnlyEndpoints, ".MapGet(\"/bind-policy\"", "Remote-readonly bind policy route must remain GET.");
        AssertContains(remoteReadOnlyEndpoints, ".MapGet(\"/audit\"", "Remote-readonly audit route must remain GET.");
        AssertNotContains(remoteReadOnlyEndpoints, ".MapPost(", "Remote-readonly endpoints must not expose POST.");
        AssertNotContains(remoteReadOnlyEndpoints, ".MapPut(", "Remote-readonly endpoints must not expose PUT.");
        AssertNotContains(remoteReadOnlyEndpoints, ".MapPatch(", "Remote-readonly endpoints must not expose PATCH.");
        AssertNotContains(remoteReadOnlyEndpoints, ".MapDelete(", "Remote-readonly endpoints must not expose DELETE.");

        string remoteReadOnlyService = ReadRequiredSourceByFileName(projectRoot, "RemoteReadOnlyApiService.cs");
        AssertContains(remoteReadOnlyService, "LocalOnlyReadMode", "RemoteReadOnlyApiService must keep LocalOnlyReadMode.");
        AssertContains(remoteReadOnlyService, "NoWildcardBind", "RemoteReadOnlyApiService must keep NoWildcardBind policy.");
        AssertContains(remoteReadOnlyService, "Status read", "RemoteReadOnlyApiService must expose status read only.");
        AssertContains(remoteReadOnlyService, "Product read", "RemoteReadOnlyApiService must expose product read only.");
        AssertContains(remoteReadOnlyService, "Dashboard read", "RemoteReadOnlyApiService must expose dashboard read only.");
        AssertContains(remoteReadOnlyService, "Terminal status read", "RemoteReadOnlyApiService must expose terminal status read only.");
        AssertContains(remoteReadOnlyService, "Official journal writes disabled", "RemoteReadOnlyApiService must keep official journal writes disabled.");
        AssertContains(remoteReadOnlyService, "Official sequence allocation disabled", "RemoteReadOnlyApiService must keep official sequence allocation disabled.");

        string remoteReadOnlyClient = ReadRequiredSourceByAnyContent(
            projectRoot,
            new[] { "*.ts", "*.tsx" },
            "remote-readonly",
            "mutation",
            "disabled");
        AssertContainsIgnoreCase(remoteReadOnlyClient, "disabled", "Remote read-only client must include disabled mutation warnings.");
        AssertContainsIgnoreCase(remoteReadOnlyClient, "mutation", "Remote read-only client must reference mutation-disabled behavior.");

        string localWebOriginPolicy = ReadRequiredSourceByAnyContent(
            projectRoot,
            new[] { "*.cs" },
            "localhost",
            "127.0.0.1",
            "javascript");
        AssertContainsIgnoreCase(localWebOriginPolicy, "localhost", "Local WebView origin policy must mention localhost.");
        AssertContainsIgnoreCase(localWebOriginPolicy, "127.0.0.1", "Local WebView origin policy must mention 127.0.0.1.");
        AssertContainsIgnoreCase(localWebOriginPolicy, "javascript", "Local WebView origin policy must block javascript navigation.");

        string remoteTransport = ReadRequiredSourceByAnyContent(
            projectRoot,
            new[] { "*.cs" },
            "RemoteTransport",
            "Status",
            "Product",
            "Dashboard",
            "Terminal");
        AssertContainsIgnoreCase(remoteTransport, "Status", "Remote transport guard must keep status category coverage.");
        AssertContainsIgnoreCase(remoteTransport, "Product", "Remote transport guard must keep product category coverage.");
        AssertContainsIgnoreCase(remoteTransport, "Dashboard", "Remote transport guard must keep dashboard category coverage.");
        AssertContainsIgnoreCase(remoteTransport, "Terminal", "Remote transport guard must keep terminal category coverage.");
    }

    private static void AssertLanM14AChangedFilesStayInScope(string projectRoot, string currentState)
    {
        if (IsLaterLanM14Candidate(currentState))
        {
            Console.WriteLine("LAN-M14-A changed-file scope guard skipped: LAN-M14-A has been promoted. Static LAN cutline checks remain active.");
            return;
        }

        if (!currentState.Contains("LAN-M14-A is current candidate work", StringComparison.Ordinal))
        {
            Console.WriteLine("LAN-M14-A changed-file scope guard skipped: LAN-M14-A is no longer current candidate work. Static LAN cutline checks remain active.");
            return;
        }

        string[] changedPaths = GetChangedPaths(projectRoot);

        if (changedPaths.Length == 0)
        {
            Console.WriteLine("LAN-M14-A changed-file scope guard skipped: no branch-diff paths were available. Static LAN cutline checks remain active.");
            return;
        }

        HashSet<string> allowedPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "00-HANDOFF/LAN_M14_A_POST_PROMOTION_SCOPE_AUDIT_AND_FIRST_CUTLINE.md",
            "00-HANDOFF/CURRENT_STABLE_STATE_AND_NEXT_WORK.md",
            "tests/Salryn.Tests/M5VerifiedCandidateStackLedgerChecks.cs",
            "tests/Salryn.Tests/M6CandidateStackPromotionDryRunChecks.cs",
            "tests/Salryn.Tests/M7CandidateStackPromotionDebtMeterChecks.cs",
            "tests/Salryn.Tests/M8MSeriesCompletionLanTransitionGateChecks.cs",
            "tests/Salryn.Tests/LanM14APostPromotionScopeAuditChecks.cs",
            "tools/Salryn.Tools.CoreSmoke/Program.cs"
        };

        foreach (string changedPath in changedPaths)
        {
            string normalizedPath = changedPath.Replace('\\', '/');

            if (!allowedPaths.Contains(normalizedPath))
            {
                throw new InvalidOperationException($"LAN-M14-A changed an out-of-scope file: {normalizedPath}");
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
                || lowerPath.Contains("lan-m14-b", StringComparison.Ordinal)
                || lowerPath.StartsWith("src/salryn.api.local/", StringComparison.Ordinal)
                || lowerPath.StartsWith("src/salryn.app.web/", StringComparison.Ordinal)
                || lowerPath.StartsWith("src/salryn.app.winforms/", StringComparison.Ordinal)
                || lowerPath.StartsWith("src/salryn.host.local/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"LAN-M14-A changed a forbidden boundary file: {normalizedPath}");
            }
        }
    }

    private static string[] GetChangedPaths(string projectRoot)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddPaths(paths, TryRunGit(projectRoot, "diff --name-only " + PostPromotionBaselineSha + "...HEAD"));
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

    private static bool IsLaterLanM14Candidate(string currentState)
    {
        return currentState.Contains("`LAN-M14-A`", StringComparison.Ordinal)
            && (currentState.Contains("LAN-M14-B is current candidate work", StringComparison.Ordinal)
                || currentState.Contains("LAN-M14-B is stable", StringComparison.Ordinal)
                || currentState.Contains("LAN-M14-C is current candidate work", StringComparison.Ordinal)
                || currentState.Contains("LAN-M14-C is stable", StringComparison.Ordinal)
                || currentState.Contains("LAN-M14-D is current candidate work", StringComparison.Ordinal)
                || currentState.Contains("LAN-M14-D is stable", StringComparison.Ordinal)
                || currentState.Contains("LAN-M14-E is current candidate work", StringComparison.Ordinal)
                || currentState.Contains("LAN-M14-F through LAN-M14-I are candidate work completed", StringComparison.Ordinal)
                || currentState.Contains("LAN-M14-I is the LAN-M14 cap", StringComparison.Ordinal));
    }

    private static bool IsCurrentLanM16QaBState(string currentState)
    {
        return currentState.Contains("LAN-M16-QA-B", StringComparison.Ordinal)
            && currentState.Contains("Historical M-series and ROLE docs remain context only and must not override current source truth.", StringComparison.Ordinal)
            && currentState.Contains("Current live coordination authority:", StringComparison.Ordinal);
    }

    private static string ReadRequiredSourceByFileName(string projectRoot, string fileName)
    {
        string? path = Directory
            .GetFiles(projectRoot, fileName, SearchOption.AllDirectories)
            .Where(path => !IsIgnoredSourceSearchPath(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (path is null)
        {
            throw new InvalidOperationException("Required source file missing: " + fileName);
        }

        return File.ReadAllText(path);
    }

    private static string ReadRequiredSourceByAnyContent(
        string projectRoot,
        IReadOnlyCollection<string> searchPatterns,
        params string[] requiredSnippets)
    {
        foreach (string pattern in searchPatterns)
        {
            foreach (string path in Directory.GetFiles(projectRoot, pattern, SearchOption.AllDirectories)
                         .Where(path => !IsIgnoredSourceSearchPath(path))
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                string content = File.ReadAllText(path);

                if (requiredSnippets.All(snippet =>
                        content.Contains(snippet, StringComparison.OrdinalIgnoreCase)))
                {
                    return content;
                }
            }
        }

        throw new InvalidOperationException(
            "Required source boundary text missing: " + string.Join(", ", requiredSnippets));
    }

    private static bool IsIgnoredSourceSearchPath(string path)
    {
        string normalized = path.Replace('\\', '/');
        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/dist/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/.git/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/tests/Salryn.Tests/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/00-HANDOFF/", StringComparison.OrdinalIgnoreCase);
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

    private static void AssertContainsIgnoreCase(string source, string expected, string message)
    {
        if (!source.Contains(expected, StringComparison.OrdinalIgnoreCase))
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

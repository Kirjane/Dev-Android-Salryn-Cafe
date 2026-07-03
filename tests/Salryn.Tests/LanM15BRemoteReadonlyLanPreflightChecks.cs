using System.Diagnostics;

namespace Salryn.Tests;

public static class LanM15BRemoteReadonlyLanPreflightChecks
{
    private const string LanM15BBaselineSha = "e477699dca27a21954a5ad9d388307fdb9ed08fd";

    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string handoffRoot = Path.Combine(projectRoot, "00-HANDOFF");
        string currentStatePath = Path.Combine(handoffRoot, "CURRENT_STABLE_STATE_AND_NEXT_WORK.md");
        string lanM15BPath = Path.Combine(handoffRoot, "LAN_M15_B_REMOTE_READONLY_LAN_PREFLIGHT_AND_OPERATOR_TROUBLESHOOTING.md");
        string coreSmokePath = Path.Combine(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string infraSmokePath = Path.Combine(projectRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "Program.cs");

        string currentState = ReadRequiredFile(currentStatePath);
        string lanM15B = ReadRequiredFile(lanM15BPath);
        string coreSmoke = ReadRequiredFile(coreSmokePath);
        string infraSmoke = ReadRequiredFile(infraSmokePath);

        AssertDocBoundaries(currentState, lanM15B, coreSmoke, infraSmoke, handoffRoot);
        AssertSourcePreflightBoundaries(projectRoot);
        AssertLanM15BChangedFilesStayInScope(projectRoot, currentState);
    }

    private static void AssertDocBoundaries(
        string currentState,
        string lanM15B,
        string coreSmoke,
        string infraSmoke,
        string handoffRoot)
    {
        AssertContains(currentState, LanM15BBaselineSha, "Current-state doc must contain the stable Sandbox SHA.");
        AssertContains(currentState, "LAN-M14-A through LAN-M14-E are stable", "Current-state doc must keep LAN-M14-A through LAN-M14-E stable.");
        AssertContains(currentState, "LAN-M14-F through LAN-M14-I remain completed but unpromoted on Freehand", "Current-state doc must mark LAN-M14-F through LAN-M14-I as unpromoted.");
        AssertContains(currentState, "LAN-M15-A remains completed but unpromoted on Freehand", "Current-state doc must mark LAN-M15-A as unpromoted.");
        AssertContains(currentState, "LAN-M15-B is current candidate work on Freehand", "Current-state doc must mark LAN-M15-B as current candidate work.");
        AssertContains(currentState, "LAN-M15-C is not started", "Current-state doc must state LAN-M15-C is not started.");
        AssertContains(currentState, "LAN-M14-J does not exist and must not be created", "Current-state doc must forbid LAN-M14-J.");
        AssertContains(currentState, "no Sandbox promotion has occurred", "Current-state doc must state no Sandbox promotion has occurred.");
        AssertNotContains(currentState, "LAN-M14-J is current", "Current-state doc must not mark LAN-M14-J as current work.");
        AssertNotContains(currentState, "LAN-M15-C is current", "Current-state doc must not mark LAN-M15-C as current work.");

        string docPath = Path.Combine(handoffRoot, "LAN_M15_B_REMOTE_READONLY_LAN_PREFLIGHT_AND_OPERATOR_TROUBLESHOOTING.md");
        if (!File.Exists(docPath))
        {
            throw new InvalidOperationException("Required LAN-M15-B doc missing.");
        }

        AssertContains(currentState, "LAN_M15_B_REMOTE_READONLY_LAN_PREFLIGHT_AND_OPERATOR_TROUBLESHOOTING.md", "Current-state doc must list LAN-M15-B doc as live authority.");
        AssertContains(lanM15B, LanM15BBaselineSha, "LAN-M15-B doc must contain the source stamp SHA.");
        AssertContains(lanM15B, "RemoteReadOnlyLanPreflightEvaluator", "LAN-M15-B doc must document RemoteReadOnlyLanPreflightEvaluator.");
        AssertContains(lanM15B, "/api/remote-readonly/preflight", "LAN-M15-B doc must document preflight endpoint.");
        AssertContains(lanM15B, "CookieTrustRejectedForRemoteLan", "LAN-M15-B doc must document cookie trust rejection.");
        AssertContains(lanM15B, "Forbidden Scope", "LAN-M15-B doc must define forbidden scope.");
        AssertContains(lanM15B, "Decision", "LAN-M15-B doc must include a decision section.");
        AssertNotContains(lanM15B, "LAN-M15-C is current", "LAN-M15-B doc must not mark LAN-M15-C as current work.");

        AssertContains(coreSmoke, "LanM15BRemoteReadonlyLanPreflightChecks.Run();", "CoreSmoke must wire LAN-M15-B checks.");
        AssertContains(infraSmoke, "remote-readonly-lan-preflight", "InfrastructureSmoke must wire remote-readonly-lan-preflight command.");
        AssertContains(infraSmoke, "LanM15RemoteReadOnlyLanPreflightSmokeChecks.Run", "InfrastructureSmoke must call LAN-M15-B preflight smoke.");
    }

    private static void AssertSourcePreflightBoundaries(string projectRoot)
    {
        string apiRoot = Path.Combine(projectRoot, "src", "Salryn.Api.Local");
        string webRoot = Path.Combine(projectRoot, "src", "Salryn.App.Web", "src");

        string evaluator = ReadRequiredFile(apiRoot, "Services", "RemoteReadOnlyLanPreflightEvaluator.cs");
        string remoteReadOnlyEndpoints = ReadRequiredFile(apiRoot, "RemoteReadOnlyApiEndpoints.cs");
        string remoteReadOnlyPage = ReadRequiredFile(webRoot, "pages", "RemoteReadOnlyClientPage.tsx");
        string smoke = ReadRequiredFile(projectRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "LanM15RemoteReadOnlyLanPreflightSmokeChecks.cs");

        AssertContains(evaluator, "RemoteReadOnlyLanPreflightEvaluator", "Preflight evaluator must exist.");
        AssertContains(evaluator, "CookieTrustRejectedForRemoteLan", "Preflight evaluator must report cookie trust rejection.");
        AssertContains(evaluator, "MutationCheckoutAdminForbidden", "Preflight evaluator must report mutation/checkout/admin forbidden.");
        AssertContains(evaluator, "PayloadContainsSecretMarker", "Preflight evaluator must expose secret marker detection.");

        AssertContains(remoteReadOnlyEndpoints, "/preflight", "Remote-readonly endpoints must expose preflight route.");
        AssertNotContains(remoteReadOnlyEndpoints, ".MapPost(", "Remote-readonly endpoints must remain GET-only.");
        AssertContains(remoteReadOnlyEndpoints, "/diagnostics", "Remote-readonly endpoints must keep diagnostics route.");

        AssertContains(remoteReadOnlyPage, "Preflight / Troubleshooting", "Remote read-only page must expose preflight troubleshooting section.");
        AssertContains(remoteReadOnlyPage, "Safe next steps", "Remote read-only page must show safe next steps.");
        AssertContains(remoteReadOnlyPage, "No catalog mutation controls are present", "Remote read-only page must keep mutation controls absent.");

        AssertContains(smoke, "RemoteReadOnlyLanPreflightEvaluator", "LAN-M15-B smoke must evaluate preflight.");
        AssertContains(smoke, "PayloadContainsSecretMarker", "LAN-M15-B smoke must verify secret marker redaction.");
        AssertContains(smoke, "CookieTrustRejectedForRemoteLan", "LAN-M15-B smoke must verify cookie trust rejection reporting.");
    }

    private static void AssertLanM15BChangedFilesStayInScope(string projectRoot, string currentState)
    {
        if (!currentState.Contains("LAN-M15-B is current candidate work on Freehand", StringComparison.Ordinal))
        {
            Console.WriteLine("LAN-M15-B changed-file scope guard skipped: LAN-M15-B is no longer current candidate work. Static preflight checks remain active.");
            return;
        }

        string[] changedPaths = GetChangedPaths(projectRoot);

        if (changedPaths.Length == 0)
        {
            Console.WriteLine("LAN-M15-B changed-file scope guard skipped: no branch-diff paths were available. Static preflight checks remain active.");
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
                || lowerPath.Contains("lan-m15-c", StringComparison.Ordinal)
                || lowerPath.Contains("discovery", StringComparison.Ordinal)
                || lowerPath.Contains("mobile", StringComparison.Ordinal)
                || lowerPath.Contains("cloud", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"LAN-M15-B changed a forbidden boundary file: {normalizedPath}");
            }

            throw new InvalidOperationException($"LAN-M15-B changed an out-of-scope file: {normalizedPath}");
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

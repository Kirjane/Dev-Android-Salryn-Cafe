using System.Diagnostics;

namespace Salryn.Tests;

public static class LanM15DSessionResilienceChecks
{
    private const string LanM15DBaselineSha = "e477699dca27a21954a5ad9d388307fdb9ed08fd";

    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string handoffRoot = Path.Combine(projectRoot, "00-HANDOFF");
        string currentStatePath = Path.Combine(handoffRoot, "CURRENT_STABLE_STATE_AND_NEXT_WORK.md");
        string lanM15DPath = Path.Combine(handoffRoot, "LAN_M15_D_REMOTE_CLIENT_RESILIENCE_AND_SESSION_LIFECYCLE_HARDENING.md");
        string coreSmokePath = Path.Combine(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string infraSmokePath = Path.Combine(projectRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "Program.cs");

        string currentState = ReadRequiredFile(currentStatePath);
        string lanM15D = ReadRequiredFile(lanM15DPath);
        string coreSmoke = ReadRequiredFile(coreSmokePath);
        string infraSmoke = ReadRequiredFile(infraSmokePath);

        if (IsCurrentLanM16QaBState(currentState))
        {
            AssertContains(currentState, "Stable as of `e477699dca27a21954a5ad9d388307fdb9ed08fd`", "Current-state doc must preserve the current stable Sandbox SHA.");
            AssertContains(currentState, "LAN-M16-QA-B", "Current-state doc must mark LAN-M16-QA-B as the current candidate work.");
            AssertContains(currentState, "Current live coordination authority:", "Current-state doc must keep live coordination authority visible.");
            AssertContains(currentState, "Historical M-series and ROLE docs remain context only and must not override current source truth.", "Current-state doc must keep older M-series docs demoted to historical context.");

            AssertContains(lanM15D, "RemoteReadOnlySessionLifecycle", "LAN-M15-D doc must remain available as historical session resilience context.");
            AssertContains(coreSmoke, "LanM15DSessionResilienceChecks.Run();", "CoreSmoke must keep LAN-M15-D checks wired.");
            AssertContains(infraSmoke, "remote-readonly-lan-session-resilience", "InfrastructureSmoke must keep LAN-M15-D coverage wired.");
            AssertSourceResilienceBoundaries(projectRoot);
            return;
        }

        AssertDocBoundaries(currentState, lanM15D, coreSmoke, infraSmoke, handoffRoot);
        AssertSourceResilienceBoundaries(projectRoot);
        AssertLanM15DChangedFilesStayInScope(projectRoot, currentState);
    }

    private static bool IsCurrentLanM16QaBState(string currentState)
    {
        return currentState.Contains("LAN-M16-QA-B", StringComparison.Ordinal)
            && currentState.Contains("Historical M-series and ROLE docs remain context only and must not override current source truth.", StringComparison.Ordinal)
            && currentState.Contains("Current live coordination authority:", StringComparison.Ordinal);
    }

    private static void AssertDocBoundaries(
        string currentState,
        string lanM15D,
        string coreSmoke,
        string infraSmoke,
        string handoffRoot)
    {
        AssertContains(currentState, LanM15DBaselineSha, "Current-state doc must contain the stable Sandbox SHA.");
        AssertContains(currentState, "LAN-M14-A through LAN-M14-E are stable", "Current-state doc must keep LAN-M14-A through LAN-M14-E stable.");
        AssertContains(currentState, "LAN-M14-F through LAN-M14-I remain completed but unpromoted on Freehand", "Current-state doc must mark LAN-M14-F through LAN-M14-I as unpromoted.");
        AssertContains(currentState, "LAN-M15-D remains completed but unpromoted on Freehand", "Current-state doc must mark LAN-M15-D as unpromoted candidate work.");
        AssertContains(currentState, "LAN-M15-F is the LAN completion cap", "Current-state doc must name LAN-M15-F as the LAN completion cap.");
        AssertContains(currentState, "no Sandbox promotion has occurred", "Current-state doc must state no Sandbox promotion has occurred.");
        AssertNotContains(currentState, "LAN-M14-J is current", "Current-state doc must not mark LAN-M14-J as current work.");

        string docPath = Path.Combine(handoffRoot, "LAN_M15_D_REMOTE_CLIENT_RESILIENCE_AND_SESSION_LIFECYCLE_HARDENING.md");
        if (!File.Exists(docPath))
        {
            throw new InvalidOperationException("Required LAN-M15-D doc missing.");
        }

        AssertContains(currentState, "LAN_M15_D_REMOTE_CLIENT_RESILIENCE_AND_SESSION_LIFECYCLE_HARDENING.md", "Current-state doc must list LAN-M15-D doc as live authority.");
        AssertContains(lanM15D, LanM15DBaselineSha, "LAN-M15-D doc must contain the source stamp SHA.");
        AssertContains(lanM15D, "RemoteReadOnlySessionLifecycle", "LAN-M15-D doc must document the session lifecycle mapper.");
        AssertContains(lanM15D, "/api/remote-readonly", "LAN-M15-D doc must reference the read-only endpoint group.");
        AssertContains(lanM15D, "no retry storm", "LAN-M15-D doc must state there is no retry storm.");
        AssertContains(lanM15D, "Forbidden Scope", "LAN-M15-D doc must define forbidden scope.");
        AssertContains(lanM15D, "Decision", "LAN-M15-D doc must include a decision section.");
        AssertNotContains(lanM15D, "LAN-M15-G is current", "LAN-M15-D doc must not mark LAN-M15-G as current work.");

        AssertContains(coreSmoke, "LanM15DSessionResilienceChecks.Run();", "CoreSmoke must wire LAN-M15-D checks.");
        AssertContains(infraSmoke, "remote-readonly-lan-session-resilience", "InfrastructureSmoke must wire remote-readonly-lan-session-resilience command.");
        AssertContains(infraSmoke, "LanM15RemoteReadOnlyLanSessionResilienceSmokeChecks.Run", "InfrastructureSmoke must call LAN-M15-D session resilience smoke.");
    }

    private static void AssertSourceResilienceBoundaries(string projectRoot)
    {
        string apiRoot = Path.Combine(projectRoot, "src", "Salryn.Api.Local");
        string webRoot = Path.Combine(projectRoot, "src", "Salryn.App.Web", "src");

        string lifecycle = ReadRequiredFile(apiRoot, "Services", "RemoteReadOnlySessionLifecycle.cs");
        string service = ReadRequiredFile(apiRoot, "Services", "RemoteReadOnlyApiService.cs");
        string contracts = ReadRequiredFile(apiRoot, "Contracts", "RemoteReadOnlyApiContracts.cs");
        string remoteReadOnlyEndpoints = ReadRequiredFile(apiRoot, "RemoteReadOnlyApiEndpoints.cs");
        string page = ReadRequiredFile(webRoot, "pages", "RemoteReadOnlyClientPage.tsx");
        string hook = ReadRequiredFile(webRoot, "hooks", "useRemoteReadOnlyClient.ts");
        string smoke = ReadRequiredFile(projectRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "LanM15RemoteReadOnlyLanSessionResilienceSmokeChecks.cs");

        AssertContains(lifecycle, "RemoteReadOnlySessionLifecycle", "Session lifecycle mapper must exist.");
        AssertContains(lifecycle, "HandshakeExpired", "Session lifecycle must classify expired handshake.");
        AssertContains(lifecycle, "TerminalRevoked", "Session lifecycle must classify revoked terminal.");
        AssertContains(lifecycle, "AutomaticRepairDisabled", "Session lifecycle must keep automatic re-pair disabled.");
        AssertContains(lifecycle, "SanitizePublicText", "Session lifecycle must sanitize public guidance.");

        AssertContains(contracts, "RemoteReadOnlySessionResilienceResponse", "Contracts must define the session resilience response.");
        AssertContains(service, "RemoteReadOnlySessionLifecycle", "Api service must map session resilience.");
        AssertContains(service, "SessionResilience", "Api service must populate SessionResilience.");

        AssertContains(remoteReadOnlyEndpoints, "/api/remote-readonly", "Remote-readonly endpoints must stay under /api/remote-readonly.");
        AssertNotContains(remoteReadOnlyEndpoints, ".MapPost(", "Remote-readonly endpoints must remain GET-only.");

        AssertContains(page, "Session Resilience / Lifecycle", "Remote read-only page must expose the session resilience card.");
        AssertContains(page, "Last successful read", "Remote read-only page must show the last successful read time.");
        AssertContains(page, "No catalog mutation controls are present", "Remote read-only page must keep mutation controls absent.");
        AssertNotContains(hook, "useMutation", "Remote read-only client hook must remain read-only (no mutations).");

        AssertContains(smoke, "RemoteReadOnlySessionLifecycle", "LAN-M15-D smoke must exercise the lifecycle mapper.");
        AssertContains(smoke, "AutomaticRepairDisabled", "LAN-M15-D smoke must verify automatic re-pair stays disabled.");
    }

    private static void AssertLanM15DChangedFilesStayInScope(string projectRoot, string currentState)
    {
        if (!currentState.Contains("LAN-M15-D remains completed but unpromoted on Freehand", StringComparison.Ordinal))
        {
            Console.WriteLine("LAN-M15-D changed-file scope guard skipped: LAN-M15-D is not marked completed. Static resilience checks remain active.");
            return;
        }

        string[] changedPaths = GetChangedPaths(projectRoot);

        if (changedPaths.Length == 0)
        {
            Console.WriteLine("LAN-M15-D changed-file scope guard skipped: no branch-diff paths were available. Static resilience checks remain active.");
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
                throw new InvalidOperationException($"LAN-M15-D changed a forbidden boundary file: {normalizedPath}");
            }

            throw new InvalidOperationException($"LAN-M15-D changed an out-of-scope file: {normalizedPath}");
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

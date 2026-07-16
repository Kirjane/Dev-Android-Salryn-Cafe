using System.Diagnostics;

namespace Salryn.Tests;

public static class LanM15CLocalControlChecks
{
    private const string LanM15CBaselineSha = "e477699dca27a21954a5ad9d388307fdb9ed08fd";

    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string handoffRoot = Path.Combine(projectRoot, "00-HANDOFF");
        string currentStatePath = Path.Combine(handoffRoot, "CURRENT_STABLE_STATE_AND_NEXT_WORK.md");
        string lanM15CPath = Path.Combine(handoffRoot, "LAN_M15_C_LOCAL_ONLY_LAN_CONTROL_PANEL_AND_TERMINAL_TRUST_MANAGEMENT.md");
        string coreSmokePath = Path.Combine(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string infraSmokePath = Path.Combine(projectRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "Program.cs");

        string currentState = ReadRequiredFile(currentStatePath);
        string lanM15C = ReadRequiredFile(lanM15CPath);
        string coreSmoke = ReadRequiredFile(coreSmokePath);
        string infraSmoke = ReadRequiredFile(infraSmokePath);

        if (IsCurrentLanM16QaBState(currentState))
        {
            AssertContains(currentState, "Stable as of `e477699dca27a21954a5ad9d388307fdb9ed08fd`", "Current-state doc must preserve the current stable Sandbox SHA.");
            AssertContains(currentState, "LAN-M16-QA-B", "Current-state doc must mark LAN-M16-QA-B as the current candidate work.");
            AssertContains(currentState, "Current live coordination authority:", "Current-state doc must keep live coordination authority visible.");
            AssertContains(currentState, "Historical M-series and ROLE docs remain context only and must not override current source truth.", "Current-state doc must keep older M-series docs demoted to historical context.");

            AssertContains(lanM15C, "LocalLanControlAuthorization", "LAN-M15-C doc must remain available as historical local control context.");
            AssertContains(coreSmoke, "LanM15CLocalControlChecks.Run();", "CoreSmoke must keep LAN-M15-C checks wired.");
            AssertContains(infraSmoke, "remote-readonly-lan-local-control", "InfrastructureSmoke must keep LAN-M15-C coverage wired.");
            AssertSourceLocalControlBoundaries(projectRoot);
            return;
        }

        AssertDocBoundaries(currentState, lanM15C, coreSmoke, infraSmoke, handoffRoot);
        AssertSourceLocalControlBoundaries(projectRoot);
        AssertLanM15CChangedFilesStayInScope(projectRoot, currentState);
    }

    private static bool IsCurrentLanM16QaBState(string currentState)
    {
        return currentState.Contains("LAN-M16-QA-B", StringComparison.Ordinal)
            && currentState.Contains("Historical M-series and ROLE docs remain context only and must not override current source truth.", StringComparison.Ordinal)
            && currentState.Contains("Current live coordination authority:", StringComparison.Ordinal);
    }

    private static void AssertDocBoundaries(
        string currentState,
        string lanM15C,
        string coreSmoke,
        string infraSmoke,
        string handoffRoot)
    {
        AssertContains(currentState, LanM15CBaselineSha, "Current-state doc must contain the stable Sandbox SHA.");
        AssertContains(currentState, "LAN-M14-A through LAN-M14-E are stable", "Current-state doc must keep LAN-M14-A through LAN-M14-E stable.");
        AssertContains(currentState, "LAN-M14-F through LAN-M14-I remain completed but unpromoted on Freehand", "Current-state doc must mark LAN-M14-F through LAN-M14-I as unpromoted.");
        AssertContains(currentState, "LAN-M15-C remains completed but unpromoted on Freehand", "Current-state doc must mark LAN-M15-C as unpromoted candidate work.");
        AssertContains(currentState, "LAN-M15-F is the LAN completion cap", "Current-state doc must name LAN-M15-F as the LAN completion cap.");
        AssertContains(currentState, "no Sandbox promotion has occurred", "Current-state doc must state no Sandbox promotion has occurred.");
        AssertNotContains(currentState, "LAN-M14-J is current", "Current-state doc must not mark LAN-M14-J as current work.");

        string docPath = Path.Combine(handoffRoot, "LAN_M15_C_LOCAL_ONLY_LAN_CONTROL_PANEL_AND_TERMINAL_TRUST_MANAGEMENT.md");
        if (!File.Exists(docPath))
        {
            throw new InvalidOperationException("Required LAN-M15-C doc missing.");
        }

        AssertContains(currentState, "LAN_M15_C_LOCAL_ONLY_LAN_CONTROL_PANEL_AND_TERMINAL_TRUST_MANAGEMENT.md", "Current-state doc must list LAN-M15-C doc as live authority.");
        AssertContains(lanM15C, LanM15CBaselineSha, "LAN-M15-C doc must contain the source stamp SHA.");
        AssertContains(lanM15C, "/api/lan-control", "LAN-M15-C doc must document the local-only control endpoint group.");
        AssertContains(lanM15C, "LocalLanControlAuthorization", "LAN-M15-C doc must document the local operator authorization guard.");
        AssertContains(lanM15C, "LocalLanControlService", "LAN-M15-C doc must document the local control service.");
        AssertContains(lanM15C, "CookieTrustRejectedForRemoteLan", "LAN-M15-C doc must document cookie trust rejection.");
        AssertContains(lanM15C, "Forbidden Scope", "LAN-M15-C doc must define forbidden scope.");
        AssertContains(lanM15C, "Decision", "LAN-M15-C doc must include a decision section.");
        AssertNotContains(lanM15C, "LAN-M15-G is current", "LAN-M15-C doc must not mark LAN-M15-G as current work.");

        AssertContains(coreSmoke, "LanM15CLocalControlChecks.Run();", "CoreSmoke must wire LAN-M15-C checks.");
        AssertContains(infraSmoke, "remote-readonly-lan-local-control", "InfrastructureSmoke must wire remote-readonly-lan-local-control command.");
        AssertContains(infraSmoke, "LanM15RemoteReadOnlyLanLocalControlSmokeChecks.Run", "InfrastructureSmoke must call LAN-M15-C local control smoke.");
    }

    private static void AssertSourceLocalControlBoundaries(string projectRoot)
    {
        string apiRoot = Path.Combine(projectRoot, "src", "Salryn.Api.Local");
        string webRoot = Path.Combine(projectRoot, "src", "Salryn.App.Web", "src");

        string authorization = ReadRequiredFile(apiRoot, "Auth", "LocalLanControlAuthorization.cs");
        string localAccessGuard = ReadRequiredFile(apiRoot, "Auth", "LocalApiLocalAccessGuard.cs");
        string service = ReadRequiredFile(apiRoot, "Services", "LocalLanControlService.cs");
        string endpoints = ReadRequiredFile(apiRoot, "LocalLanControlEndpoints.cs");
        string remoteReadOnlyEndpoints = ReadRequiredFile(apiRoot, "RemoteReadOnlyApiEndpoints.cs");
        string factory = ReadRequiredFile(apiRoot, "LocalApiApplicationFactory.cs");
        string page = ReadRequiredFile(webRoot, "pages", "RemoteReadOnlyClientPage.tsx");
        string hostPage = ReadRequiredFile(webRoot, "pages", "HostLanControlPage.tsx");
        string api = ReadRequiredFile(webRoot, "api", "remoteReadOnlyClient.ts");
        string smoke = ReadRequiredFile(projectRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "LanM15RemoteReadOnlyLanLocalControlSmokeChecks.cs");

        AssertContains(authorization, "IsLoopbackOperatorRequest", "Local control authorization must classify loopback operator requests.");
        AssertContains(authorization, "LocalLanControl.LocalOperatorRequired", "Local control authorization must reject non-local callers with a safe code.");
        AssertContains(localAccessGuard, "RemoteReadOnlyBridgeHeaders.TerminalId", "Local access guard must reject requests carrying remote bridge headers.");

        AssertContains(service, "SetRemoteTransportAsync", "Local control service must support enable/disable transport.");
        AssertContains(service, "DisableTerminalAsync", "Local control service must support disable terminal.");
        AssertContains(service, "RevokeTerminalAsync", "Local control service must support revoke terminal.");
        AssertContains(service, "ExplicitLanAddress", "Local control service must enable only explicit LAN address binds.");
        AssertContains(service, "Wildcard bind is never allowed", "Local control service must reject wildcard bind in messaging.");
        AssertNotContains(service, "AllowOfficialCommandsRemotely: true", "Local control service must never allow official commands remotely.");
        AssertNotContains(service, "AllowAdminCommandsRemotely: true", "Local control service must never allow admin commands remotely.");

        AssertContains(endpoints, "/api/lan-control", "Local control endpoints must be under /api/lan-control.");
        AssertContains(endpoints, "RequireLocalOperator", "Local control endpoints must require the localhost operator.");
        AssertContains(endpoints, "terminals/{terminalId}/disable", "Local control endpoints must expose terminal disable.");
        AssertContains(endpoints, "terminals/{terminalId}/revoke", "Local control endpoints must expose terminal revoke.");

        AssertContains(factory, "MapLocalLanControlEndpoints", "Application factory must map local LAN control endpoints.");
        AssertContains(factory, "LocalLanControlService", "Application factory must register the local LAN control service.");

        AssertNotContains(remoteReadOnlyEndpoints, ".MapPost(", "Remote-readonly endpoints must remain GET-only; local control lives in a separate module.");

        AssertContains(hostPage, "Terminal Management", "Dedicated terminal management page must exist.");
        AssertContains(hostPage, "Available on this host", "Terminal management page must remain host-only.");
        AssertNotContains(page, "Local LAN Control (Operator-Only)", "Remote read-only page must not expose host LAN controls.");
        AssertContains(page, "No catalog mutation controls are present", "Remote read-only page must keep catalog mutation controls absent.");
        AssertContains(api, "/api/lan-control/status", "Web API client must call the local control status endpoint.");

        AssertContains(smoke, "LocalLanControlAuthorization", "LAN-M15-C smoke must exercise the local control authorization guard.");
        AssertContains(smoke, "IsLoopbackOperatorRequest", "LAN-M15-C smoke must verify loopback operator classification.");
    }

    private static void AssertLanM15CChangedFilesStayInScope(string projectRoot, string currentState)
    {
        if (!currentState.Contains("LAN-M15-C remains completed but unpromoted on Freehand", StringComparison.Ordinal))
        {
            Console.WriteLine("LAN-M15-C changed-file scope guard skipped: LAN-M15-C is not marked completed. Static local control checks remain active.");
            return;
        }

        string[] changedPaths = GetChangedPaths(projectRoot);

        if (changedPaths.Length == 0)
        {
            Console.WriteLine("LAN-M15-C changed-file scope guard skipped: no branch-diff paths were available. Static local control checks remain active.");
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
                throw new InvalidOperationException($"LAN-M15-C changed a forbidden boundary file: {normalizedPath}");
            }

            throw new InvalidOperationException($"LAN-M15-C changed an out-of-scope file: {normalizedPath}");
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

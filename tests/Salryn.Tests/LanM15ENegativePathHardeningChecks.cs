using System.Diagnostics;

namespace Salryn.Tests;

public static class LanM15ENegativePathHardeningChecks
{
    private const string LanM15EBaselineSha = "e477699dca27a21954a5ad9d388307fdb9ed08fd";

    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string handoffRoot = Path.Combine(projectRoot, "00-HANDOFF");
        string currentStatePath = Path.Combine(handoffRoot, "CURRENT_STABLE_STATE_AND_NEXT_WORK.md");
        string lanM15EPath = Path.Combine(handoffRoot, "LAN_M15_E_ABUSE_FLOOD_NEGATIVE_PATH_HARDENING.md");
        string coreSmokePath = Path.Combine(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string infraSmokePath = Path.Combine(projectRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "Program.cs");

        string currentState = ReadRequiredFile(currentStatePath);
        string lanM15E = ReadRequiredFile(lanM15EPath);
        string coreSmoke = ReadRequiredFile(coreSmokePath);
        string infraSmoke = ReadRequiredFile(infraSmokePath);

        AssertDocBoundaries(currentState, lanM15E, coreSmoke, infraSmoke, handoffRoot);
        AssertSourceHardeningBoundaries(projectRoot);
        AssertLanM15EChangedFilesStayInScope(projectRoot, currentState);
    }

    private static void AssertDocBoundaries(
        string currentState,
        string lanM15E,
        string coreSmoke,
        string infraSmoke,
        string handoffRoot)
    {
        AssertContains(currentState, LanM15EBaselineSha, "Current-state doc must contain the stable Sandbox SHA.");
        AssertContains(currentState, "LAN-M14-A through LAN-M14-E are stable", "Current-state doc must keep LAN-M14-A through LAN-M14-E stable.");
        AssertContains(currentState, "LAN-M14-F through LAN-M14-I remain completed but unpromoted on Freehand", "Current-state doc must mark LAN-M14-F through LAN-M14-I as unpromoted.");
        AssertContains(currentState, "LAN-M15-E remains completed but unpromoted on Freehand", "Current-state doc must mark LAN-M15-E as unpromoted candidate work.");
        AssertContains(currentState, "LAN-M15-F is the LAN completion cap", "Current-state doc must name LAN-M15-F as the LAN completion cap.");
        AssertContains(currentState, "no Sandbox promotion has occurred", "Current-state doc must state no Sandbox promotion has occurred.");
        AssertNotContains(currentState, "LAN-M14-J is current", "Current-state doc must not mark LAN-M14-J as current work.");

        string docPath = Path.Combine(handoffRoot, "LAN_M15_E_ABUSE_FLOOD_NEGATIVE_PATH_HARDENING.md");
        if (!File.Exists(docPath))
        {
            throw new InvalidOperationException("Required LAN-M15-E doc missing.");
        }

        AssertContains(currentState, "LAN_M15_E_ABUSE_FLOOD_NEGATIVE_PATH_HARDENING.md", "Current-state doc must list LAN-M15-E doc as live authority.");
        AssertContains(lanM15E, LanM15EBaselineSha, "LAN-M15-E doc must contain the source stamp SHA.");
        AssertContains(lanM15E, "RemoteReadOnlyAbuseGuard", "LAN-M15-E doc must document the abuse guard.");
        AssertContains(lanM15E, "no schema", "LAN-M15-E doc must state no schema is introduced.");
        AssertContains(lanM15E, "soft-throttle", "LAN-M15-E doc must document the soft-throttle model.");
        AssertContains(lanM15E, "Forbidden Scope", "LAN-M15-E doc must define forbidden scope.");
        AssertContains(lanM15E, "Decision", "LAN-M15-E doc must include a decision section.");
        AssertNotContains(lanM15E, "LAN-M15-G is current", "LAN-M15-E doc must not mark LAN-M15-G as current work.");

        AssertContains(coreSmoke, "LanM15ENegativePathHardeningChecks.Run();", "CoreSmoke must wire LAN-M15-E checks.");
        AssertContains(infraSmoke, "remote-readonly-lan-negative-path-hardening", "InfrastructureSmoke must wire remote-readonly-lan-negative-path-hardening command.");
        AssertContains(infraSmoke, "LanM15RemoteReadOnlyLanNegativePathHardeningSmokeChecks.Run", "InfrastructureSmoke must call LAN-M15-E negative-path smoke.");
    }

    private static void AssertSourceHardeningBoundaries(string projectRoot)
    {
        string apiRoot = Path.Combine(projectRoot, "src", "Salryn.Api.Local");

        string abuseGuard = ReadRequiredFile(apiRoot, "Services", "RemoteReadOnlyAbuseGuard.cs");
        string bridgeContext = ReadRequiredFile(apiRoot, "Bridge", "RemoteReadOnlyBridgeRequestContext.cs");
        string bridgeAuth = ReadRequiredFile(apiRoot, "Services", "RemoteReadOnlyApiAuthorization.cs");
        string remoteReadOnlyEndpoints = ReadRequiredFile(apiRoot, "RemoteReadOnlyApiEndpoints.cs");
        string smoke = ReadRequiredFile(projectRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "LanM15RemoteReadOnlyLanNegativePathHardeningSmokeChecks.cs");

        AssertContains(abuseGuard, "IsHeaderValueWithinLimit", "Abuse guard must bound header length.");
        AssertContains(abuseGuard, "IsAcceptableCategory", "Abuse guard must allow-list safe categories.");
        AssertContains(abuseGuard, "SanitizeHostileText", "Abuse guard must sanitize hostile text.");
        AssertContains(abuseGuard, "RegisterDenial", "Abuse guard must register denials for soft-throttle.");
        AssertContains(abuseGuard, "IsSoftThrottled", "Abuse guard must expose soft-throttle state.");
        AssertNotContains(abuseGuard, "hard lock", "Abuse guard must not hard-lock terminals.");

        AssertContains(bridgeContext, "HasOversizedHeader", "Bridge context must classify oversized headers.");
        AssertContains(bridgeContext, "HasMalformedTerminalId", "Bridge context must classify malformed terminal ids.");
        AssertContains(bridgeContext, "HasMalformedHandshakeId", "Bridge context must classify malformed handshake ids.");

        AssertContains(bridgeAuth, "HasOversizedHeader", "Bridge authorization must reject oversized headers.");
        AssertContains(bridgeAuth, "IsAcceptableCategory", "Bridge authorization must reject non-allow-listed categories.");
        AssertContains(bridgeAuth, "RegisterDenial", "Bridge authorization must record denials for soft-throttle.");

        AssertContains(remoteReadOnlyEndpoints, "/api/remote-readonly", "Remote-readonly endpoints must stay under /api/remote-readonly.");
        AssertNotContains(remoteReadOnlyEndpoints, ".MapPost(", "Remote-readonly endpoints must remain GET-only.");

        AssertContains(smoke, "RemoteReadOnlyAbuseGuard", "LAN-M15-E smoke must exercise the abuse guard.");
        AssertContains(smoke, "HasOversizedHeader", "LAN-M15-E smoke must verify oversized header classification.");
        AssertContains(smoke, "IsSoftThrottled", "LAN-M15-E smoke must verify soft-throttle behavior.");
    }

    private static void AssertLanM15EChangedFilesStayInScope(string projectRoot, string currentState)
    {
        if (!currentState.Contains("LAN-M15-E remains completed but unpromoted on Freehand", StringComparison.Ordinal))
        {
            Console.WriteLine("LAN-M15-E changed-file scope guard skipped: LAN-M15-E is not marked completed. Static hardening checks remain active.");
            return;
        }

        string[] changedPaths = GetChangedPaths(projectRoot);

        if (changedPaths.Length == 0)
        {
            Console.WriteLine("LAN-M15-E changed-file scope guard skipped: no branch-diff paths were available. Static hardening checks remain active.");
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
                throw new InvalidOperationException($"LAN-M15-E changed a forbidden boundary file: {normalizedPath}");
            }

            throw new InvalidOperationException($"LAN-M15-E changed an out-of-scope file: {normalizedPath}");
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

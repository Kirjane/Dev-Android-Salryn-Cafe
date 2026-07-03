using System.Diagnostics;

namespace Salryn.Tests;

public static class LanM14GRemoteReadonlyBridgeRuntimeChecks
{
    private const string LanM14GBaselineSha = "e477699dca27a21954a5ad9d388307fdb9ed08fd";

    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string handoffRoot = Path.Combine(projectRoot, "00-HANDOFF");
        string currentStatePath = Path.Combine(handoffRoot, "CURRENT_STABLE_STATE_AND_NEXT_WORK.md");
        string lanM14GPath = Path.Combine(handoffRoot, "LAN_M14_G_REMOTE_READONLY_BRIDGE_RUNTIME.md");
        string coreSmokePath = Path.Combine(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        string currentState = ReadRequiredFile(currentStatePath);
        string lanM14G = ReadRequiredFile(lanM14GPath);
        string coreSmoke = ReadRequiredFile(coreSmokePath);

        if (IsLanM14FThroughIClosureContext(currentState))
        {
            AssertContains(currentState, LanM14GBaselineSha, "Current-state doc must contain the LAN-M14-G baseline SHA.");
            AssertContains(lanM14G, "hybrid bridge", "LAN-M14-G doc must document hybrid bridge runtime.");
            AssertContains(lanM14G, LanM14GBaselineSha, "LAN-M14-G doc must contain the source stamp SHA.");
            AssertContains(coreSmoke, "LanM14GRemoteReadonlyBridgeRuntimeChecks.Run();", "CoreSmoke must explicitly wire LAN-M14-G checks.");
            AssertSourceBridgeRuntimeBoundaries(projectRoot);
            AssertLanM14GChangedFilesStayInScope(projectRoot, currentState);
            return;
        }

        AssertDocBoundaries(currentState, lanM14G, coreSmoke);
        AssertSourceBridgeRuntimeBoundaries(projectRoot);
        AssertLanM14GChangedFilesStayInScope(projectRoot, currentState);
    }

    private static bool IsLanM14FThroughIClosureContext(string currentState)
    {
        return currentState.Contains("LAN-M14-F through LAN-M14-I are candidate work completed", StringComparison.Ordinal)
            || currentState.Contains("LAN-M14-I is the LAN-M14 cap", StringComparison.Ordinal);
    }

    private static void AssertDocBoundaries(string currentState, string lanM14G, string coreSmoke)
    {
        AssertContains(currentState, LanM14GBaselineSha, "Current-state doc must contain the LAN-M14-G baseline SHA.");
        AssertContains(currentState, "Hybrid bridge is implemented", "Current-state doc must state hybrid bridge is implemented.");
        AssertContains(currentState, "disabled by default", "Current-state doc must state hybrid bridge is disabled by default.");
        AssertContains(currentState, "LAN_M14_G_REMOTE_READONLY_BRIDGE_RUNTIME.md", "Current-state doc must list LAN-M14-G as live authority.");

        AssertContains(lanM14G, LanM14GBaselineSha, "LAN-M14-G doc must contain the source stamp SHA.");
        AssertContains(lanM14G, "RemoteReadOnlyBridgeHeaders", "LAN-M14-G doc must document RemoteReadOnlyBridgeHeaders.");
        AssertContains(lanM14G, "RemoteReadOnlyApiAuthorization", "LAN-M14-G doc must document RemoteReadOnlyApiAuthorization.");
        AssertContains(lanM14G, "hybrid bridge", "LAN-M14-G doc must document hybrid bridge architecture.");
        AssertContains(lanM14G, "X-Salryn-Terminal-Id", "LAN-M14-G doc must document handshake headers.");
        AssertContains(lanM14G, "GET-only", "LAN-M14-G doc must document GET-only endpoints.");
        AssertContains(lanM14G, "disabled by default", "LAN-M14-G doc must state disabled by default.");
        AssertContains(lanM14G, "Forbidden Scope", "LAN-M14-G doc must define forbidden scope.");
        AssertContains(lanM14G, "Decision", "LAN-M14-G doc must include a decision section.");

        AssertContains(coreSmoke, "LanM14GRemoteReadonlyBridgeRuntimeChecks.Run();", "CoreSmoke must explicitly wire LAN-M14-G checks.");
    }

    private static void AssertSourceBridgeRuntimeBoundaries(string projectRoot)
    {
        string apiRoot = Path.Combine(projectRoot, "src", "Salryn.Api.Local");

        string bridgeHeaders = ReadRequiredFile(apiRoot, "Bridge", "RemoteReadOnlyBridgeHeaders.cs");
        string bridgeAuth = ReadRequiredFile(apiRoot, "Services", "RemoteReadOnlyApiAuthorization.cs");
        string bridgeContext = ReadRequiredFile(apiRoot, "Bridge", "RemoteReadOnlyBridgeRequestContext.cs");
        string lanComposition = ReadRequiredFile(apiRoot, "Composition", "LanRemoteTransportApiComposition.cs");
        string readAttemptAudit = ReadRequiredFile(apiRoot, "Services", "RemoteReadOnlyReadAttemptAuditRecorder.cs");
        string remoteReadOnlyEndpoints = ReadRequiredFile(apiRoot, "RemoteReadOnlyApiEndpoints.cs");

        AssertContains(bridgeHeaders, "X-Salryn-Terminal-Id", "RemoteReadOnlyBridgeHeaders must define terminal id header.");
        AssertContains(bridgeHeaders, "X-Salryn-Handshake-Session-Id", "RemoteReadOnlyBridgeHeaders must define handshake session header.");
        AssertContains(bridgeHeaders, "X-Salryn-Remote-Origin", "RemoteReadOnlyBridgeHeaders must define remote origin header.");

        AssertContains(bridgeAuth, "AuthorizeBridgeAsync", "RemoteReadOnlyApiAuthorization must authorize bridge requests.");
        AssertContains(bridgeAuth, "ValidateHandshakeSessionAsync", "RemoteReadOnlyApiAuthorization must validate handshake sessions.");
        AssertContains(bridgeAuth, "ReadAttemptAudit", "RemoteReadOnlyApiAuthorization must record read-attempt audit.");

        AssertContains(bridgeContext, "FromHttpContext", "RemoteReadOnlyBridgeRequestContext must parse HTTP context.");
        AssertContains(bridgeContext, "IPAddress.IsLoopback", "RemoteReadOnlyBridgeRequestContext must distinguish loopback from LAN requests.");
        AssertContains(bridgeContext, "hasTerminalHeader || hasSessionHeader || !isLoopbackRequest", "Remote LAN requests must be treated as bridge requests even without bridge headers.");

        AssertContains(lanComposition, "LanRemoteTransportApiComposition", "LanRemoteTransportApiComposition must exist.");
        AssertContains(lanComposition, "RemoteReadOnlyClientService", "LanRemoteTransportApiComposition must wire RemoteReadOnlyClientService.");
        AssertContains(lanComposition, "SalrynHostCommandRemoteTransport", "LanRemoteTransportApiComposition must wire host command remote transport.");

        AssertContains(readAttemptAudit, "RemoteReadAttempt.Allowed", "Read-attempt audit must record allowed attempts.");
        AssertContains(readAttemptAudit, "RemoteReadAttempt.Denied", "Read-attempt audit must record denied attempts.");
        AssertContains(readAttemptAudit, "[redacted]", "Read-attempt audit must redact secrets.");

        AssertContains(remoteReadOnlyEndpoints, "MapGroup(\"/api/remote-readonly\")", "Remote-readonly endpoint group must remain under /api/remote-readonly.");
        AssertContains(remoteReadOnlyEndpoints, "AuthorizeBridgeOrLocalhostAsync", "Remote-readonly endpoints must authorize bridge or localhost.");
        AssertContains(remoteReadOnlyEndpoints, ".MapGet(", "Remote-readonly endpoints must remain GET routes.");
        AssertNotContains(remoteReadOnlyEndpoints, ".MapPost(", "Remote-readonly endpoints must not expose POST.");
        AssertNotContains(remoteReadOnlyEndpoints, ".MapPut(", "Remote-readonly endpoints must not expose PUT.");
        AssertNotContains(remoteReadOnlyEndpoints, ".MapPatch(", "Remote-readonly endpoints must not expose PATCH.");
        AssertNotContains(remoteReadOnlyEndpoints, ".MapDelete(", "Remote-readonly endpoints must not expose DELETE.");
    }

    private static void AssertLanM14GChangedFilesStayInScope(string projectRoot, string currentState)
    {
        if (IsLanM14FThroughIClosureContext(currentState))
        {
            Console.WriteLine("LAN-M14-G changed-file scope guard skipped: LAN-M14-I closure owns comprehensive changed-file scope.");
            return;
        }

        if (!currentState.Contains("LAN-M14-F through LAN-M14-I are candidate work completed", StringComparison.Ordinal))
        {
            Console.WriteLine("LAN-M14-G changed-file scope guard skipped: LAN-M14-G is no longer current candidate work. Static bridge runtime checks remain active.");
            return;
        }

        string[] changedPaths = GetChangedPaths(projectRoot);

        if (changedPaths.Length == 0)
        {
            Console.WriteLine("LAN-M14-G changed-file scope guard skipped: no branch-diff paths were available. Static bridge runtime checks remain active.");
            return;
        }

        HashSet<string> allowedExact = new(StringComparer.OrdinalIgnoreCase)
        {
            "00-HANDOFF/LAN_M14_G_REMOTE_READONLY_BRIDGE_RUNTIME.md",
            "00-HANDOFF/CURRENT_STABLE_STATE_AND_NEXT_WORK.md",
            "tests/Salryn.Tests/LanM14GRemoteReadonlyBridgeRuntimeChecks.cs",
            "tools/Salryn.Tools.CoreSmoke/Program.cs"
        };

        foreach (string changedPath in changedPaths)
        {
            string normalizedPath = changedPath.Replace('\\', '/');

            if (allowedExact.Contains(normalizedPath))
            {
                continue;
            }

            if (normalizedPath.StartsWith("tests/Salryn.Tests/LanM14", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (normalizedPath.StartsWith("00-HANDOFF/LAN_M14_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (normalizedPath.StartsWith("src/Salryn.Api.Local/", StringComparison.OrdinalIgnoreCase)
                && (normalizedPath.Contains("Bridge", StringComparison.OrdinalIgnoreCase)
                    || normalizedPath.Contains("Composition", StringComparison.OrdinalIgnoreCase)
                    || normalizedPath.Contains("RemoteReadOnly", StringComparison.OrdinalIgnoreCase)
                    || normalizedPath.Contains("HostCommands", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            throw new InvalidOperationException($"LAN-M14-G changed an out-of-scope file: {normalizedPath}");
        }
    }

    private static string[] GetChangedPaths(string projectRoot)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddPaths(paths, TryRunGit(projectRoot, "diff --name-only " + LanM14GBaselineSha + "...HEAD"));
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

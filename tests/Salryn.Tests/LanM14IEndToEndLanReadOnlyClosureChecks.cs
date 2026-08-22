using System.Diagnostics;

namespace Salryn.Tests;

public static class LanM14IEndToEndLanReadOnlyClosureChecks
{
    private const string LanM14IBaselineSha = "e477699dca27a21954a5ad9d388307fdb9ed08fd";

    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string handoffRoot = Path.Combine(projectRoot, "00-HANDOFF");
        string currentStatePath = Path.Combine(handoffRoot, "CURRENT_STABLE_STATE_AND_NEXT_WORK.md");
        string lanM14IPath = Path.Combine(handoffRoot, "LAN_M14_I_END_TO_END_LAN_READONLY_CLOSURE.md");
        string coreSmokePath = Path.Combine(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string infraSmokePath = Path.Combine(projectRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "Program.cs");

        string currentState = ReadRequiredFile(currentStatePath);
        string lanM14I = ReadRequiredFile(lanM14IPath);
        string coreSmoke = ReadRequiredFile(coreSmokePath);
        string infraSmoke = ReadRequiredFile(infraSmokePath);

        if (IsCurrentLanM16QaBState(currentState))
        {
            AssertContains(currentState, "Stable as of `e477699dca27a21954a5ad9d388307fdb9ed08fd`", "Current-state doc must preserve the current stable Sandbox SHA.");
            AssertContains(currentState, "LAN-M16-QA-B", "Current-state doc must mark LAN-M16-QA-B as the current candidate work.");
            AssertContains(currentState, "Current live coordination authority:", "Current-state doc must keep live coordination authority visible.");
            AssertContains(currentState, "Historical M-series and ROLE docs remain context only and must not override current source truth.", "Current-state doc must keep older M-series docs demoted to historical context.");

            AssertContains(lanM14I, "LAN-M14-I is the LAN-M14 cap", "LAN-M14-I doc must remain available as historical closure context.");
            AssertContains(coreSmoke, "LanM14IEndToEndLanReadOnlyClosureChecks.Run();", "CoreSmoke must keep LAN-M14-I checks wired.");
            AssertContains(infraSmoke, "lan-m14-end-to-end-closure", "InfrastructureSmoke must keep LAN-M14-I coverage wired.");
            AssertSourceClosureBoundaries(projectRoot);
            return;
        }

        AssertDocBoundaries(currentState, lanM14I, coreSmoke, infraSmoke, handoffRoot);
        AssertSourceClosureBoundaries(projectRoot);
        AssertLanM14IChangedFilesStayInScope(projectRoot, currentState);
    }

    private static bool IsCurrentLanM16QaBState(string currentState)
    {
        return currentState.Contains("LAN-M16-QA-B", StringComparison.Ordinal)
            && currentState.Contains("Historical M-series and ROLE docs remain context only and must not override current source truth.", StringComparison.Ordinal)
            && currentState.Contains("Current live coordination authority:", StringComparison.Ordinal);
    }

    private static void AssertDocBoundaries(
        string currentState,
        string lanM14I,
        string coreSmoke,
        string infraSmoke,
        string handoffRoot)
    {
        AssertContains(currentState, LanM14IBaselineSha, "Current-state doc must contain the LAN-M14-I baseline SHA.");
        AssertContains(currentState, "LAN-M14-F through LAN-M14-I are candidate work completed", "Current-state doc must mark LAN-M14-F through LAN-M14-I as candidate work completed.");
        AssertContains(currentState, "LAN-M14-I is the LAN-M14 cap", "Current-state doc must state LAN-M14-I is the LAN-M14 cap.");
        AssertContains(currentState, "LAN-M14-J is not started and must not exist", "Current-state doc must state LAN-M14-J must not exist.");
        AssertContains(currentState, "Hybrid bridge is implemented", "Current-state doc must state hybrid bridge is implemented.");
        AssertContains(currentState, "disabled by default", "Current-state doc must state hybrid bridge is disabled by default.");
        AssertContains(currentState, "explicit operator enable is required", "Current-state doc must require explicit operator enable.");

        string[] requiredDocs =
        [
            "LAN_M14_F_HOST_BIND_CONFIG_SEAM_IMPLEMENTATION.md",
            "LAN_M14_G_REMOTE_READONLY_BRIDGE_RUNTIME.md",
            "LAN_M14_H_PAIRING_OPERATOR_UX_SURFACE.md",
            "LAN_M14_I_END_TO_END_LAN_READONLY_CLOSURE.md"
        ];

        foreach (string docName in requiredDocs)
        {
            string docPath = Path.Combine(handoffRoot, docName);
            if (!File.Exists(docPath))
            {
                throw new InvalidOperationException("Required LAN-M14 closure doc missing: " + docName);
            }

            AssertContains(currentState, docName, "Current-state doc must list " + docName + " as live authority.");
        }

        AssertNotContains(currentState, "LAN-M14-J is current", "Current-state doc must not mark LAN-M14-J as current work.");
        AssertNotContains(File.ReadAllText(Path.Combine(handoffRoot, "LAN_M14_F_HOST_BIND_CONFIG_SEAM_IMPLEMENTATION.md")), "LAN-M14-J", "LAN-M14-F doc must not reference LAN-M14-J.");
        AssertNotContains(File.ReadAllText(Path.Combine(handoffRoot, "LAN_M14_G_REMOTE_READONLY_BRIDGE_RUNTIME.md")), "LAN-M14-J", "LAN-M14-G doc must not reference LAN-M14-J.");
        AssertNotContains(File.ReadAllText(Path.Combine(handoffRoot, "LAN_M14_H_PAIRING_OPERATOR_UX_SURFACE.md")), "LAN-M14-J", "LAN-M14-H doc must not reference LAN-M14-J.");

        AssertContains(lanM14I, LanM14IBaselineSha, "LAN-M14-I doc must contain the source stamp SHA.");
        AssertContains(lanM14I, "LanM14FHostBindConfigSeamChecks", "LAN-M14-I doc must reference LAN-M14-F guard.");
        AssertContains(lanM14I, "LanM14GRemoteReadonlyBridgeRuntimeChecks", "LAN-M14-I doc must reference LAN-M14-G guard.");
        AssertContains(lanM14I, "LanM14HPairingOperatorUxSurfaceChecks", "LAN-M14-I doc must reference LAN-M14-H guard.");
        AssertContains(lanM14I, "LanM14IEndToEndLanReadOnlyClosureChecks", "LAN-M14-I doc must reference LAN-M14-I guard.");
        AssertContains(lanM14I, "lan-m14-end-to-end-closure", "LAN-M14-I doc must reference end-to-end infrastructure smoke.");
        AssertContains(lanM14I, "LAN-M14-I is the LAN-M14 cap", "LAN-M14-I doc must state it is the LAN-M14 cap.");
        AssertContains(lanM14I, "LAN-M14-J must not exist", "LAN-M14-I doc must forbid LAN-M14-J.");
        AssertContains(lanM14I, "Forbidden Scope", "LAN-M14-I doc must define forbidden scope.");
        AssertContains(lanM14I, "Decision", "LAN-M14-I doc must include a decision section.");

        AssertContains(coreSmoke, "LanM14FHostBindConfigSeamChecks.Run();", "CoreSmoke must wire LAN-M14-F checks.");
        AssertContains(coreSmoke, "LanM14GRemoteReadonlyBridgeRuntimeChecks.Run();", "CoreSmoke must wire LAN-M14-G checks.");
        AssertContains(coreSmoke, "LanM14HPairingOperatorUxSurfaceChecks.Run();", "CoreSmoke must wire LAN-M14-H checks.");
        AssertContains(coreSmoke, "LanM14IEndToEndLanReadOnlyClosureChecks.Run();", "CoreSmoke must wire LAN-M14-I checks.");

        AssertContains(infraSmoke, "lan-m14-end-to-end-closure", "InfrastructureSmoke must wire lan-m14-end-to-end-closure command.");
        AssertContains(infraSmoke, "LanM14EndToEndRemoteReadOnlyLanClosureSmokeChecks.Run", "InfrastructureSmoke must call LAN-M14 end-to-end closure smoke.");
    }

    private static void AssertSourceClosureBoundaries(string projectRoot)
    {
        string apiRoot = Path.Combine(projectRoot, "src", "Salryn.Api.Local");
        string smokeRoot = Path.Combine(projectRoot, "tools", "Salryn.Tools.InfrastructureSmoke");

        string localApiLanUrlResolver = ReadRequiredFile(apiRoot, "Services", "LocalApiLanUrlResolver.cs");
        string bridgeHeaders = ReadRequiredFile(apiRoot, "Bridge", "RemoteReadOnlyBridgeHeaders.cs");
        string bridgeAuth = ReadRequiredFile(apiRoot, "Services", "RemoteReadOnlyApiAuthorization.cs");
        string readAttemptAudit = ReadRequiredFile(apiRoot, "Services", "RemoteReadOnlyReadAttemptAuditRecorder.cs");
        string lanComposition = ReadRequiredFile(apiRoot, "Composition", "LanRemoteTransportApiComposition.cs");
        string remoteReadOnlyEndpoints = ReadRequiredFile(apiRoot, "RemoteReadOnlyApiEndpoints.cs");
        string closureSmoke = ReadRequiredFile(smokeRoot, "LanM14EndToEndRemoteReadOnlyLanClosureSmokeChecks.cs");

        AssertContains(localApiLanUrlResolver, "ResolveListenUrls", "Closure must keep LocalApiLanUrlResolver.");
        AssertContains(bridgeHeaders, "X-Salryn-Terminal-Id", "Closure must keep bridge headers.");
        AssertContains(bridgeAuth, "AuthorizeBridgeAsync", "Closure must keep bridge authorization.");
        AssertContains(readAttemptAudit, "RemoteReadOnlyLan", "Closure must keep read-attempt audit recorder.");
        AssertContains(lanComposition, "LanRemoteTransportApiComposition", "Closure must keep hybrid bridge composition.");
        AssertNotContains(remoteReadOnlyEndpoints, ".MapPost(", "Closure must keep GET-only remote-readonly endpoints.");

        AssertContains(closureSmoke, "0.0.0.0", "Closure smoke must reject 0.0.0.0.");
        AssertContains(closureSmoke, "192.168.1.10", "Closure smoke must test explicit LAN enable.");
        AssertContains(closureSmoke, "OfficialCheckoutComplete", "Closure smoke must block official checkout.");
        AssertContains(closureSmoke, "LocalApiLanUrlResolver", "Closure smoke must test LocalApiLanUrlResolver when disabled.");
    }

    private static void AssertLanM14IChangedFilesStayInScope(string projectRoot, string currentState)
    {
        if (!currentState.Contains("LAN-M14-F through LAN-M14-I are candidate work completed", StringComparison.Ordinal)
            || currentState.Contains("LAN-M15-A is current candidate work on Freehand", StringComparison.Ordinal))
        {
            Console.WriteLine("LAN-M14-I changed-file scope guard skipped: LAN-M14-I is no longer current candidate work. Static closure checks remain active.");
            return;
        }

        string[] changedPaths = GetChangedPaths(projectRoot);

        if (changedPaths.Length == 0)
        {
            Console.WriteLine("LAN-M14-I changed-file scope guard skipped: no branch-diff paths were available. Static closure checks remain active.");
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
                || lowerPath.StartsWith("src/salryn.app.web/src/", StringComparison.Ordinal)
                || lowerPath.StartsWith("src/salryn.host.local/", StringComparison.Ordinal))
            {
                continue;
            }

            if (lowerPath.Contains("checkout", StringComparison.Ordinal)
                || lowerPath.Contains("settings", StringComparison.Ordinal)
                || lowerPath.Contains("backup", StringComparison.Ordinal)
                || lowerPath.Contains("official", StringComparison.Ordinal)
                || lowerPath.Contains("reports", StringComparison.Ordinal)
                || lowerPath.Contains("rmo", StringComparison.Ordinal)
                || lowerPath.Contains("lan-m14-j", StringComparison.Ordinal)
                || lowerPath.Contains("discovery", StringComparison.Ordinal)
                || lowerPath.Contains("mobile", StringComparison.Ordinal)
                || lowerPath.Contains("cloud", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"LAN-M14-I changed a forbidden boundary file: {normalizedPath}");
            }

            throw new InvalidOperationException($"LAN-M14-I changed an out-of-scope file: {normalizedPath}");
        }
    }

    private static string[] GetChangedPaths(string projectRoot)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddPaths(paths, TryRunGit(projectRoot, "diff --name-only " + LanM14IBaselineSha + "...HEAD"));
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

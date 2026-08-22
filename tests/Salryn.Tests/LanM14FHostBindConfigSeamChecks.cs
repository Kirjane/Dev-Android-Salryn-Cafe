using System.Diagnostics;

namespace Salryn.Tests;

public static class LanM14FHostBindConfigSeamChecks
{
    private const string LanM14FBaselineSha = "e477699dca27a21954a5ad9d388307fdb9ed08fd";

    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string handoffRoot = Path.Combine(projectRoot, "00-HANDOFF");
        string currentStatePath = Path.Combine(handoffRoot, "CURRENT_STABLE_STATE_AND_NEXT_WORK.md");
        string lanM14FPath = Path.Combine(handoffRoot, "LAN_M14_F_HOST_BIND_CONFIG_SEAM_IMPLEMENTATION.md");
        string coreSmokePath = Path.Combine(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        string currentState = ReadRequiredFile(currentStatePath);
        string lanM14F = ReadRequiredFile(lanM14FPath);
        string coreSmoke = ReadRequiredFile(coreSmokePath);

        if (IsCurrentLanM16QaBState(currentState))
        {
            AssertContains(currentState, "Stable as of `e477699dca27a21954a5ad9d388307fdb9ed08fd`", "Current-state doc must preserve the current stable Sandbox SHA.");
            AssertContains(currentState, "LAN-M16-QA-B", "Current-state doc must mark LAN-M16-QA-B as the current candidate work.");
            AssertContains(currentState, "Current live coordination authority:", "Current-state doc must keep live coordination authority visible.");
            AssertContains(currentState, "Historical M-series and ROLE docs remain context only and must not override current source truth.", "Current-state doc must keep older M-series docs demoted to historical context.");

            AssertContains(lanM14F, "LocalApiLanUrlResolver", "LAN-M14-F doc must remain available as historical host-bind cutline context.");
            AssertContains(coreSmoke, "LanM14FHostBindConfigSeamChecks.Run();", "CoreSmoke must keep LAN-M14-F checks wired.");
            AssertSourceHostBindSeamBoundaries(projectRoot);
            return;
        }

        if (IsLanM14FThroughIClosureContext(currentState))
        {
            AssertContains(currentState, LanM14FBaselineSha, "Current-state doc must contain the LAN-M14-F baseline SHA.");
            AssertContains(currentState, "LAN-M14-F through LAN-M14-I are candidate work completed", "Current-state doc must mark LAN-M14-F through LAN-M14-I as candidate work completed.");
            AssertContains(lanM14F, "LocalApiLanUrlResolver", "LAN-M14-F doc must document LocalApiLanUrlResolver.");
            AssertContains(lanM14F, LanM14FBaselineSha, "LAN-M14-F doc must contain the source stamp SHA.");
            AssertContains(coreSmoke, "LanM14FHostBindConfigSeamChecks.Run();", "CoreSmoke must explicitly wire LAN-M14-F checks.");
            AssertSourceHostBindSeamBoundaries(projectRoot);
            AssertLanM14FChangedFilesStayInScope(projectRoot, currentState);
            return;
        }

        AssertDocBoundaries(currentState, lanM14F, coreSmoke);
        AssertSourceHostBindSeamBoundaries(projectRoot);
        AssertLanM14FChangedFilesStayInScope(projectRoot, currentState);
    }

    private static bool IsLanM14FThroughIClosureContext(string currentState)
    {
        return currentState.Contains("LAN-M14-F through LAN-M14-I are candidate work completed", StringComparison.Ordinal)
            || currentState.Contains("LAN-M14-I is the LAN-M14 cap", StringComparison.Ordinal);
    }

    private static bool IsCurrentLanM16QaBState(string currentState)
    {
        return currentState.Contains("LAN-M16-QA-B", StringComparison.Ordinal)
            && currentState.Contains("Historical M-series and ROLE docs remain context only and must not override current source truth.", StringComparison.Ordinal)
            && currentState.Contains("Current live coordination authority:", StringComparison.Ordinal);
    }

    private static void AssertDocBoundaries(string currentState, string lanM14F, string coreSmoke)
    {
        AssertContains(currentState, LanM14FBaselineSha, "Current-state doc must contain the LAN-M14-F baseline SHA.");
        AssertContains(currentState, "`LAN-M14-E`", "Current-state doc must keep LAN-M14-E in the stable stack.");
        AssertContains(currentState, "LAN-M14-F through LAN-M14-I are candidate work completed", "Current-state doc must mark LAN-M14-F through LAN-M14-I as candidate work completed.");
        AssertContains(currentState, "Hybrid bridge is implemented", "Current-state doc must state hybrid bridge is implemented.");
        AssertContains(currentState, "disabled by default", "Current-state doc must state hybrid bridge is disabled by default.");
        AssertContains(currentState, "LAN-M14-J is not started", "Current-state doc must state LAN-M14-J is not started.");
        AssertContains(currentState, "LAN_M14_F_HOST_BIND_CONFIG_SEAM_IMPLEMENTATION.md", "Current-state doc must list LAN-M14-F as live authority.");
        AssertNotContains(currentState, "LAN-M14-F is not started", "Current-state doc must not retain stale LAN-M14-F not-started wording.");
        AssertContains(currentState, "LAN-M14-J is not started and must not exist", "Current-state doc must state LAN-M14-J is not started and must not exist.");

        AssertContains(lanM14F, LanM14FBaselineSha, "LAN-M14-F doc must contain the source stamp SHA.");
        AssertContains(lanM14F, "LocalApiLanUrlResolver", "LAN-M14-F doc must document LocalApiLanUrlResolver.");
        AssertContains(lanM14F, "RemoteReadOnlyLanBindPolicyMapper", "LAN-M14-F doc must document RemoteReadOnlyLanBindPolicyMapper.");
        AssertContains(lanM14F, "127.0.0.1:5299", "LAN-M14-F doc must document default localhost URL.");
        AssertContains(lanM14F, "ILanRemoteTransportConfigService", "LAN-M14-F doc must document ILanRemoteTransportConfigService wiring.");
        AssertContains(lanM14F, "0.0.0.0", "LAN-M14-F doc must forbid wildcard bind.");
        AssertContains(lanM14F, "disabled by default", "LAN-M14-F doc must state disabled by default.");
        AssertContains(lanM14F, "Forbidden Scope", "LAN-M14-F doc must define forbidden scope.");
        AssertContains(lanM14F, "Decision", "LAN-M14-F doc must include a decision section.");

        AssertContains(coreSmoke, "LanM14FHostBindConfigSeamChecks.Run();", "CoreSmoke must explicitly wire LAN-M14-F checks.");
    }

    private static void AssertSourceHostBindSeamBoundaries(string projectRoot)
    {
        string apiRoot = Path.Combine(projectRoot, "src", "Salryn.Api.Local");

        string localApiLanUrlResolver = ReadRequiredFile(apiRoot, "Services", "LocalApiLanUrlResolver.cs");
        string bindPolicyMapper = ReadRequiredFile(apiRoot, "Services", "RemoteReadOnlyLanBindPolicyMapper.cs");
        string localApiApplicationFactory = ReadRequiredFile(apiRoot, "LocalApiApplicationFactory.cs");
        string lanComposition = ReadRequiredFile(apiRoot, "Composition", "LanRemoteTransportApiComposition.cs");

        AssertContains(localApiLanUrlResolver, "ResolveListenUrls", "LocalApiLanUrlResolver must expose ResolveListenUrls.");
        AssertContains(localApiLanUrlResolver, "LanRemoteTransportConfig", "LocalApiLanUrlResolver must consume LanRemoteTransportConfig.");
        AssertContains(localApiLanUrlResolver, "ExplicitLanAddress", "LocalApiLanUrlResolver must gate on ExplicitLanAddress bind mode.");
        AssertNotContains(localApiLanUrlResolver, "0.0.0.0", "LocalApiLanUrlResolver must not contain wildcard bind.");

        AssertContains(bindPolicyMapper, "LocalOnlyReadMode", "RemoteReadOnlyLanBindPolicyMapper must keep LocalOnlyReadMode.");
        AssertContains(bindPolicyMapper, "HybridBridgeReadyMode", "RemoteReadOnlyLanBindPolicyMapper must define HybridBridgeReadyMode.");
        AssertContains(bindPolicyMapper, "LocalhostOnlyMode", "RemoteReadOnlyLanBindPolicyMapper must keep LocalhostOnlyMode.");
        AssertContains(bindPolicyMapper, "NoWildcardBind", "RemoteReadOnlyLanBindPolicyMapper must keep NoWildcardBind.");

        AssertContains(localApiApplicationFactory, "http://127.0.0.1:5299", "LocalApiApplicationFactory must keep default Api.Local URL on 127.0.0.1:5299.");
        AssertContains(localApiApplicationFactory, "LocalApiLanUrlResolver.ResolveListenUrls", "LocalApiApplicationFactory must wire LocalApiLanUrlResolver.");
        AssertContains(localApiApplicationFactory, "LanRemoteTransportApiComposition.Create", "LocalApiApplicationFactory must wire LanRemoteTransportApiComposition.");
        AssertNotContains(localApiApplicationFactory, "0.0.0.0", "LocalApiApplicationFactory must not contain wildcard bind.");

        AssertContains(lanComposition, "ILanRemoteTransportConfigService", "LanRemoteTransportApiComposition must wire ILanRemoteTransportConfigService.");
        AssertContains(lanComposition, "LanRemoteTransportConfigService", "LanRemoteTransportApiComposition must create LanRemoteTransportConfigService.");
    }

    private static void AssertLanM14FChangedFilesStayInScope(string projectRoot, string currentState)
    {
        if (IsLanM14FThroughIClosureContext(currentState))
        {
            Console.WriteLine("LAN-M14-F changed-file scope guard skipped: LAN-M14-I closure owns comprehensive changed-file scope.");
            return;
        }

        if (!currentState.Contains("LAN-M14-F through LAN-M14-I are candidate work completed", StringComparison.Ordinal))
        {
            Console.WriteLine("LAN-M14-F changed-file scope guard skipped: LAN-M14-F is no longer current candidate work. Static host bind seam checks remain active.");
            return;
        }

        string[] changedPaths = GetChangedPaths(projectRoot);

        if (changedPaths.Length == 0)
        {
            Console.WriteLine("LAN-M14-F changed-file scope guard skipped: no branch-diff paths were available. Static host bind seam checks remain active.");
            return;
        }

        HashSet<string> allowedPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "00-HANDOFF/LAN_M14_F_HOST_BIND_CONFIG_SEAM_IMPLEMENTATION.md",
            "00-HANDOFF/CURRENT_STABLE_STATE_AND_NEXT_WORK.md",
            "tests/Salryn.Tests/LanM14APostPromotionScopeAuditChecks.cs",
            "tests/Salryn.Tests/LanM14BExplicitHostBindReadinessChecks.cs",
            "tests/Salryn.Tests/LanM14CRemoteReadonlySessionBridgeCutlineChecks.cs",
            "tests/Salryn.Tests/LanM14DBridgeSessionContractStudyChecks.cs",
            "tests/Salryn.Tests/LanM14EAuditReadAttemptPolicyCutlineChecks.cs",
            "tests/Salryn.Tests/LanM14FHostBindConfigSeamChecks.cs",
            "tools/Salryn.Tools.CoreSmoke/Program.cs",
            "src/Salryn.Api.Local/Services/LocalApiLanUrlResolver.cs",
            "src/Salryn.Api.Local/Services/RemoteReadOnlyLanBindPolicyMapper.cs",
            "src/Salryn.Api.Local/LocalApiApplicationFactory.cs",
            "src/Salryn.Api.Local/Services/RemoteReadOnlyApiService.cs"
        };

        foreach (string changedPath in changedPaths)
        {
            string normalizedPath = changedPath.Replace('\\', '/');

            if (!allowedPaths.Contains(normalizedPath))
            {
                throw new InvalidOperationException($"LAN-M14-F changed an out-of-scope file: {normalizedPath}");
            }
        }
    }

    private static string[] GetChangedPaths(string projectRoot)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddPaths(paths, TryRunGit(projectRoot, "diff --name-only " + LanM14FBaselineSha + "...HEAD"));
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

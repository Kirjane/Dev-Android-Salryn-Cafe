using System.Diagnostics;

namespace Salryn.Tests;

public static class LanM14DBridgeSessionContractStudyChecks
{
    private const string LanM14DBaselineSha = "9aba4e2c5acda61a0d6f88e3606eb9331a8657ab";

    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string handoffRoot = Path.Combine(projectRoot, "00-HANDOFF");
        string currentStatePath = Path.Combine(handoffRoot, "CURRENT_STABLE_STATE_AND_NEXT_WORK.md");
        string lanM14DPath = Path.Combine(handoffRoot, "LAN_M14_D_BRIDGE_SESSION_CONTRACT_STUDY.md");
        string coreSmokePath = Path.Combine(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        string currentState = ReadRequiredFile(currentStatePath);
        string lanM14D = ReadRequiredFile(lanM14DPath);
        string coreSmoke = ReadRequiredFile(coreSmokePath);

        if (IsLaterLanM14DHistoricalContext(currentState))
        {
            AssertContains(currentState, "LAN-M14-D is stable", "Current-state doc must keep LAN-M14-D stable after later LAN-M14 work.");
            if (IsLanM14FThroughIImplementationContext(currentState))
            {
                AssertContains(currentState, "LAN-M14-F through LAN-M14-I are candidate work completed", "Current-state doc must mark LAN-M14-F through LAN-M14-I as candidate work completed.");
            }
            else
            {
                AssertContains(currentState, "LAN-M14-E is current candidate work", "Current-state doc must mark LAN-M14-E as current candidate work.");
            }

            AssertContains(lanM14D, "LAN-M14-D defines the future bridge session contract", "LAN-M14-D doc must remain available as historical session contract context.");
            AssertContains(lanM14D, "It does not implement session runtime behavior", "LAN-M14-D doc must state it does not implement session runtime behavior.");
            AssertContains(coreSmoke, "LanM14DBridgeSessionContractStudyChecks.Run();", "CoreSmoke must keep LAN-M14-D checks wired.");
            AssertSourceSessionContractBoundaries(projectRoot, currentState);
            AssertLanM14DChangedFilesStayInScope(projectRoot, currentState);
            return;
        }

        AssertDocBoundaries(currentState, lanM14D, coreSmoke);
        AssertSourceSessionContractBoundaries(projectRoot, currentState);
        AssertLanM14DChangedFilesStayInScope(projectRoot, currentState);
    }

    private static bool IsLanM14FThroughIImplementationContext(string currentState)
    {
        return currentState.Contains("LAN-M14-F through LAN-M14-I are candidate work completed", StringComparison.Ordinal)
            || currentState.Contains("LAN-M14-I is the LAN-M14 cap", StringComparison.Ordinal);
    }

    private static bool IsLaterLanM14DHistoricalContext(string currentState)
    {
        return currentState.Contains("LAN-M14-D is stable", StringComparison.Ordinal)
            && (currentState.Contains("LAN-M14-E is current candidate work", StringComparison.Ordinal)
                || currentState.Contains("LAN-M14-F through LAN-M14-I are candidate work completed", StringComparison.Ordinal)
                || currentState.Contains("LAN-M14-I is the LAN-M14 cap", StringComparison.Ordinal));
    }

    private static void AssertDocBoundaries(string currentState, string lanM14D, string coreSmoke)
    {
        AssertContains(currentState, LanM14DBaselineSha, "Current-state doc must contain the LAN-M14-D baseline SHA.");
        AssertContains(currentState, "LAN-M14-C is stable", "Current-state doc must state LAN-M14-C is stable.");
        AssertContains(currentState, "LAN-M14-D is current candidate work pending Codex verification", "Current-state doc must mark LAN-M14-D as current candidate pending Codex verification.");
        AssertContains(currentState, "LAN-M14-E is not started", "Current-state doc must state LAN-M14-E is not started.");
        AssertContains(currentState, "LAN bind activation is not started", "Current-state doc must state LAN bind activation is not started.");
        AssertContains(currentState, "Api.Local LAN bind is not started", "Current-state doc must state Api.Local LAN bind is not started.");
        AssertContains(currentState, "Remote read-only session bridge is not implemented", "Current-state doc must state the bridge is not implemented.");
        AssertContains(currentState, "Bridge session runtime is not implemented", "Current-state doc must state bridge session runtime is not implemented.");
        AssertContains(currentState, "LAN_M14_D_BRIDGE_SESSION_CONTRACT_STUDY.md", "Current-state doc must list LAN-M14-D as live authority.");
        AssertNotContains(currentState, "LAN-M14-C is current candidate work", "Current-state doc must not retain stale LAN-M14-C candidate wording.");
        AssertNotContains(currentState, "LAN-M14-C is current candidate work pending Codex verification", "Current-state doc must not retain stale LAN-M14-C unverified wording.");
        AssertNotContains(currentState, "LAN-M14-D is implemented", "Current-state doc must not claim LAN-M14-D is implemented.");
        AssertNotContains(currentState, "LAN bind is enabled", "Current-state doc must not claim LAN bind is enabled.");
        AssertNotContains(currentState, "LAN runtime is active", "Current-state doc must not claim LAN runtime is active.");
        AssertNotContains(currentState, "bridge is implemented", "Current-state doc must not claim bridge is implemented.");
        AssertNotContains(currentState, "session runtime is implemented", "Current-state doc must not claim session runtime is implemented.");

        AssertContains(lanM14D, "It does not implement the bridge", "LAN-M14-D doc must state it does not implement the bridge.");
        AssertContains(lanM14D, "It does not implement session runtime behavior", "LAN-M14-D doc must state it does not implement session runtime behavior.");
        AssertContains(lanM14D, "It does not enable LAN bind", "LAN-M14-D doc must state it does not enable LAN bind.");
        AssertContains(lanM14D, "Operator enablement state", "LAN-M14-D doc must define operator enablement state ownership.");
        AssertContains(lanM14D, "Terminal identity", "LAN-M14-D doc must define terminal identity ownership.");
        AssertContains(lanM14D, "Pairing activation", "LAN-M14-D doc must define pairing activation ownership.");
        AssertContains(lanM14D, "Handshake validation", "LAN-M14-D doc must define handshake validation ownership.");
        AssertContains(lanM14D, "Api.Local browser/session state", "LAN-M14-D doc must define Api.Local browser/session state ownership.");
        AssertContains(lanM14D, "Bridge read session", "LAN-M14-D doc must define bridge read session as future concept.");
        AssertContains(lanM14D, "Future concept only", "LAN-M14-D doc must mark bridge read session as future concept only.");
        AssertContains(lanM14D, "Audit/read-attempt policy", "LAN-M14-D doc must define audit/read-attempt policy ownership.");
        AssertContains(lanM14D, "SessionId", "LAN-M14-D doc must list required session contract fields.");
        AssertContains(lanM14D, "conceptual only", "LAN-M14-D doc must state contract is conceptual only.");
        AssertContains(lanM14D, "Do not add code DTOs or runtime models", "LAN-M14-D doc must forbid code DTOs/runtime models.");
        AssertContains(lanM14D, "product lookup/product browse", "LAN-M14-D doc must list allowed future read categories.");
        AssertContains(lanM14D, "checkout", "LAN-M14-D doc must list forbidden future categories.");
        AssertContains(lanM14D, "expired handshake", "LAN-M14-D doc must define failure/denial states.");
        AssertContains(lanM14D, "Localhost operator cookies/sessions must not become LAN remote trust", "LAN-M14-D doc must forbid localhost cookies becoming LAN remote trust.");
        AssertContains(lanM14D, "LAN remote terminal trust must require terminal identity and handshake proof", "LAN-M14-D doc must require terminal identity and handshake proof for LAN remote trust.");
        AssertContains(lanM14D, "CORS must not be relaxed unless a later sprint adds explicit guard coverage", "LAN-M14-D doc must forbid unsafe CORS relaxation.");
        AssertContains(lanM14D, "Codex must verify before any LAN-M14-E task", "LAN-M14-D doc must block LAN-M14-E before Codex verification.");

        AssertContains(coreSmoke, "LanM14DBridgeSessionContractStudyChecks.Run();", "CoreSmoke must explicitly wire LAN-M14-D checks.");
    }

    private static void AssertSourceSessionContractBoundaries(string projectRoot, string currentState)
    {
        string apiRoot = Path.Combine(projectRoot, "src", "Salryn.Api.Local");
        string webRoot = Path.Combine(projectRoot, "src", "Salryn.App.Web");
        string hostRoot = Path.Combine(projectRoot, "src", "Salryn.Host.Local");
        string smokeRoot = Path.Combine(projectRoot, "tools", "Salryn.Tools.InfrastructureSmoke");

        string localApiApplicationFactory = ReadRequiredFile(apiRoot, "LocalApiApplicationFactory.cs");
        string remoteReadOnlyService = ReadRequiredFile(apiRoot, "Services", "RemoteReadOnlyApiService.cs");
        string bindPolicyMapper = ReadRequiredFile(apiRoot, "Services", "RemoteReadOnlyLanBindPolicyMapper.cs");
        string localApiLanUrlResolver = ReadRequiredFile(apiRoot, "Services", "LocalApiLanUrlResolver.cs");
        string remoteReadOnlyEndpoints = ReadRequiredFile(apiRoot, "RemoteReadOnlyApiEndpoints.cs");
        string remoteReadOnlyClient = ReadRequiredFile(webRoot, "src", "api", "remoteReadOnlyClient.ts");
        string remoteReadOnlyHook = ReadRequiredFile(webRoot, "src", "hooks", "useRemoteReadOnlyClient.ts");
        string remoteReadOnlyPage = ReadRequiredFile(webRoot, "src", "pages", "RemoteReadOnlyClientPage.tsx");
        string remoteTransport = ReadRequiredFile(hostRoot, "HostCommands", "SalrynHostCommandRemoteTransport.cs");
        string controlledTransportSmoke = ReadRequiredFile(smokeRoot, "ControlledRemoteTransportGateSmokeChecks.cs");
        string pairingHandshakeSmoke = ReadRequiredFile(smokeRoot, "PairingActivationHandshakeSmokeChecks.cs");

        AssertContains(localApiApplicationFactory, "http://127.0.0.1:5299", "LocalApiApplicationFactory must keep default Api.Local URL on 127.0.0.1:5299.");
        AssertNotContains(localApiApplicationFactory, "0.0.0.0", "LocalApiApplicationFactory must not contain wildcard bind.");

        AssertContains(remoteReadOnlyService, "LocalOnlyReadMode", "RemoteReadOnlyApiService must keep LocalOnlyReadMode.");
        AssertContains(remoteReadOnlyService, "LocalhostOnly", "RemoteReadOnlyApiService must keep LocalhostOnly.");
        AssertContains(remoteReadOnlyService, "DisabledByDefault", "RemoteReadOnlyApiService must keep DisabledByDefault.");
        AssertContains(remoteReadOnlyService, "ExplicitHostBindRequired", "RemoteReadOnlyApiService must keep ExplicitHostBindRequired.");
        AssertContains(remoteReadOnlyService, "SpecificIpOnly", "RemoteReadOnlyApiService must keep SpecificIpOnly.");
        AssertContains(remoteReadOnlyService, "NoWildcardBind", "RemoteReadOnlyApiService must keep NoWildcardBind.");
        AssertContains(remoteReadOnlyService, "NoDiscovery", "RemoteReadOnlyApiService must keep NoDiscovery.");

        if (IsLanM14FThroughIImplementationContext(currentState))
        {
            AssertContains(bindPolicyMapper, "HybridBridgeReadyMode", "RemoteReadOnlyLanBindPolicyMapper must define HybridBridgeReadyMode after LAN-M14-F implementation.");
            AssertContains(localApiLanUrlResolver, "ResolveListenUrls", "LocalApiLanUrlResolver must resolve listen URLs after LAN-M14-F implementation.");
            AssertContains(localApiApplicationFactory, "LocalApiLanUrlResolver.ResolveListenUrls", "LocalApiApplicationFactory must wire LocalApiLanUrlResolver after LAN-M14-F implementation.");
        }

        AssertContains(remoteReadOnlyEndpoints, "MapGroup(\"/api/remote-readonly\")", "Remote-readonly endpoint group must remain under /api/remote-readonly.");
        AssertContains(remoteReadOnlyEndpoints, ".MapGet(", "Remote-readonly endpoints must remain GET routes.");
        AssertNotContains(remoteReadOnlyEndpoints, ".MapPost(", "Remote-readonly endpoints must not expose POST.");
        AssertNotContains(remoteReadOnlyEndpoints, ".MapPut(", "Remote-readonly endpoints must not expose PUT.");
        AssertNotContains(remoteReadOnlyEndpoints, ".MapPatch(", "Remote-readonly endpoints must not expose PATCH.");
        AssertNotContains(remoteReadOnlyEndpoints, ".MapDelete(", "Remote-readonly endpoints must not expose DELETE.");

        AssertContains(remoteReadOnlyClient, "/api/remote-readonly", "Remote read-only client must fetch /api/remote-readonly endpoints.");
        AssertContains(remoteReadOnlyClient, "actions are disabled on remote clients", "Remote read-only client must keep disabled mutation warning.");

        AssertContains(remoteReadOnlyHook, "useQuery", "Remote read-only hook must use query reads.");
        AssertContains(remoteReadOnlyHook, "fetchRemoteReadOnlyStatus", "Remote read-only hook must query status.");
        AssertContains(remoteReadOnlyHook, "fetchRemoteReadOnlyProducts", "Remote read-only hook must query products.");
        AssertNotContains(remoteReadOnlyHook, "useMutation", "Remote read-only hook must not use mutations.");

        AssertContains(remoteReadOnlyPage, "No catalog mutation controls are present", "Remote read-only page must explicitly state no mutation controls.");
        AssertNotContains(remoteReadOnlyPage, "Save", "Remote read-only page must not expose save controls.");
        AssertNotContains(remoteReadOnlyPage, "Delete", "Remote read-only page must not expose delete controls.");
        AssertNotContains(remoteReadOnlyPage, "Create", "Remote read-only page must not expose create controls.");

        AssertContains(remoteTransport, "StatusRead or HostCommandCategory.ProductRead or HostCommandCategory.DashboardRead or HostCommandCategory.TerminalStatusRead", "Host command remote transport must whitelist safe read categories only.");
        AssertContains(remoteTransport, "Only safe status, product, dashboard, and terminal status reads", "Host command remote transport must block non-safe categories after handshake.");

        AssertContains(controlledTransportSmoke, "0.0.0.0 must be rejected", "Controlled remote transport smoke must reject 0.0.0.0.");
        AssertContains(pairingHandshakeSmoke, "OfficialCheckoutComplete", "Pairing handshake smoke must block official checkout after handshake.");
        AssertContains(pairingHandshakeSmoke, "TerminalRegistryDisable", "Pairing handshake smoke must block admin mutation after handshake.");
    }

    private static void AssertLanM14DChangedFilesStayInScope(string projectRoot, string currentState)
    {
        if (!currentState.Contains("LAN-M14-D is current candidate work", StringComparison.Ordinal))
        {
            Console.WriteLine("LAN-M14-D changed-file scope guard skipped: LAN-M14-D is no longer current candidate work. Static session contract checks remain active.");
            return;
        }

        string[] changedPaths = GetChangedPaths(projectRoot);

        if (changedPaths.Length == 0)
        {
            Console.WriteLine("LAN-M14-D changed-file scope guard skipped: no branch-diff paths were available. Static session contract checks remain active.");
            return;
        }

        HashSet<string> allowedPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "00-HANDOFF/LAN_M14_D_BRIDGE_SESSION_CONTRACT_STUDY.md",
            "00-HANDOFF/CURRENT_STABLE_STATE_AND_NEXT_WORK.md",
            "tests/Salryn.Tests/M5VerifiedCandidateStackLedgerChecks.cs",
            "tests/Salryn.Tests/M6CandidateStackPromotionDryRunChecks.cs",
            "tests/Salryn.Tests/M7CandidateStackPromotionDebtMeterChecks.cs",
            "tests/Salryn.Tests/M8MSeriesCompletionLanTransitionGateChecks.cs",
            "tests/Salryn.Tests/LanM14APostPromotionScopeAuditChecks.cs",
            "tests/Salryn.Tests/LanM14BExplicitHostBindReadinessChecks.cs",
            "tests/Salryn.Tests/LanM14CRemoteReadonlySessionBridgeCutlineChecks.cs",
            "tests/Salryn.Tests/LanM14DBridgeSessionContractStudyChecks.cs",
            "tools/Salryn.Tools.CoreSmoke/Program.cs"
        };

        foreach (string changedPath in changedPaths)
        {
            string normalizedPath = changedPath.Replace('\\', '/');

            if (!allowedPaths.Contains(normalizedPath))
            {
                throw new InvalidOperationException($"LAN-M14-D changed an out-of-scope file: {normalizedPath}");
            }

            string lowerPath = normalizedPath.ToLowerInvariant();

            if (lowerPath.StartsWith("src/salryn.api.local/", StringComparison.Ordinal)
                || lowerPath.StartsWith("src/salryn.app.web/", StringComparison.Ordinal)
                || lowerPath.StartsWith("src/salryn.desktop.webview/", StringComparison.Ordinal)
                || lowerPath.StartsWith("src/salryn.host.local/", StringComparison.Ordinal)
                || lowerPath.StartsWith("src/salryn.core/", StringComparison.Ordinal)
                || lowerPath.StartsWith("src/salryn.infrastructure/", StringComparison.Ordinal)
                || lowerPath.StartsWith("src/salryn.app.winforms/", StringComparison.Ordinal)
                || lowerPath.Contains("migrations", StringComparison.Ordinal)
                || lowerPath.Contains("seed", StringComparison.Ordinal)
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
                || lowerPath.Contains("discovery", StringComparison.Ordinal)
                || lowerPath.Contains("mobile", StringComparison.Ordinal)
                || lowerPath.Contains("cloud", StringComparison.Ordinal)
                || lowerPath.Contains("roleeditor", StringComparison.Ordinal)
                || lowerPath.Contains("permissioneditor", StringComparison.Ordinal)
                || lowerPath.Contains("lan-m14-e", StringComparison.Ordinal)
                || lowerPath.Contains("bridge-session", StringComparison.Ordinal)
                || lowerPath.Contains("sessionruntime", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"LAN-M14-D changed a forbidden boundary file: {normalizedPath}");
            }
        }
    }

    private static string[] GetChangedPaths(string projectRoot)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddPaths(paths, TryRunGit(projectRoot, "diff --name-only " + LanM14DBaselineSha + "...HEAD"));
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

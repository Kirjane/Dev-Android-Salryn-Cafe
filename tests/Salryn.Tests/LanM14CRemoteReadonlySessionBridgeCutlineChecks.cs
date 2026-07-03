using System.Diagnostics;

namespace Salryn.Tests;

public static class LanM14CRemoteReadonlySessionBridgeCutlineChecks
{
    private const string LanM14CBaselineSha = "05f2e40f7e21bf11fd85887cbb7a09d32f6d6ec8";

    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string handoffRoot = Path.Combine(projectRoot, "00-HANDOFF");
        string currentStatePath = Path.Combine(handoffRoot, "CURRENT_STABLE_STATE_AND_NEXT_WORK.md");
        string lanM14CPath = Path.Combine(handoffRoot, "LAN_M14_C_REMOTE_READONLY_SESSION_BRIDGE_CUTLINE.md");
        string coreSmokePath = Path.Combine(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        string currentState = ReadRequiredFile(currentStatePath);
        string lanM14C = ReadRequiredFile(lanM14CPath);
        string coreSmoke = ReadRequiredFile(coreSmokePath);

        if (IsLaterLanM14CHistoricalContext(currentState))
        {
            AssertContains(currentState, "LAN-M14-C is stable", "Current-state doc must keep LAN-M14-C stable after LAN-M14-D starts.");
            AssertContains(currentState, "LAN-M14-D is current candidate work", "Current-state doc must mark LAN-M14-D as current candidate work.");
            AssertContains(lanM14C, "It does not implement the bridge", "LAN-M14-C doc must remain available as historical bridge cutline context.");
            AssertContains(lanM14C, "LAN-M14-C does not implement LAN", "LAN-M14-C doc must state it does not implement LAN.");
            AssertContains(coreSmoke, "LanM14CRemoteReadonlySessionBridgeCutlineChecks.Run();", "CoreSmoke must keep LAN-M14-C checks wired.");
            AssertSourceBridgeBoundaries(projectRoot);
            AssertLanM14CChangedFilesStayInScope(projectRoot, currentState);
            return;
        }

        AssertDocBoundaries(currentState, lanM14C, coreSmoke);
        AssertSourceBridgeBoundaries(projectRoot);
        AssertLanM14CChangedFilesStayInScope(projectRoot, currentState);
    }

    private static bool IsLaterLanM14CHistoricalContext(string currentState)
    {
        return currentState.Contains("LAN-M14-C is stable", StringComparison.Ordinal)
            && currentState.Contains("LAN-M14-D is current candidate work", StringComparison.Ordinal);
    }

    private static void AssertDocBoundaries(string currentState, string lanM14C, string coreSmoke)
    {
        AssertContains(currentState, LanM14CBaselineSha, "Current-state doc must contain the LAN-M14-C baseline SHA.");
        AssertContains(currentState, "LAN-M14-B is stable", "Current-state doc must state LAN-M14-B is stable.");
        AssertContains(currentState, "LAN-M14-C is current candidate work pending Codex verification", "Current-state doc must mark LAN-M14-C as current candidate pending Codex verification.");
        AssertContains(currentState, "LAN-M14-D is not started", "Current-state doc must state LAN-M14-D is not started.");
        AssertContains(currentState, "LAN bind activation is not started", "Current-state doc must state LAN bind activation is not started.");
        AssertContains(currentState, "Api.Local LAN bind is not started", "Current-state doc must state Api.Local LAN bind is not started.");
        AssertContains(currentState, "Remote read-only session bridge is not implemented", "Current-state doc must state the bridge is not implemented.");
        AssertContains(currentState, "Live remote transport dispatch is not started", "Current-state doc must state live remote transport dispatch is not started.");
        AssertContains(currentState, "LAN mutation is not started", "Current-state doc must state LAN mutation is not started.");
        AssertContains(currentState, "Remote cashier/admin behavior is not started", "Current-state doc must state remote cashier/admin is not started.");
        AssertContains(currentState, "LAN_M14_C_REMOTE_READONLY_SESSION_BRIDGE_CUTLINE.md", "Current-state doc must list LAN-M14-C as live authority.");
        AssertNotContains(currentState, "LAN-M14-B is current candidate work", "Current-state doc must not retain stale LAN-M14-B candidate wording.");
        AssertNotContains(currentState, "LAN-M14-B is current candidate work pending Codex verification", "Current-state doc must not retain stale LAN-M14-B unverified wording.");
        AssertNotContains(currentState, "LAN-M14-C is implemented", "Current-state doc must not claim LAN-M14-C is implemented.");
        AssertNotContains(currentState, "LAN bind is enabled", "Current-state doc must not claim LAN bind is enabled.");
        AssertNotContains(currentState, "LAN runtime is active", "Current-state doc must not claim LAN runtime is active.");
        AssertNotContains(currentState, "bridge is implemented", "Current-state doc must not claim bridge is implemented.");

        AssertContains(lanM14C, "It does not implement the bridge", "LAN-M14-C doc must state it does not implement the bridge.");
        AssertContains(lanM14C, "It does not enable LAN bind", "LAN-M14-C doc must state it does not enable LAN bind.");
        AssertContains(lanM14C, "It does not make Api.Local listen on LAN", "LAN-M14-C doc must state it does not make Api.Local listen on LAN.");
        AssertContains(lanM14C, "It does not enable live remote dispatch", "LAN-M14-C doc must state it does not enable live remote dispatch.");
        AssertContains(lanM14C, "Api.Local HTTP bridge", "LAN-M14-C doc must define Api.Local HTTP bridge option.");
        AssertContains(lanM14C, "Host command transport bridge", "LAN-M14-C doc must define host command transport bridge option.");
        AssertContains(lanM14C, "Hybrid bridge", "LAN-M14-C doc must define hybrid bridge option.");
        AssertContains(lanM14C, "Stop / no bridge yet", "LAN-M14-C doc must define stop/no bridge option.");
        AssertContains(lanM14C, "LAN bind still disabled by default", "LAN-M14-C doc must require disabled by default.");
        AssertContains(lanM14C, "explicit operator enable required", "LAN-M14-C doc must require explicit operator enable.");
        AssertContains(lanM14C, "specific IP only", "LAN-M14-C doc must require specific IP only.");
        AssertContains(lanM14C, "no `0.0.0.0`", "LAN-M14-C doc must forbid 0.0.0.0.");
        AssertContains(lanM14C, "no wildcard bind", "LAN-M14-C doc must forbid wildcard bind.");
        AssertContains(lanM14C, "no discovery", "LAN-M14-C doc must forbid discovery.");
        AssertContains(lanM14C, "no shared SQLite", "LAN-M14-C doc must forbid shared SQLite.");
        AssertContains(lanM14C, "no DB path exposure", "LAN-M14-C doc must forbid DB path exposure.");
        AssertContains(lanM14C, "SQLite path exposure", "LAN-M14-C doc must forbid SQLite path exposure.");
        AssertContains(lanM14C, "no mutation", "LAN-M14-C doc must forbid mutation.");
        AssertContains(lanM14C, "read-only category whitelist", "LAN-M14-C doc must require read-only category whitelist.");
        AssertContains(lanM14C, "terminal identity and handshake required for remote client reads", "LAN-M14-C doc must require terminal identity and handshake before remote client reads.");
        AssertContains(lanM14C, "no cookie/session ambiguity across LAN clients without explicit design", "LAN-M14-C doc must forbid cookie/session ambiguity without explicit design.");
        AssertContains(lanM14C, "no CORS relaxation unless explicitly justified and guarded", "LAN-M14-C doc must forbid unsafe CORS relaxation.");
        AssertContains(lanM14C, "audit trail for bridge enable/disable and remote read attempts", "LAN-M14-C doc must require audit trail before runtime enable.");
        AssertContains(lanM14C, "Codex must verify LAN-M14-C before any LAN-M14-D task", "LAN-M14-C doc must block LAN-M14-D before Codex verification.");
        AssertContains(lanM14C, "Stop for maintainer review", "LAN-M14-C doc must stop for maintainer review before LAN-M14-D.");
        AssertContains(lanM14C, "LAN-M14-C does not implement LAN", "LAN-M14-C doc must state it does not implement LAN.");
        AssertContains(lanM14C, "LAN implementation remains unstarted", "LAN-M14-C doc must state LAN implementation remains unstarted.");

        AssertContains(coreSmoke, "LanM14CRemoteReadonlySessionBridgeCutlineChecks.Run();", "CoreSmoke must explicitly wire LAN-M14-C checks.");
    }

    private static void AssertSourceBridgeBoundaries(string projectRoot)
    {
        string apiRoot = Path.Combine(projectRoot, "src", "Salryn.Api.Local");
        string webRoot = Path.Combine(projectRoot, "src", "Salryn.App.Web");
        string desktopWebViewRoot = Path.Combine(projectRoot, "src", "Salryn.Desktop.WebView");
        string hostRoot = Path.Combine(projectRoot, "src", "Salryn.Host.Local");
        string smokeRoot = Path.Combine(projectRoot, "tools", "Salryn.Tools.InfrastructureSmoke");

        string localApiApplicationFactory = ReadRequiredFile(apiRoot, "LocalApiApplicationFactory.cs");
        string remoteReadOnlyService = ReadRequiredFile(apiRoot, "Services", "RemoteReadOnlyApiService.cs");
        string remoteReadOnlyEndpoints = ReadRequiredFile(apiRoot, "RemoteReadOnlyApiEndpoints.cs");
        string remoteReadOnlyClient = ReadRequiredFile(webRoot, "src", "api", "remoteReadOnlyClient.ts");
        string remoteReadOnlyHook = ReadRequiredFile(webRoot, "src", "hooks", "useRemoteReadOnlyClient.ts");
        string remoteReadOnlyPage = ReadRequiredFile(webRoot, "src", "pages", "RemoteReadOnlyClientPage.tsx");
        string localWebOriginPolicy = ReadRequiredFile(desktopWebViewRoot, "LocalWebOriginPolicy.cs");
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
        AssertContains(remoteReadOnlyService, "Official journal writes disabled", "RemoteReadOnlyApiService must keep official journal writes disabled.");
        AssertContains(remoteReadOnlyService, "Official sequence allocation disabled", "RemoteReadOnlyApiService must keep official sequence allocation disabled.");

        AssertContains(remoteReadOnlyEndpoints, "MapGroup(\"/api/remote-readonly\")", "Remote-readonly endpoint group must remain under /api/remote-readonly.");
        AssertContains(remoteReadOnlyEndpoints, ".MapGet(\"/status\"", "Remote-readonly status route must remain GET.");
        AssertContains(remoteReadOnlyEndpoints, ".MapGet(\"/capabilities\"", "Remote-readonly capabilities route must remain GET.");
        AssertContains(remoteReadOnlyEndpoints, ".MapGet(\"/dashboard-summary\"", "Remote-readonly dashboard route must remain GET.");
        AssertContains(remoteReadOnlyEndpoints, ".MapGet(\"/products\"", "Remote-readonly product route must remain GET.");
        AssertContains(remoteReadOnlyEndpoints, ".MapGet(\"/terminal-status\"", "Remote-readonly terminal route must remain GET.");
        AssertContains(remoteReadOnlyEndpoints, ".MapGet(\"/bind-policy\"", "Remote-readonly bind policy route must remain GET.");
        AssertContains(remoteReadOnlyEndpoints, ".MapGet(\"/audit\"", "Remote-readonly audit route must remain GET.");
        AssertNotContains(remoteReadOnlyEndpoints, ".MapPost(", "Remote-readonly endpoints must not expose POST.");
        AssertNotContains(remoteReadOnlyEndpoints, ".MapPut(", "Remote-readonly endpoints must not expose PUT.");
        AssertNotContains(remoteReadOnlyEndpoints, ".MapPatch(", "Remote-readonly endpoints must not expose PATCH.");
        AssertNotContains(remoteReadOnlyEndpoints, ".MapDelete(", "Remote-readonly endpoints must not expose DELETE.");

        AssertContains(remoteReadOnlyClient, "/api/remote-readonly/status", "Remote read-only client must fetch same-origin status endpoint.");
        AssertContains(remoteReadOnlyClient, "/api/remote-readonly/products", "Remote read-only client must fetch same-origin product endpoint.");
        AssertContains(remoteReadOnlyClient, "/api/remote-readonly/dashboard-summary", "Remote read-only client must fetch same-origin dashboard endpoint.");
        AssertContains(remoteReadOnlyClient, "/api/remote-readonly/terminal-status", "Remote read-only client must fetch same-origin terminal endpoint.");
        AssertContains(remoteReadOnlyClient, "/api/remote-readonly/audit", "Remote read-only client must fetch same-origin audit endpoint.");
        AssertContains(remoteReadOnlyClient, "Remote cashier/admin execution is not enabled", "Remote read-only client must keep cashier/admin disabled warning.");
        AssertContains(remoteReadOnlyClient, "actions are disabled on remote clients", "Remote read-only client must keep disabled mutation warning.");

        AssertContains(remoteReadOnlyHook, "useQuery", "Remote read-only hook must use query reads.");
        AssertContains(remoteReadOnlyHook, "fetchRemoteReadOnlyStatus", "Remote read-only hook must query status.");
        AssertContains(remoteReadOnlyHook, "fetchRemoteReadOnlyProducts", "Remote read-only hook must query products.");
        AssertContains(remoteReadOnlyHook, "fetchRemoteReadOnlyAuditVisibility", "Remote read-only hook must query audit visibility.");
        AssertNotContains(remoteReadOnlyHook, "useMutation", "Remote read-only hook must not use mutations.");

        AssertContains(remoteReadOnlyPage, "Refresh read-only state", "Remote read-only page must expose refresh-only action.");
        AssertContains(remoteReadOnlyPage, "Search read-only", "Remote read-only page must expose read-only search.");
        AssertContains(remoteReadOnlyPage, "No catalog mutation controls are present", "Remote read-only page must explicitly state no mutation controls.");
        AssertNotContains(remoteReadOnlyPage, "Save", "Remote read-only page must not expose save controls.");
        AssertNotContains(remoteReadOnlyPage, "Delete", "Remote read-only page must not expose delete controls.");
        AssertNotContains(remoteReadOnlyPage, "Create", "Remote read-only page must not expose create controls.");

        AssertContains(localWebOriginPolicy, "127.0.0.1", "LocalWebOriginPolicy must allow only local 127.0.0.1 origins.");
        AssertContains(localWebOriginPolicy, "localhost", "LocalWebOriginPolicy must allow only localhost origins.");
        AssertContains(localWebOriginPolicy, "LAN addresses are blocked", "LocalWebOriginPolicy must block LAN addresses.");
        AssertContains(localWebOriginPolicy, "javascript", "LocalWebOriginPolicy must block javascript navigation.");

        AssertContains(remoteTransport, "IRemoteClientHandshakeService", "Host command remote transport must use handshake service.");
        AssertContains(remoteTransport, "RemoteHandshake.SessionRequired", "Host command remote transport must require handshake session.");
        AssertContains(remoteTransport, "StatusRead or HostCommandCategory.ProductRead or HostCommandCategory.DashboardRead or HostCommandCategory.TerminalStatusRead", "Host command remote transport must whitelist safe read categories only.");
        AssertContains(remoteTransport, "Only safe status, product, dashboard, and terminal status reads", "Host command remote transport must block non-safe categories after handshake.");

        AssertContains(controlledTransportSmoke, "0.0.0.0 must be rejected", "Controlled remote transport smoke must reject 0.0.0.0.");
        AssertContains(controlledTransportSmoke, "Remote enabled active terminal must allow safe status/read command after handshake", "Controlled remote transport smoke must allow safe read after handshake.");
        AssertContains(controlledTransportSmoke, "Official mutation must stay blocked remotely", "Controlled remote transport smoke must block official mutation.");

        AssertContains(pairingHandshakeSmoke, "OfficialCheckoutComplete", "Pairing handshake smoke must block official checkout after handshake.");
        AssertContains(pairingHandshakeSmoke, "TerminalRegistryDisable", "Pairing handshake smoke must block admin mutation after handshake.");
        AssertContains(pairingHandshakeSmoke, "StatusRead", "Pairing handshake smoke must allow safe status reads.");
        AssertContains(pairingHandshakeSmoke, "ProductRead", "Pairing handshake smoke must allow safe product reads.");
    }

    private static void AssertLanM14CChangedFilesStayInScope(string projectRoot, string currentState)
    {
        if (!currentState.Contains("LAN-M14-C is current candidate work", StringComparison.Ordinal))
        {
            Console.WriteLine("LAN-M14-C changed-file scope guard skipped: LAN-M14-C is no longer current candidate work. Static bridge cutline checks remain active.");
            return;
        }

        string[] changedPaths = GetChangedPaths(projectRoot);

        if (changedPaths.Length == 0)
        {
            Console.WriteLine("LAN-M14-C changed-file scope guard skipped: no branch-diff paths were available. Static bridge cutline checks remain active.");
            return;
        }

        HashSet<string> allowedPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "00-HANDOFF/LAN_M14_C_REMOTE_READONLY_SESSION_BRIDGE_CUTLINE.md",
            "00-HANDOFF/CURRENT_STABLE_STATE_AND_NEXT_WORK.md",
            "tests/Salryn.Tests/M5VerifiedCandidateStackLedgerChecks.cs",
            "tests/Salryn.Tests/M6CandidateStackPromotionDryRunChecks.cs",
            "tests/Salryn.Tests/M7CandidateStackPromotionDebtMeterChecks.cs",
            "tests/Salryn.Tests/M8MSeriesCompletionLanTransitionGateChecks.cs",
            "tests/Salryn.Tests/LanM14APostPromotionScopeAuditChecks.cs",
            "tests/Salryn.Tests/LanM14BExplicitHostBindReadinessChecks.cs",
            "tests/Salryn.Tests/LanM14CRemoteReadonlySessionBridgeCutlineChecks.cs",
            "tools/Salryn.Tools.CoreSmoke/Program.cs"
        };

        foreach (string changedPath in changedPaths)
        {
            string normalizedPath = changedPath.Replace('\\', '/');

            if (!allowedPaths.Contains(normalizedPath))
            {
                throw new InvalidOperationException($"LAN-M14-C changed an out-of-scope file: {normalizedPath}");
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
                || lowerPath.Contains("lan-m14-d", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"LAN-M14-C changed a forbidden boundary file: {normalizedPath}");
            }
        }
    }

    private static string[] GetChangedPaths(string projectRoot)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddPaths(paths, TryRunGit(projectRoot, "diff --name-only " + LanM14CBaselineSha + "...HEAD"));
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

using System.Diagnostics;

namespace Salryn.Tests;

public static class LanM14BExplicitHostBindReadinessChecks
{
    private const string LanM14BBaselineSha = "d8f2a8b346c3128336a14899567369278b801c75";

    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string handoffRoot = Path.Combine(projectRoot, "00-HANDOFF");
        string currentStatePath = Path.Combine(handoffRoot, "CURRENT_STABLE_STATE_AND_NEXT_WORK.md");
        string lanM14BPath = Path.Combine(handoffRoot, "LAN_M14_B_EXPLICIT_HOST_BIND_READINESS_AND_GUARDED_RUNTIME_CUTLINE.md");
        string coreSmokePath = Path.Combine(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        string currentState = ReadRequiredFile(currentStatePath);
        string lanM14B = ReadRequiredFile(lanM14BPath);
        string coreSmoke = ReadRequiredFile(coreSmokePath);

        if (IsLanM14CCurrentCandidate(currentState))
        {
            AssertContains(currentState, "LAN-M14-B is stable", "Current-state doc must keep LAN-M14-B stable after LAN-M14-C starts.");
            AssertContains(currentState, "LAN-M14-C is current candidate work pending Codex verification", "Current-state doc must mark LAN-M14-C as current candidate work.");
            AssertContains(currentState, "LAN-M14-D is not started", "Current-state doc must state LAN-M14-D is not started.");
            AssertContains(lanM14B, "LAN-M14-B does not implement LAN", "LAN-M14-B doc must remain available as historical host-bind cutline context.");
            AssertContains(coreSmoke, "LanM14BExplicitHostBindReadinessChecks.Run();", "CoreSmoke must keep LAN-M14-B checks wired.");
            AssertLanM14BChangedFilesStayInScope(projectRoot, currentState);
            return;
        }

        AssertDocBoundaries(currentState, lanM14B, coreSmoke);
        AssertSourceHostBindBoundaries(projectRoot);
        AssertLanM14BChangedFilesStayInScope(projectRoot, currentState);
    }

    private static void AssertDocBoundaries(string currentState, string lanM14B, string coreSmoke)
    {
        AssertContains(currentState, LanM14BBaselineSha, "Current-state doc must contain the LAN-M14-B baseline SHA.");
        AssertContains(currentState, "LAN-M14-A is stable", "Current-state doc must state LAN-M14-A is stable.");
        AssertContains(currentState, "LAN-M14-B is current candidate work pending Codex verification", "Current-state doc must mark LAN-M14-B as current candidate pending Codex verification.");
        AssertContains(currentState, "LAN-M14-C is not started", "Current-state doc must state LAN-M14-C is not started.");
        AssertContains(currentState, "LAN bind activation is not started", "Current-state doc must state LAN bind activation is not started.");
        AssertContains(currentState, "Api.Local LAN bind is not started", "Current-state doc must state Api.Local LAN bind is not started.");
        AssertContains(currentState, "Live remote transport dispatch is not started", "Current-state doc must state live remote transport dispatch is not started.");
        AssertContains(currentState, "LAN mutation is not started", "Current-state doc must state LAN mutation is not started.");
        AssertContains(currentState, "Remote cashier/admin behavior is not started", "Current-state doc must state remote cashier/admin is not started.");
        AssertContains(currentState, "LAN_M14_B_EXPLICIT_HOST_BIND_READINESS_AND_GUARDED_RUNTIME_CUTLINE.md", "Current-state doc must list LAN-M14-B as live authority.");
        AssertNotContains(currentState, "LAN-M14-A is current candidate work", "Current-state doc must not retain stale LAN-M14-A candidate wording.");
        AssertNotContains(currentState, "LAN-M14-A is current candidate work and is not verified", "Current-state doc must not retain stale LAN-M14-A unverified wording.");
        AssertNotContains(currentState, "LAN-M14-B is implemented", "Current-state doc must not claim LAN-M14-B is implemented.");
        AssertNotContains(currentState, "LAN bind is enabled", "Current-state doc must not claim LAN bind is enabled.");
        AssertNotContains(currentState, "LAN runtime is active", "Current-state doc must not claim LAN runtime is active.");

        AssertContains(lanM14B, "It does not enable LAN bind", "LAN-M14-B doc must state it does not enable LAN bind.");
        AssertContains(lanM14B, "It does not make Api.Local listen on LAN", "LAN-M14-B doc must state it does not make Api.Local listen on LAN.");
        AssertContains(lanM14B, "It does not enable live remote dispatch", "LAN-M14-B doc must state it does not enable live remote dispatch.");
        AssertContains(lanM14B, "Api.Local HTTP bind exposure", "LAN-M14-B doc must define Api.Local HTTP bind ownership.");
        AssertContains(lanM14B, "Host command remote transport dispatch", "LAN-M14-B doc must define host command transport ownership.");
        AssertContains(lanM14B, "Remote-readonly Web client consumption", "LAN-M14-B doc must define remote-readonly Web ownership.");
        AssertContains(lanM14B, "Terminal pairing/handshake identity", "LAN-M14-B doc must define terminal pairing/handshake ownership.");
        AssertContains(lanM14B, "Operator settings/control plane", "LAN-M14-B doc must define operator control-plane ownership.");
        AssertContains(lanM14B, "disabled by default", "LAN-M14-B doc must require disabled by default.");
        AssertContains(lanM14B, "explicit operator action required", "LAN-M14-B doc must require explicit operator action.");
        AssertContains(lanM14B, "specific IP only", "LAN-M14-B doc must require specific IP only.");
        AssertContains(lanM14B, "no `0.0.0.0`", "LAN-M14-B doc must forbid 0.0.0.0.");
        AssertContains(lanM14B, "no wildcard bind", "LAN-M14-B doc must forbid wildcard bind.");
        AssertContains(lanM14B, "no discovery", "LAN-M14-B doc must forbid discovery.");
        AssertContains(lanM14B, "no shared SQLite", "LAN-M14-B doc must forbid shared SQLite.");
        AssertContains(lanM14B, "no DB path exposure", "LAN-M14-B doc must forbid DB path exposure.");
        AssertContains(lanM14B, "SQLite path exposure", "LAN-M14-B doc must forbid SQLite path exposure.");
        AssertContains(lanM14B, "no mutation", "LAN-M14-B doc must forbid mutation.");
        AssertContains(lanM14B, "read-only category whitelist", "LAN-M14-B doc must require read-only category whitelist.");
        AssertContains(lanM14B, "authenticated/paired terminal required before remote reads", "LAN-M14-B doc must require authenticated/paired terminal before remote reads.");
        AssertContains(lanM14B, "smoke coverage before any runtime enable", "LAN-M14-B doc must require smoke coverage before runtime enable.");
        AssertContains(lanM14B, "Codex must verify LAN-M14-B before any LAN-M14-C task", "LAN-M14-B doc must block LAN-M14-C before Codex verification.");
        AssertContains(lanM14B, "Stop for maintainer review", "LAN-M14-B doc must stop for maintainer review before LAN-M14-C.");
        AssertContains(lanM14B, "LAN-M14-B does not implement LAN", "LAN-M14-B doc must state it does not implement LAN.");
        AssertContains(lanM14B, "LAN implementation remains unstarted", "LAN-M14-B doc must state LAN implementation remains unstarted.");

        AssertContains(coreSmoke, "LanM14BExplicitHostBindReadinessChecks.Run();", "CoreSmoke must explicitly wire LAN-M14-B checks.");
    }

    private static void AssertSourceHostBindBoundaries(string projectRoot)
    {
        string apiRoot = Path.Combine(projectRoot, "src", "Salryn.Api.Local");
        string desktopWebViewRoot = Path.Combine(projectRoot, "src", "Salryn.Desktop.WebView");
        string hostRoot = Path.Combine(projectRoot, "src", "Salryn.Host.Local");
        string smokeRoot = Path.Combine(projectRoot, "tools", "Salryn.Tools.InfrastructureSmoke");

        string localApiApplicationFactory = ReadRequiredFile(apiRoot, "LocalApiApplicationFactory.cs");
        string apiProgram = ReadRequiredFile(apiRoot, "Program.cs");
        string remoteReadOnlyService = ReadRequiredFile(apiRoot, "Services", "RemoteReadOnlyApiService.cs");
        string remoteReadOnlyEndpoints = ReadRequiredFile(apiRoot, "RemoteReadOnlyApiEndpoints.cs");
        string devHostConfig = ReadRequiredFile(desktopWebViewRoot, "DevHostConfig.cs");
        string localWebOriginPolicy = ReadRequiredFile(desktopWebViewRoot, "LocalWebOriginPolicy.cs");
        string remoteTransport = ReadRequiredFile(hostRoot, "HostCommands", "SalrynHostCommandRemoteTransport.cs");
        string controlledTransportSmoke = ReadRequiredFile(smokeRoot, "ControlledRemoteTransportGateSmokeChecks.cs");
        string pairingHandshakeSmoke = ReadRequiredFile(smokeRoot, "PairingActivationHandshakeSmokeChecks.cs");
        string remoteLanUsabilitySmoke = ReadRequiredFile(smokeRoot, "RemoteReadOnlyLanUsabilitySmokeChecks.cs");

        AssertContains(localApiApplicationFactory, "http://127.0.0.1:5299", "LocalApiApplicationFactory must keep default Api.Local URL on 127.0.0.1:5299.");
        AssertNotContains(localApiApplicationFactory, "0.0.0.0", "LocalApiApplicationFactory must not contain wildcard bind.");
        AssertContains(apiProgram, "LocalApiApplicationFactory.CreateAsync(args)", "Api.Local Program must delegate binding to LocalApiApplicationFactory.");
        AssertNotContains(apiProgram, "0.0.0.0", "Api.Local Program must not contain wildcard bind.");

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

        AssertContains(devHostConfig, "only ever targets localhost", "DevHostConfig must document localhost-only host behavior.");
        AssertContains(devHostConfig, "never exposes anything to the LAN", "DevHostConfig must document no LAN exposure.");
        AssertContains(devHostConfig, "http://127.0.0.1:5299", "DevHostConfig must keep local Api base URL.");

        AssertContains(localWebOriginPolicy, "127.0.0.1", "LocalWebOriginPolicy must allow only local 127.0.0.1 origins.");
        AssertContains(localWebOriginPolicy, "localhost", "LocalWebOriginPolicy must allow only localhost origins.");
        AssertContains(localWebOriginPolicy, "LAN addresses are blocked", "LocalWebOriginPolicy must block LAN addresses.");
        AssertContains(localWebOriginPolicy, "javascript", "LocalWebOriginPolicy must block javascript navigation.");
        AssertContains(localWebOriginPolicy, "file", "LocalWebOriginPolicy must block file navigation.");
        AssertContains(localWebOriginPolicy, "data", "LocalWebOriginPolicy must block data navigation.");

        AssertContains(remoteTransport, "ILanRemoteTransportGate", "Host command remote transport must use the LAN remote transport gate.");
        AssertContains(remoteTransport, "IRemoteClientHandshakeService", "Host command remote transport must use handshake service.");
        AssertContains(remoteTransport, "RemoteHandshake.SessionRequired", "Host command remote transport must require handshake session.");
        AssertContains(remoteTransport, "StatusRead or HostCommandCategory.ProductRead or HostCommandCategory.DashboardRead or HostCommandCategory.TerminalStatusRead", "Host command remote transport must whitelist safe read categories only.");
        AssertContains(remoteTransport, "Only safe status, product, dashboard, and terminal status reads", "Host command remote transport must block non-safe categories after handshake.");

        AssertContains(controlledTransportSmoke, "0.0.0.0", "Controlled remote transport smoke must exercise wildcard rejection.");
        AssertContains(controlledTransportSmoke, "0.0.0.0 must be rejected", "Controlled remote transport smoke must require wildcard rejection.");
        AssertContains(controlledTransportSmoke, "LanRemoteTransportBindMode.ExplicitLanAddress", "Controlled remote transport smoke must use explicit LAN address config concept.");
        AssertContains(controlledTransportSmoke, "192.168.1.10", "Controlled remote transport smoke must use a specific LAN address config concept.");
        AssertContains(controlledTransportSmoke, "Official mutation must stay blocked remotely", "Controlled remote transport smoke must block official mutation.");
        AssertContains(controlledTransportSmoke, "Remote enabled active terminal must allow safe status/read command after handshake", "Controlled remote transport smoke must allow safe read after handshake.");

        AssertContains(pairingHandshakeSmoke, "OfficialCheckoutComplete", "Pairing handshake smoke must block official checkout after handshake.");
        AssertContains(pairingHandshakeSmoke, "OfficialLifecycleAppend", "Pairing handshake smoke must block official lifecycle append after handshake.");
        AssertContains(pairingHandshakeSmoke, "OfficialReadingFinalizeZ", "Pairing handshake smoke must block Z finalize after handshake.");
        AssertContains(pairingHandshakeSmoke, "TerminalRegistryDisable", "Pairing handshake smoke must block admin mutation after handshake.");
        AssertContains(pairingHandshakeSmoke, "StatusRead", "Pairing handshake smoke must allow safe status reads.");
        AssertContains(pairingHandshakeSmoke, "ProductRead", "Pairing handshake smoke must allow safe product reads.");

        AssertContains(remoteLanUsabilitySmoke, "Api.Local must remain localhost-only", "Remote read-only LAN usability smoke must keep Api.Local localhost-only.");
        AssertContains(remoteLanUsabilitySmoke, "All-interface bind must not exist", "Remote read-only LAN usability smoke must reject all-interface bind.");
        AssertContains(remoteLanUsabilitySmoke, "Remote-readonly route must be GET-only", "Remote read-only LAN usability smoke must enforce GET-only route shape.");
        AssertContains(remoteLanUsabilitySmoke, "Remote-readonly API service must not reference mutation path", "Remote read-only LAN usability smoke must reject remote-readonly mutations.");

        AssertExistingLanTransportConfigConcepts(projectRoot);
    }

    private static void AssertExistingLanTransportConfigConcepts(string projectRoot)
    {
        string[] csFiles = Directory.GetFiles(Path.Combine(projectRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsIgnoredSourceSearchPath(path))
            .ToArray();

        string allSource = string.Join("\n", csFiles.Select(File.ReadAllText));
        AssertContains(allSource, "LanRemoteTransportConfig", "Repo must retain existing LanRemoteTransportConfig concepts.");
        AssertContains(allSource, "ILanRemoteTransportGate", "Repo must retain existing LAN remote transport gate concepts.");
        AssertContains(allSource, "ExplicitLanAddress", "Repo must retain explicit LAN address configuration concept.");
        AssertContains(allSource, "NoWildcardBind", "Repo must retain no-wildcard bind policy concept.");
    }

    private static void AssertLanM14BChangedFilesStayInScope(string projectRoot, string currentState)
    {
        if (IsLanM14CCurrentCandidate(currentState))
        {
            Console.WriteLine("LAN-M14-B changed-file scope guard skipped: LAN-M14-B has been promoted. Static host-bind checks remain active.");
            return;
        }

        if (!currentState.Contains("LAN-M14-B is current candidate work", StringComparison.Ordinal))
        {
            Console.WriteLine("LAN-M14-B changed-file scope guard skipped: LAN-M14-B is no longer current candidate work. Static host-bind checks remain active.");
            return;
        }

        string[] changedPaths = GetChangedPaths(projectRoot);

        if (changedPaths.Length == 0)
        {
            Console.WriteLine("LAN-M14-B changed-file scope guard skipped: no branch-diff paths were available. Static host-bind checks remain active.");
            return;
        }

        HashSet<string> allowedPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "00-HANDOFF/LAN_M14_B_EXPLICIT_HOST_BIND_READINESS_AND_GUARDED_RUNTIME_CUTLINE.md",
            "00-HANDOFF/CURRENT_STABLE_STATE_AND_NEXT_WORK.md",
            "tests/Salryn.Tests/M5VerifiedCandidateStackLedgerChecks.cs",
            "tests/Salryn.Tests/M6CandidateStackPromotionDryRunChecks.cs",
            "tests/Salryn.Tests/M7CandidateStackPromotionDebtMeterChecks.cs",
            "tests/Salryn.Tests/M8MSeriesCompletionLanTransitionGateChecks.cs",
            "tests/Salryn.Tests/LanM14APostPromotionScopeAuditChecks.cs",
            "tests/Salryn.Tests/LanM14BExplicitHostBindReadinessChecks.cs",
            "tools/Salryn.Tools.CoreSmoke/Program.cs"
        };

        foreach (string changedPath in changedPaths)
        {
            string normalizedPath = changedPath.Replace('\\', '/');

            if (!allowedPaths.Contains(normalizedPath))
            {
                throw new InvalidOperationException($"LAN-M14-B changed an out-of-scope file: {normalizedPath}");
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
                || lowerPath.Contains("lan-m14-c", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"LAN-M14-B changed a forbidden boundary file: {normalizedPath}");
            }
        }
    }

    private static string[] GetChangedPaths(string projectRoot)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddPaths(paths, TryRunGit(projectRoot, "diff --name-only " + LanM14BBaselineSha + "...HEAD"));
        AddPaths(paths, TryRunGit(projectRoot, "diff --name-only"));
        AddPaths(paths, TryRunGit(projectRoot, "diff --name-only --cached"));
        AddPaths(paths, TryRunGit(projectRoot, "ls-files --others --exclude-standard"));

        return paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsLanM14CCurrentCandidate(string currentState)
    {
        return currentState.Contains("LAN-M14-B is stable", StringComparison.Ordinal)
            && currentState.Contains("LAN-M14-C is current candidate work", StringComparison.Ordinal);
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

    private static bool IsIgnoredSourceSearchPath(string path)
    {
        string normalized = path.Replace('\\', '/');
        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/.git/", StringComparison.OrdinalIgnoreCase);
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

using System.Diagnostics;

namespace Salryn.Tests;

public static class LanM15ARemoteReadonlyLanFieldHardeningChecks
{
    private const string LanM15ABaselineSha = "e477699dca27a21954a5ad9d388307fdb9ed08fd";

    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string handoffRoot = Path.Combine(projectRoot, "00-HANDOFF");
        string currentStatePath = Path.Combine(handoffRoot, "CURRENT_STABLE_STATE_AND_NEXT_WORK.md");
        string lanM15APath = Path.Combine(handoffRoot, "LAN_M15_A_REMOTE_READONLY_LAN_FIELD_HARDENING_AND_OPERATOR_DIAGNOSTICS.md");
        string coreSmokePath = Path.Combine(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string infraSmokePath = Path.Combine(projectRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "Program.cs");

        string currentState = ReadRequiredFile(currentStatePath);
        string lanM15A = ReadRequiredFile(lanM15APath);
        string coreSmoke = ReadRequiredFile(coreSmokePath);
        string infraSmoke = ReadRequiredFile(infraSmokePath);

        AssertDocBoundaries(currentState, lanM15A, coreSmoke, infraSmoke, handoffRoot);
        AssertSourceFieldHardeningBoundaries(projectRoot);
        AssertLanM15AChangedFilesStayInScope(projectRoot, currentState);
    }

    private static void AssertDocBoundaries(
        string currentState,
        string lanM15A,
        string coreSmoke,
        string infraSmoke,
        string handoffRoot)
    {
        AssertContains(currentState, LanM15ABaselineSha, "Current-state doc must contain the stable Sandbox SHA.");
        AssertContains(currentState, "LAN-M14-A through LAN-M14-E are stable", "Current-state doc must keep LAN-M14-A through LAN-M14-E stable.");
        AssertContains(currentState, "LAN-M14-F through LAN-M14-I are completed candidate work on Freehand", "Current-state doc must mark LAN-M14-F through LAN-M14-I as unpromoted candidate work.");
        AssertContains(currentState, "LAN-M15-A remains completed but unpromoted on Freehand", "Current-state doc must mark LAN-M15-A as unpromoted.");
        AssertContains(currentState, "LAN-M14-I remains the LAN-M14 cap", "Current-state doc must state LAN-M14-I remains the LAN-M14 cap.");
        AssertContains(currentState, "LAN-M14-J does not exist and must not be created", "Current-state doc must forbid LAN-M14-J.");
        AssertContains(currentState, "no Sandbox promotion has occurred", "Current-state doc must state no Sandbox promotion has occurred.");
        AssertNotContains(currentState, "LAN-M14-J is current", "Current-state doc must not mark LAN-M14-J as current work.");

        string docPath = Path.Combine(handoffRoot, "LAN_M15_A_REMOTE_READONLY_LAN_FIELD_HARDENING_AND_OPERATOR_DIAGNOSTICS.md");
        if (!File.Exists(docPath))
        {
            throw new InvalidOperationException("Required LAN-M15-A doc missing.");
        }

        AssertContains(currentState, "LAN_M15_A_REMOTE_READONLY_LAN_FIELD_HARDENING_AND_OPERATOR_DIAGNOSTICS.md", "Current-state doc must list LAN-M15-A doc as live authority.");
        AssertContains(lanM15A, LanM15ABaselineSha, "LAN-M15-A doc must contain the source stamp SHA.");
        AssertContains(lanM15A, "RemoteReadOnlyBridgeDiagnostics", "LAN-M15-A doc must document RemoteReadOnlyBridgeDiagnostics.");
        AssertContains(lanM15A, "/api/remote-readonly/diagnostics", "LAN-M15-A doc must document diagnostics endpoint.");
        AssertContains(lanM15A, "CookieTrustRejected", "LAN-M15-A doc must document cookie trust rejection.");
        AssertContains(lanM15A, "Forbidden Scope", "LAN-M15-A doc must define forbidden scope.");
        AssertContains(lanM15A, "Decision", "LAN-M15-A doc must include a decision section.");
        AssertNotContains(lanM15A, "LAN-M14-J is current", "LAN-M15-A doc must not mark LAN-M14-J as current work.");

        AssertContains(coreSmoke, "LanM15ARemoteReadonlyLanFieldHardeningChecks.Run();", "CoreSmoke must wire LAN-M15-A checks.");
        AssertContains(infraSmoke, "remote-readonly-lan-field-hardening", "InfrastructureSmoke must wire remote-readonly-lan-field-hardening command.");
        AssertContains(infraSmoke, "LanM15RemoteReadOnlyLanFieldHardeningSmokeChecks.Run", "InfrastructureSmoke must call LAN-M15-A field hardening smoke.");
    }

    private static void AssertSourceFieldHardeningBoundaries(string projectRoot)
    {
        string apiRoot = Path.Combine(projectRoot, "src", "Salryn.Api.Local");
        string webRoot = Path.Combine(projectRoot, "src", "Salryn.App.Web", "src");

        string diagnostics = ReadRequiredFile(apiRoot, "Services", "RemoteReadOnlyBridgeDiagnostics.cs");
        string bridgeContext = ReadRequiredFile(apiRoot, "Bridge", "RemoteReadOnlyBridgeRequestContext.cs");
        string bridgeAuth = ReadRequiredFile(apiRoot, "Services", "RemoteReadOnlyApiAuthorization.cs");
        string remoteReadOnlyEndpoints = ReadRequiredFile(apiRoot, "RemoteReadOnlyApiEndpoints.cs");
        string remoteReadOnlyPage = ReadRequiredFile(webRoot, "pages", "RemoteReadOnlyClientPage.tsx");
        string smoke = ReadRequiredFile(projectRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "LanM15RemoteReadOnlyLanFieldHardeningSmokeChecks.cs");

        AssertContains(diagnostics, "CookieTrustRejectedForRemoteLan", "Diagnostics must define cookie trust rejection code.");
        AssertContains(diagnostics, "TerminalHeaderMissing", "Diagnostics must define terminal header missing code.");
        AssertContains(diagnostics, "HandshakeHeaderMissing", "Diagnostics must define handshake header missing code.");
        AssertContains(diagnostics, "SanitizePublicText", "Diagnostics must sanitize public text.");
        AssertContains(diagnostics, "[redacted]", "Diagnostics must redact secrets.");

        AssertContains(bridgeContext, "HasOperatorSessionCookie", "Bridge request context must expose operator session cookie presence.");
        AssertContains(bridgeContext, "IsLoopbackRequest", "Bridge request context must expose loopback classification.");
        AssertContains(bridgeContext, "HasTerminalHeader", "Bridge request context must expose terminal header presence.");
        AssertContains(bridgeContext, "HasHandshakeHeader", "Bridge request context must expose handshake header presence.");

        AssertContains(bridgeAuth, "ResolveBridgeDenial", "Bridge authorization must resolve granular bridge denials.");
        AssertContains(bridgeAuth, "MapHandshakeFailure", "Bridge authorization must map handshake failures to safe diagnostics.");
        AssertContains(bridgeAuth, "diagnostic", "Bridge rejection payload must include diagnostic metadata.");

        AssertContains(remoteReadOnlyEndpoints, "/diagnostics", "Remote-readonly endpoints must expose diagnostics route.");
        AssertNotContains(remoteReadOnlyEndpoints, ".MapPost(", "Remote-readonly endpoints must remain GET-only.");

        AssertContains(remoteReadOnlyPage, "Runtime Diagnostics", "Remote read-only page must expose runtime diagnostics section.");
        AssertContains(remoteReadOnlyPage, "Cookie LAN trust", "Remote read-only page must show cookie LAN trust status.");
        AssertContains(remoteReadOnlyPage, "No catalog mutation controls are present", "Remote read-only page must keep mutation controls absent.");

        AssertContains(smoke, "DefaultHttpContext", "LAN-M15-A smoke must classify bridge requests with DefaultHttpContext.");
        AssertContains(smoke, "CookieTrustRejectedForRemoteLan", "LAN-M15-A smoke must test cookie trust rejection.");
        AssertContains(smoke, "TerminalHeaderMissing", "LAN-M15-A smoke must test terminal header missing denial.");
    }

    private static void AssertLanM15AChangedFilesStayInScope(string projectRoot, string currentState)
    {
        if (!currentState.Contains("LAN-M15-A is current candidate work on Freehand", StringComparison.Ordinal)
            || currentState.Contains("LAN-M15-B is current candidate work on Freehand", StringComparison.Ordinal))
        {
            Console.WriteLine("LAN-M15-A changed-file scope guard skipped: LAN-M15-A is no longer current candidate work. Static field hardening checks remain active.");
            return;
        }

        string[] changedPaths = GetChangedPaths(projectRoot);

        if (changedPaths.Length == 0)
        {
            Console.WriteLine("LAN-M15-A changed-file scope guard skipped: no branch-diff paths were available. Static field hardening checks remain active.");
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
                || lowerPath.Contains("discovery", StringComparison.Ordinal)
                || lowerPath.Contains("mobile", StringComparison.Ordinal)
                || lowerPath.Contains("cloud", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"LAN-M15-A changed a forbidden boundary file: {normalizedPath}");
            }

            throw new InvalidOperationException($"LAN-M15-A changed an out-of-scope file: {normalizedPath}");
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

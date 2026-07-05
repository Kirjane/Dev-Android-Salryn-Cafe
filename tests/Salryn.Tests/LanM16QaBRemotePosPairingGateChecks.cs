namespace Salryn.Tests;

public static class LanM16QaBRemotePosPairingGateChecks
{
    private const string SandboxSha = "e477699dca27a21954a5ad9d388307fdb9ed08fd";
    private const string FreehandStartSha = "80d784d84e6af635688085d8b97bfe935c953d65";

    public static void Run()
    {
        string root = FindProjectRoot(Directory.GetCurrentDirectory());
        string currentState = Read(root, "00-HANDOFF", "CURRENT_STABLE_STATE_AND_NEXT_WORK.md");
        string handoff = Read(root, "00-HANDOFF", "LAN_M16_QA_B_HOST_APPROVED_REMOTE_POS_PAIRING_GATE.md");
        string page = Read(root, "src", "Salryn.App.Web", "src", "pages", "RemotePosClientPage.tsx");
        string hook = Read(root, "src", "Salryn.App.Web", "src", "hooks", "useRemotePosClient.ts");
        string client = Read(root, "src", "Salryn.App.Web", "src", "api", "remotePosClient.ts");
        string gate = Read(root, "src", "Salryn.Api.Local", "RemotePos", "RemotePosPairingGateService.cs");
        string endpoints = Read(root, "src", "Salryn.Api.Local", "RemotePos", "RemotePosApiEndpoints.cs");
        string localControl = Read(root, "src", "Salryn.Api.Local", "LocalLanControlEndpoints.cs");
        string sessionService = Read(root, "src", "Salryn.Api.Local", "RemotePos", "RemotePosOperatorSessionService.cs");
        string infraProgram = Read(root, "tools", "Salryn.Tools.InfrastructureSmoke", "Program.cs");
        string coreSmoke = Read(root, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        Require(currentState.Contains(SandboxSha, StringComparison.Ordinal), "Current state must contain Sandbox SHA.");
        Require(currentState.Contains(FreehandStartSha, StringComparison.Ordinal), "Current state must contain Freehand starting SHA.");
        Require(handoff.Contains("Pairing approves the device", StringComparison.Ordinal), "Handoff must state device pairing boundary.");
        Require(handoff.Contains("Login approves the person", StringComparison.Ordinal), "Handoff must state person login boundary.");

        Require(!page.Contains("label=\"Terminal ID\"", StringComparison.Ordinal), "Remote POS normal UI must not ask for Terminal ID.");
        Require(!page.Contains("label=\"Handshake session ID\"", StringComparison.Ordinal), "Remote POS normal UI must not ask for Handshake Session ID.");
        Require(page.Contains("Cashier / Manager login only", StringComparison.Ordinal), "Remote POS login copy must say Cashier / Manager only.");
        Require(!page.Contains("Admin credentials", StringComparison.OrdinalIgnoreCase), "Remote POS login copy must not say Admin credentials.");
        Require(page.Contains("Admin and Owner accounts are host-local only for this build", StringComparison.Ordinal), "Remote POS page must state Admin/Owner are host-local.");

        Require(client.Contains("remotePosRequestPairing", StringComparison.Ordinal), "Remote pairing request client must exist.");
        Require(client.Contains("joinCode", StringComparison.Ordinal) && client.Contains("deviceName", StringComparison.Ordinal), "Remote pairing request must use join code and device name.");
        Require(hook.Contains("requestJoin", StringComparison.Ordinal), "Hook must expose requestJoin.");
        Require(hook.Contains("activatePairing", StringComparison.Ordinal), "Hook must expose activatePairing.");

        Require(gate.Contains("HashSecret(joinCode)", StringComparison.Ordinal), "Join code must be hashed.");
        Require(gate.Contains("JoinCodeLifetime = TimeSpan.FromMinutes(10)", StringComparison.Ordinal), "Join code must be short-lived.");
        Require(gate.Contains("Pending", StringComparison.Ordinal), "Join code must create pending request state.");
        Require(gate.Contains("PairingCodeLifetime = TimeSpan.FromMinutes(5)", StringComparison.Ordinal), "Pairing code must be short-lived.");
        Require(gate.Contains("PairingCodeUsed", StringComparison.Ordinal), "Pairing code must be one-time use.");
        Require(gate.Contains("SanitizeDeviceName", StringComparison.Ordinal), "Device name must be sanitized.");
        Require(gate.Contains("DeviceNameMaxLength = 40", StringComparison.Ordinal), "Device name must be length-limited.");
        Require(gate.Contains("TerminalPairingActivation", StringComparison.Ordinal), "Gate must use existing pairing activation service.");
        Require(gate.Contains("HandshakeAsync", StringComparison.Ordinal), "Gate must produce handshake path through existing service.");
        Require(!gate.Contains("PairingCode = request.PairingCode", StringComparison.Ordinal), "Pairing code must not be persisted as plaintext state.");

        Require(endpoints.Contains("/api/remote-pos", StringComparison.Ordinal), "Remote POS endpoint group must exist.");
        Require(endpoints.Contains("/pairing/request", StringComparison.Ordinal), "Remote pairing request endpoint must exist.");
        Require(endpoints.Contains("/pairing/activate", StringComparison.Ordinal), "Remote pairing activation endpoint must exist.");
        Require(!endpoints.Contains("/pairing/requests/{requestId}/accept", StringComparison.Ordinal), "Remote clients must not have accept endpoint.");
        Require(!endpoints.Contains("/pairing/requests/{requestId}/reject", StringComparison.Ordinal), "Remote clients must not have reject endpoint.");

        Require(localControl.Contains("/remote-pos/pairing/requests/{requestId}/accept", StringComparison.Ordinal), "Host-local accept endpoint must exist.");
        Require(localControl.Contains("/remote-pos/pairing/requests/{requestId}/reject", StringComparison.Ordinal), "Host-local reject endpoint must exist.");
        Require(localControl.Contains("RequireLocalOperator", StringComparison.Ordinal), "Host-local pairing endpoints must require local operator.");

        Require(sessionService.Contains("AllowedRemoteLoginRoles", StringComparison.Ordinal), "Backend role login allow-list must exist.");
        Require(sessionService.Contains("Cashier", StringComparison.Ordinal) && sessionService.Contains("Manager", StringComparison.Ordinal), "Cashier/Manager must be allowed remotely.");
        Require(sessionService.Contains("RoleNotAllowed", StringComparison.Ordinal), "Backend must refuse disallowed remote login roles.");
        Require(sessionService.Contains("Admin and Owner accounts are host-local only", StringComparison.Ordinal), "Backend role rejection must mention host-local Admin/Owner boundary.");
        Require(sessionService.Contains("SessionLifetime = TimeSpan.FromMinutes(30)", StringComparison.Ordinal), "Remote POS session must remain 30-minute short-lived.");
        Require(sessionService.Contains("OriginMismatch", StringComparison.Ordinal), "Existing origin binding must remain.");

        Require(!ChangedPathExists(root, "Migrations"), "No schema/migration files should be added.");
        Require(!ChangedPathExists(root, "Seed"), "No seed files should be added.");
        Require(!AllCandidateText(root).Contains("0.0.0.0 bind enabled", StringComparison.OrdinalIgnoreCase), "No default/wildcard/0.0.0.0 bind may be added.");
        Require(!AllCandidateText(root).Contains("QR pairing", StringComparison.OrdinalIgnoreCase), "No QR pairing may be added.");
        Require(!AllCandidateText(root).Contains("mobile app", StringComparison.OrdinalIgnoreCase), "No mobile app may be added.");
        Require(!AllCandidateText(root).Contains("cloud sync", StringComparison.OrdinalIgnoreCase), "No cloud sync may be added.");
        Require(!AllCandidateText(root).Contains("custom DNS", StringComparison.OrdinalIgnoreCase), "No custom DNS may be added.");
        Require(!endpoints.Contains("/api/lan-control", StringComparison.Ordinal), "Remote POS surface must not expose /api/lan-control.");

        Require(infraProgram.Contains("remote-pos-pairing-gate", StringComparison.Ordinal), "Infrastructure smoke must wire remote-pos-pairing-gate.");
        Require(coreSmoke.Contains("LanM16QaBRemotePosPairingGateChecks.Run();", StringComparison.Ordinal), "CoreSmoke must wire LAN-M16-QA-B guard.");
    }

    private static bool ChangedPathExists(string root, string token)
    {
        string? output = TryGit(root, "diff --name-only " + FreehandStartSha + "...HEAD");
        return output?.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(path => path.Contains(token, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static string AllCandidateText(string root)
    {
        string? paths = TryGit(root, "diff --name-only " + FreehandStartSha + "...HEAD");
        if (string.IsNullOrWhiteSpace(paths)) return string.Empty;
        List<string> contents = new();
        foreach (string relative in paths.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path)) contents.Add(File.ReadAllText(path));
        }
        return string.Join("\n", contents);
    }

    private static string? TryGit(string root, string args)
    {
        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = args,
                WorkingDirectory = root,
                RedirectStandardOutput = true,
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

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(parts));

    private static string FindProjectRoot(string start)
    {
        DirectoryInfo? directory = new(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Salryn.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not find Salryn.sln.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

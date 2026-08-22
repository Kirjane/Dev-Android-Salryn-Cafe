internal static class RemotePosPairingGateSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== REMOTE POS PAIRING GATE SMOKE =====");

        string page = Read(projectRoot, "src", "Salryn.App.Web", "src", "pages", "RemotePosClientPage.tsx");
        string hook = Read(projectRoot, "src", "Salryn.App.Web", "src", "hooks", "useRemotePosClient.ts");
        string client = Read(projectRoot, "src", "Salryn.App.Web", "src", "api", "remotePosClient.ts");
        string gate = Read(projectRoot, "src", "Salryn.Api.Local", "RemotePos", "RemotePosPairingGateService.cs");
        string endpoints = Read(projectRoot, "src", "Salryn.Api.Local", "RemotePos", "RemotePosApiEndpoints.cs");
        string localControl = Read(projectRoot, "src", "Salryn.Api.Local", "LocalLanControlEndpoints.cs");
        string session = Read(projectRoot, "src", "Salryn.Api.Local", "RemotePos", "RemotePosOperatorSessionService.cs");

        Require(!page.Contains("label=\"Terminal ID\"", StringComparison.Ordinal), "Normal field UX must not expose Terminal ID input.");
        Require(!page.Contains("label=\"Handshake session ID\"", StringComparison.Ordinal), "Normal field UX must not expose Handshake Session ID input.");
        Require(page.Contains("Cashier / Manager login", StringComparison.Ordinal), "Remote POS login copy must limit login to Cashier/Manager.");
        Require(!page.Contains("Admin credentials", StringComparison.OrdinalIgnoreCase), "Remote POS login copy must not say Admin credentials.");

        Require(client.Contains("joinCode", StringComparison.Ordinal) && client.Contains("deviceName", StringComparison.Ordinal), "Remote pairing request must require join code and device name.");
        Require(gate.Contains("JoinCodeExpired", StringComparison.Ordinal), "Expired join code must be rejected.");
        Require(gate.Contains("JoinCodeRejected", StringComparison.Ordinal), "Wrong join code must be rejected.");
        Require(gate.Contains("Pending", StringComparison.Ordinal), "Join code must create only pending request state.");
        Require(!endpoints.Contains("/pairing/requests/{requestId}/accept", StringComparison.Ordinal), "Remote client must not approve requests.");
        Require(localControl.Contains("/remote-pos/pairing/requests/{requestId}/accept", StringComparison.Ordinal), "Host-local operator must be able to accept.");
        Require(localControl.Contains("/remote-pos/pairing/requests/{requestId}/reject", StringComparison.Ordinal), "Host-local operator must be able to reject.");
        Require(gate.Contains("CreatePairingInviteAsync", StringComparison.Ordinal), "Pairing code must be generated only after host approval.");
        Require(gate.Contains("PairingCodeUsed", StringComparison.Ordinal), "Pairing code must be one-time use.");
        Require(gate.Contains("PairingCodeExpired", StringComparison.Ordinal), "Expired pairing code must be rejected.");
        Require(gate.Contains("SanitizeDeviceName", StringComparison.Ordinal) && gate.Contains("DeviceNameMaxLength = 40", StringComparison.Ordinal), "Device name must be sanitized and length-limited.");
        Require(!gate.Contains("PairingCode = request.PairingCode", StringComparison.Ordinal), "Pairing code must not be persisted/logged in plaintext.");
        Require(gate.Contains("ActivatePairingAsync", StringComparison.Ordinal) && gate.Contains("HandshakeAsync", StringComparison.Ordinal), "Successful pairing must use existing activation and handshake services.");
        Require(session.Contains("TerminalHandshakeRequired", StringComparison.Ordinal), "Remote POS login must refuse unpaired devices.");
        Require(session.Contains("RoleNotAllowed", StringComparison.Ordinal), "Remote POS login must refuse disallowed roles.");
        Require(session.Contains("Cashier", StringComparison.Ordinal) && session.Contains("Manager", StringComparison.Ordinal), "Remote POS login must allow Cashier/Manager role names.");
        Require(session.Contains("OriginMismatch", StringComparison.Ordinal), "Existing origin binding must still work.");
        Require(Read(projectRoot, "src", "Salryn.Api.Local", "RemotePos", "RemotePosCheckoutService.cs").Contains("best-effort", StringComparison.OrdinalIgnoreCase), "Existing post-commit audit safety must remain documented in checkout service.");
        Require(session.Contains("TerminalNotAccepted", StringComparison.Ordinal), "Terminal disable/revoke must still block remote POS.");
        // Match production schema/seed trees only; do not fail on tool names containing "Seed".
        Require(!ChangedPathExists(projectRoot, "Migrations/"), "No migration files may be added.");
        Require(!ChangedPathExists(projectRoot, "Data/Seeding"), "No production seed data files may be added.");
        Require(!AllCandidateText(projectRoot, "tests/Salryn.Tests/LanM16QaBRemotePosPairingGateChecks.cs", "tools/Salryn.Tools.InfrastructureSmoke/RemotePosPairingGateSmokeChecks.cs").Contains("QR pairing", StringComparison.OrdinalIgnoreCase), "No QR behavior may be added.");
        Require(!AllCandidateText(projectRoot, "tests/Salryn.Tests/LanM16QaBRemotePosPairingGateChecks.cs", "tools/Salryn.Tools.InfrastructureSmoke/RemotePosPairingGateSmokeChecks.cs").Contains("cloud sync", StringComparison.OrdinalIgnoreCase), "No cloud behavior may be added.");
        Require(!AllCandidateText(projectRoot, "tests/Salryn.Tests/LanM16QaBRemotePosPairingGateChecks.cs", "tools/Salryn.Tools.InfrastructureSmoke/RemotePosPairingGateSmokeChecks.cs").Contains("custom DNS", StringComparison.OrdinalIgnoreCase), "No custom DNS behavior may be added.");
        Require(!AllCandidateText(projectRoot, "tests/Salryn.Tests/LanM16QaBRemotePosPairingGateChecks.cs", "tools/Salryn.Tools.InfrastructureSmoke/RemotePosPairingGateSmokeChecks.cs").Contains("0.0.0.0 bind enabled", StringComparison.OrdinalIgnoreCase), "No default or wildcard bind may be added.");
        Require(!endpoints.Contains("/api/lan-control", StringComparison.Ordinal), "Remote surface must not expose /api/lan-control.");

        Console.WriteLine("Remote POS pairing gate smoke passed.");
        Console.WriteLine();
    }

    private static bool ChangedPathExists(string root, string token)
    {
        string? output = TryGit(root, "diff --name-only 80d784d84e6af635688085d8b97bfe935c953d65...HEAD");
        return output?.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(path => path.Contains(token, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static string AllCandidateText(string root, params string[] excludedRelativePaths)
    {
        string? paths = TryGit(root, "diff --name-only 80d784d84e6af635688085d8b97bfe935c953d65...HEAD");
        if (string.IsNullOrWhiteSpace(paths)) return string.Empty;
        List<string> contents = new();
        foreach (string relative in paths.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (excludedRelativePaths.Any(excluded => relative.Equals(excluded, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (!relative.StartsWith("src/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

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
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);
            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(parts));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

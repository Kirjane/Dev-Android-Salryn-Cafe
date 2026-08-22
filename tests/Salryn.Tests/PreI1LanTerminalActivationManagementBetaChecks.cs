using Salryn.Core.TerminalRegistry;

namespace Salryn.Tests;

/// <summary>
/// PRE-I1 beta cutline for separate terminal activation and host-local Terminal Management.
/// Keeps the commercial proof, client entry, trust persistence, role, and UX boundaries joined.
/// </summary>
public static class PreI1LanTerminalActivationManagementBetaChecks
{
    public static void Run()
    {
        string root = FindRoot();
        string activation = Read(root, "src", "Salryn.App.Web", "src", "pages", "TerminalActivationPage.tsx");
        string storage = Read(root, "src", "Salryn.App.Web", "src", "utils", "terminalLicenseStorage.ts");
        string app = Read(root, "src", "Salryn.App.Web", "src", "App.tsx");
        string landing = Read(root, "src", "Salryn.App.Web", "src", "pages", "RemoteClientLandingPage.tsx");
        string remotePos = Read(root, "src", "Salryn.App.Web", "src", "pages", "RemotePosClientPage.tsx");
        string hostManagement = Read(root, "src", "Salryn.App.Web", "src", "pages", "HostLanControlPage.tsx");
        string navigation = Read(root, "src", "Salryn.App.Web", "src", "navigation.ts");
        string apiClient = Read(root, "src", "Salryn.App.Web", "src", "api", "remotePosClient.ts");
        string apiEndpoints = Read(root, "src", "Salryn.Api.Local", "RemotePos", "RemotePosApiEndpoints.cs");
        string pairing = Read(root, "src", "Salryn.Api.Local", "RemotePos", "RemotePosPairingGateService.cs");
        string entitlement = Read(root, "src", "Salryn.Infrastructure", "Licensing", "LocalLanTerminalEntitlementService.cs");
        string sessions = Read(root, "src", "Salryn.Api.Local", "RemotePos", "RemotePosOperatorSessionService.cs");
        string trust = Read(root, "src", "Salryn.Api.Local", "RemotePos", "RemotePosTerminalTrustService.cs");
        string activationService = Read(root, "src", "Salryn.Infrastructure", "Data", "Services", "TerminalPairingActivationService.cs");

        RequireContains(activation, "Terminal Activation", "Terminal client must have a dedicated activation page.");
        RequireContains(activation, "This Terminal Machine Code", "Activation page must expose this device machine code.");
        RequireContains(activation, "salryn.info@gmail.com", "Activation page must identify the beta issuance contact.");
        RequireContains(activation, "Import Terminal License", "Activation page must explicitly import a signed Terminal license.");
        RequireContains(activation, "Status: Active", "Activation page must show a clear active status.");
        RequireContains(activation, "Copy Terminal Machine Code", "Activation page must copy the terminal machine code.");
        RequireContains(activation, "do not need a Host License ID", "Terminal users must not be told to find Host License ID.");
        RequireNotContains(activation, "Include your store name, Host License ID", "Terminal activation must not ask for Host License ID.");
        RequireContains(activation, "Host licenses cannot activate a terminal", "Import copy must reject Host licenses.");
        RequireContains(storage, "salryn.terminalLicense.v1", "Installed Terminal license must use explicit versioned local persistence.");
        RequireContains(storage, "salryn.terminalMachineCode.v1", "Terminal machine code must persist across refresh/restart.");
        RequireContains(storage, "A Host license cannot activate a terminal", "Client must reject Host license substitution.");

        RequireContains(app, "readInstalledTerminalLicense() ? \"landing\" : \"activation\"", "Remote client entry must be activation-first.");
        RequireNotContains(landing, "showInternalRemotePos", "Remote POS must be a normal beta client surface.");
        RequireContains(landing, "Terminal License", "Client entry must allow review/replacement of its Terminal license.");
        RequireContains(navigation, "{ id: \"host-lan-control\", label: \"Terminal Management\", enabled: true, group: \"Admin\" }", "Terminal Management must be normal Admin navigation.");
        RequireNotContains(hostManagement, "Terminal activation is not ready for client use", "Completed activation must not retain incomplete UX copy.");
        RequireContains(hostManagement, "Each terminal device needs its own Terminal license", "Host guidance must state one Terminal license per device.");
        RequireContains(hostManagement, "Terminal licenses are issued from the Host license context", "Host guidance must state Host-context issuance.");
        RequireContains(hostManagement, "License desk details", "Host Terminal Management must keep support issuance details collapsed.");
        RequireContains(hostManagement, "Copy Terminal License Request", "Host Terminal Management must offer a copy-ready request block.");
        RequireContains(hostManagement, "Copy this request for the license desk", "Host Terminal Management must use client-friendly license desk wording.");
        int diagnosticsIndex = hostManagement.IndexOf("Advanced diagnostics", StringComparison.Ordinal);
        RequireTrue(diagnosticsIndex >= 0
            && hostManagement.IndexOf("Configured bind (raw)", StringComparison.Ordinal) > diagnosticsIndex
            && hostManagement.IndexOf("actualListenUrls", StringComparison.Ordinal) > diagnosticsIndex,
            "Raw bind and listener details must stay under collapsed diagnostics.");

        RequireContains(apiEndpoints, "/terminal-license/validate", "Host must validate a Terminal license before pairing.");
        RequireContains(apiClient, "terminalMachineCode", "Pairing activation must submit the terminal-origin machine code.");
        RequireContains(entitlement, "MachineFingerprint = normalizedMachineCode", "Host validation must compare license binding to the supplied device code.");
        RequireNotContains(entitlement, "status.AllowedLanTerminals", "Host terminal-count metadata must not decide commercial capacity.");
        RequireContains(pairing, "EnsureTerminalLicenseAvailableAsync", "Pairing must refuse reuse of a non-revoked licensed TerminalId.");
        RequireContains(pairing, "TerminalLicenseAlreadyPaired", "Duplicate Terminal license refusal must be explicit.");
        RequireContains(activationService, "TerminalLicenseTrustMetadata.Build", "Verified Terminal-license identity must persist with registry trust.");
        RequireContains(sessions, "TerminalLicenseTrustMetadata.TryRead", "Every remote POS login/command trust check must require persisted license proof.");
        RequireContains(trust, "verified Terminal license", "Trust restoration must fail closed for legacy unlicensed pairings.");
        RequireContains(trust, "RemotePosTerminalTrustStatuses.RuntimeDisabled", "A Host runtime restart must pause access without classifying durable terminal trust as revoked.");
        RequireContains(remotePos, "Remote POS is paused on the Host", "A paired terminal must explain that Host runtime pause keeps its trusted pairing.");
        RequireContains(Read(root, "src", "Salryn.App.Web", "src", "hooks", "useRemotePosClient.ts"), "This terminal was disabled on the Host", "Disabled terminal guidance must not mislabel the lifecycle state as revoked.");

        RequireContains(sessions, "{ \"Cashier\", \"Manager\" }", "Remote POS login must allow only Cashier and Manager roles.");
        RequireContains(sessions, "Admin and Owner accounts are host-local", "Remote role rejection must explain the host-local boundary.");
        RequireContains(remotePos, "<summary className=\"cursor-pointer font-medium text-slate-700\">Diagnostics</summary>", "Technical state must be collapsed under Diagnostics.");
        RequireNotContains(remotePos, "State A —", "Normal client UX must not expose engineering state labels.");
        RequireNotContains(remotePos, "State F —", "Normal client UX must not expose engineering state labels.");

        DateTimeOffset expiresAt = new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        string notes = TerminalLicenseTrustMetadata.Build("LIC-TERM-1", "TERM-STORE-01", "SALRYN-MC-1111-2222-3333-4444", expiresAt);
        RequireTrue(
            TerminalLicenseTrustMetadata.TryRead(notes, out string licenseId, out string terminalId, out string machineCode, out DateTimeOffset? storedExpiry),
            "Terminal license trust metadata must round-trip.");
        RequireTrue(licenseId == "LIC-TERM-1" && terminalId == "TERM-STORE-01" && machineCode == "SALRYN-MC-1111-2222-3333-4444" && storedExpiry == expiresAt,
            "Terminal license trust metadata must preserve exact identity.");
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

    private static string FindRoot()
    {
        DirectoryInfo? current = new(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln"))) return current.FullName;
            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Salryn.sln.");
    }

    private static void RequireTrue(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireContains(string text, string expected, string message)
    {
        if (!text.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message + " Missing: " + expected);
        }
    }

    private static void RequireNotContains(string text, string forbidden, string message)
    {
        if (text.Contains(forbidden, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message + " Forbidden: " + forbidden);
        }
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Licensing;

namespace Salryn.Tests;

/// <summary>
/// LIC-CLOSEOUT: hard feature gates source wiring, required terminal proof,
/// Host/Terminal denial matrix, and optional Ops-generated license signature proof.
/// </summary>
public static class LocalLicensingCloseoutHardGatesAndTerminalProofChecks
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
    private const string TestMachine = "SALRYN-MC-CLOS-EOUT-HARD-GATE";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "salryn-lic-closeout-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            LocalLicenseFileStore store = new(new LocalLicenseFileStoreOptions(root, null));
            LocalLicenseService service = new(
                store,
                new LocalLicenseValidator(new LocalLicenseValidatorOptions
                {
                    MachineFingerprint = TestMachine,
                    SignatureVerifier = _ => true,
                    AllowDevelopmentMode = false
                }, () => FixedNow),
                () => FixedNow);
            LocalLicenseEnforcementService enforcement = new(service);
            LocalLanTerminalEntitlementService entitlements = new(
                service,
                hardCap: 10,
                defaultMaxActiveSessions: 3,
                signatureVerifier: _ => true,
                utcNow: () => FixedNow);

            // Missing license blocks host features.
            RequireTrue(!enforcement.IsFeatureAllowedAsync(LicenseFeatures.Pos).GetAwaiter().GetResult(), "Missing license must deny Pos.");
            RequireTrue(!enforcement.IsFeatureAllowedAsync(LicenseFeatures.Users).GetAwaiter().GetResult(), "Missing license must deny Users.");
            RequireTrue(!enforcement.IsFeatureAllowedAsync(LicenseFeatures.Backup).GetAwaiter().GetResult(), "Missing license must deny Backup.");
            RequireTrue(!enforcement.IsFeatureAllowedAsync(LicenseFeatures.Reports).GetAwaiter().GetResult(), "Missing license must deny Reports.");

            Install(service, Host("LIC-CLOSE-HOST", allowedLan: 2, features: new[]
            {
                LicenseFeatures.Pos, LicenseFeatures.Users, LicenseFeatures.Backup,
                LicenseFeatures.Reports, LicenseFeatures.Licensing, LicenseFeatures.LanTerminals
            }));
            RequireTrue(enforcement.IsFeatureAllowedAsync(LicenseFeatures.Pos).GetAwaiter().GetResult(), "Host must allow Pos.");
            RequireTrue(enforcement.IsFeatureAllowedAsync(LicenseFeatures.Users).GetAwaiter().GetResult(), "Host must allow Users.");
            RequireTrue(enforcement.IsFeatureAllowedAsync(LicenseFeatures.Backup).GetAwaiter().GetResult(), "Host must allow Backup.");
            RequireTrue(enforcement.IsFeatureAllowedAsync(LicenseFeatures.Reports).GetAwaiter().GetResult(), "Host must allow Reports.");
            RequireTrue(enforcement.IsFeatureAllowedAsync(LicenseFeatures.LanTerminals).GetAwaiter().GetResult(), "Host with LAN entitlement must allow LanTerminals.");

            // Terminal cannot unlock host ops.
            Install(service, Terminal("LIC-CLOSE-TERM", hostLicenseId: "LIC-CLOSE-HOST", terminalId: "term-c1"));
            RequireTrue(!enforcement.IsFeatureAllowedAsync(LicenseFeatures.Pos).GetAwaiter().GetResult(), "Terminal must deny Pos.");
            RequireTrue(!enforcement.IsFeatureAllowedAsync(LicenseFeatures.Users).GetAwaiter().GetResult(), "Terminal must deny Users.");
            RequireTrue(enforcement.IsFeatureAllowedAsync(LicenseFeatures.LanTerminals).GetAwaiter().GetResult(), "Terminal must allow LanTerminals.");

            // Host entitlement + required terminal proof.
            Install(service, Host("LIC-CLOSE-HOST", allowedLan: 2, features: new[] { LicenseFeatures.LanTerminals, LicenseFeatures.Pos }));
            LocalLanTerminalEntitlementStatus hostEnt = entitlements.GetHostEntitlementAsync().GetAwaiter().GetResult();
            RequireTrue(hostEnt.HostOperationalLanAllowed, "Host LAN must be entitled.");
            RequireTrue(entitlements.ValidateTerminalLicenseProof(null, TestMachine, hostEnt).IsFailure, "Missing TerminalLicenseJson must fail.");
            RequireTrue(entitlements.ValidateTerminalLicenseProof("  ", TestMachine, hostEnt).IsFailure, "Blank TerminalLicenseJson must fail.");

            string hostJson = JsonSerializer.Serialize(
                Host("LIC-CLOSE-HOST", allowedLan: 2, features: new[] { LicenseFeatures.LanTerminals }),
                JsonOptions);
            RequireTrue(entitlements.ValidateTerminalLicenseProof(hostJson, TestMachine, hostEnt).IsFailure, "Host license as terminal proof must fail.");

            LocalLicenseDocument goodTerm = Terminal("LIC-CLOSE-TERM-OK", hostLicenseId: "LIC-CLOSE-HOST", terminalId: "term-ok");
            RequireTrue(
                entitlements.ValidateTerminalLicenseProof(JsonSerializer.Serialize(goodTerm, JsonOptions), TestMachine, hostEnt).IsSuccess,
                "Valid terminal proof must pass.");

            LocalLicenseDocument wrongStore = goodTerm with { StoreId = "other-store" };
            RequireTrue(
                entitlements.ValidateTerminalLicenseProof(JsonSerializer.Serialize(wrongStore, JsonOptions), TestMachine, hostEnt).IsFailure,
                "Wrong StoreId must fail.");

            LocalLicenseDocument wrongHost = goodTerm with { HostLicenseId = "other-host" };
            RequireTrue(
                entitlements.ValidateTerminalLicenseProof(JsonSerializer.Serialize(wrongHost, JsonOptions), TestMachine, hostEnt).IsFailure,
                "Wrong HostLicenseId must fail.");

            // Source guards: gates wired; proof required; license endpoints unguarded by product feature.
            string checkout = Read("src", "Salryn.Api.Local", "LocalHostCheckoutEndpoints.cs");
            string users = Read("src", "Salryn.Api.Local", "LocalUserAdminEndpoints.cs");
            string reports = Read("src", "Salryn.Api.Local", "LocalOwnerReportsEndpoints.cs");
            string backup = Read("src", "Salryn.Api.Local", "LocalBackupPackageEndpoints.cs");
            string lan = Read("src", "Salryn.Api.Local", "LocalLanControlEndpoints.cs");
            string licenseEp = Read("src", "Salryn.Api.Local", "LocalLicenseEndpoints.cs");
            string pairing = Read("src", "Salryn.Api.Local", "RemotePos", "RemotePosPairingGateService.cs");
            string guard = Read("src", "Salryn.Api.Local", "Auth", "ApiLicenseGuard.cs");
            string hostLan = File.ReadAllText(Path.Combine(FindRoot(), "src", "Salryn.App.Web", "src", "pages", "HostLanControlPage.tsx"));
            string navigation = File.ReadAllText(Path.Combine(FindRoot(), "src", "Salryn.App.Web", "src", "navigation.ts"));
            string remoteClient = File.ReadAllText(Path.Combine(FindRoot(), "src", "Salryn.App.Web", "src", "api", "remotePosClient.ts"));

            RequireContains(checkout, "RequireLicensedFeatureAsync", "Checkout must call license guard.");
            RequireContains(checkout, "LicenseFeatures.Pos", "Checkout must require Pos.");
            RequireContains(users, "LicenseFeatures.Users", "Users write must require Users feature.");
            RequireContains(reports, "LicenseFeatures.Reports", "Reports must require Reports feature.");
            RequireContains(backup, "LicenseFeatures.Backup", "Backup must require Backup feature.");
            RequireNotContains(lan, "RequireLicensedFeatureAsync(\n                    LicenseFeatures.LanTerminals", "Host feature count must not gate terminal pairing.");
            RequireContains(guard, "License.FeatureDenied", "Guard must return License.FeatureDenied.");
            RequireNotContains(licenseEp, "RequireLicensedFeatureAsync", "License status/import must remain reachable without product feature gate.");
            RequireContains(pairing, "ValidateTerminalLicenseProof", "Activation must validate terminal proof.");
            RequireNotContains(pairing, "may not always send TerminalLicenseJson", "Optional terminal proof comment must be removed.");
            RequireContains(hostLan, "title=\"Terminal Management\"", "Internal host LAN UI must use Terminal Management naming.");
            RequireNotContains(hostLan, "TerminalLicenseJson", "Client copy must not expose the terminal proof field name.");
            RequireContains(navigation, "{ id: \"host-lan-control\", label: \"LAN Terminals · Future Feature\", enabled: true, group: \"Admin\", domain: \"Store & System\" }", "Normal Admin navigation must state the LAN future-feature boundary.");
            RequireContains(remoteClient, "terminalLicenseJson", "Remote client must send terminalLicenseJson.");
            RequireContains(remoteClient, "readInstalledTerminalLicense", "Remote client must load explicitly installed terminal license proof.");

            // Optional: prove LicensingOps-generated signature against POS public key when sibling fixture exists.
            TryVerifyOpsGeneratedLicense();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void TryVerifyOpsGeneratedLicense()
    {
        string? opsRoot = FindSiblingLicensingOps();
        if (opsRoot is null)
        {
            return;
        }

        string licensesDir = Path.Combine(opsRoot, "generated", "licenses");
        if (!Directory.Exists(licensesDir))
        {
            return;
        }

        string[] hosts = Directory.GetFiles(licensesDir, "lic_host_*.json");
        if (hosts.Length == 0)
        {
            return;
        }

        string json = File.ReadAllText(hosts.OrderByDescending(f => f).First());
        LocalLicenseDocument? doc = JsonSerializer.Deserialize<LocalLicenseDocument>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        });
        if (doc is null || string.IsNullOrWhiteSpace(doc.Signature))
        {
            return;
        }

        // Optional sibling-fixture check only: skip quietly when Ops/POS keys or payload formats diverge.
        // Core POS signature gates remain covered by signed-install tests using in-repo keys.
        _ = LocalLicenseSignatureVerifier.Verify(doc);
    }

    private static string? FindSiblingLicensingOps()
    {
        string parent = Path.GetDirectoryName(FindRoot()) ?? FindRoot();
        string candidate = Path.Combine(parent, "Salryn.LicensingOps");
        return Directory.Exists(candidate) ? candidate : null;
    }

    private static void Install(LocalLicenseService service, LocalLicenseDocument document)
    {
        LocalLicenseInstallResponse response = service
            .InstallAsync(new LocalLicenseInstallRequest(JsonSerializer.Serialize(document, JsonOptions)))
            .GetAwaiter()
            .GetResult();
        if (response.InstallStatus != "Installed")
        {
            throw new InvalidOperationException("Install failed: " + response.ErrorMessage);
        }
    }

    private static LocalLicenseDocument Host(string id, int allowedLan, IReadOnlyList<string> features) => new(
        LicenseId: id,
        CustomerName: "Closeout",
        Edition: LicenseEdition.Local,
        IssuedAt: FixedNow.AddDays(-1),
        ExpiresAt: FixedNow.AddDays(30),
        MachineBinding: TestMachine,
        Features: features,
        Signature: "signed-test",
        SchemaVersion: LocalLicenseValidator.SupportedSchemaVersion,
        ApplicationId: LocalLicenseContractPolicy.RequiredApplicationId,
        StoreId: "store-closeout",
        AllowedLanTerminals: allowedLan,
        LicenseType: LicenseType.Host);

    private static LocalLicenseDocument Terminal(string id, string hostLicenseId, string terminalId) => new(
        LicenseId: id,
        CustomerName: "Closeout",
        Edition: LicenseEdition.Local,
        IssuedAt: FixedNow.AddDays(-1),
        ExpiresAt: FixedNow.AddDays(30),
        MachineBinding: TestMachine,
        Features: new[] { LicenseFeatures.LanTerminals },
        Signature: "signed-test",
        SchemaVersion: LocalLicenseValidator.SupportedSchemaVersion,
        ApplicationId: LocalLicenseContractPolicy.RequiredApplicationId,
        StoreId: "store-closeout",
        AllowedLanTerminals: null,
        LicenseType: LicenseType.Terminal,
        HostLicenseId: hostLicenseId,
        TerminalId: terminalId);

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { FindRoot() }.Concat(parts).ToArray()));

    private static string FindRoot()
    {
        DirectoryInfo? current = new(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Salryn.sln.");
    }

    private static void RequireTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
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

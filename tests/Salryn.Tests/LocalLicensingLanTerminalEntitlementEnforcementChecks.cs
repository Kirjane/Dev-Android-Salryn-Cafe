using System.Text.Json;
using System.Text.Json.Serialization;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Licensing;

namespace Salryn.Tests;

/// <summary>
/// LIC-LAN: valid Host unlocks management while one signed Terminal license per
/// terminal is the commercial truth; wrong device/store/host proof is rejected.
/// </summary>
public static class LocalLicensingLanTerminalEntitlementEnforcementChecks
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
    private const string TestMachine = "SALRYN-MC-LAN0-ENTI-TLE1-TEST";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "salryn-lic-lan-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            LocalLicenseFileStore store = new(new LocalLicenseFileStoreOptions(root, null));
            LocalLicenseService service = new(
                store,
                new LocalLicenseValidator(new LocalLicenseValidatorOptions
                {
                    MachineFingerprint = TestMachine,
                    SignatureVerifier = _ => true
                }, () => FixedNow),
                () => FixedNow);
            LocalLanTerminalEntitlementService entitlements = new(
                service,
                hardCap: 10,
                defaultMaxActiveSessions: 3,
                signatureVerifier: _ => true,
                utcNow: () => FixedNow);

            // Missing license blocks operational LAN.
            LocalLanTerminalEntitlementStatus missing = entitlements.GetHostEntitlementAsync().GetAwaiter().GetResult();
            RequireTrue(!missing.HostOperationalLanAllowed, "Missing license must not allow Host LAN operations.");
            RequireTrue(missing.EffectiveMaxPairedTerminals == 0, "Missing license effective paired max must be 0.");
            RequireContains(missing.Message, "salryn.info@gmail.com", "Missing entitlement message must include contact email.");

            // Host count is not commercial truth: even a hardened zero-count Host allows management.
            Install(service, Host("LIC-LAN-ZERO", allowedLan: 0, features: new[] { LicenseFeatures.Pos, LicenseFeatures.LanTerminals }));
            LocalLanTerminalEntitlementStatus zero = entitlements.GetHostEntitlementAsync().GetAwaiter().GetResult();
            RequireTrue(zero.HostOperationalLanAllowed, "A valid Host must allow Terminal Management regardless of terminal-count metadata.");
            RequireTrue(zero.EffectiveMaxPairedTerminals == 10, "Valid Host must expose the safety hard cap.");

            // A legacy Host count does not change the safety ceiling.
            Install(service, Host("LIC-LAN-HOST-2", allowedLan: 2, features: new[] { LicenseFeatures.Pos, LicenseFeatures.LanTerminals, LicenseFeatures.Licensing }));
            LocalLanTerminalEntitlementStatus allowed = entitlements.GetHostEntitlementAsync().GetAwaiter().GetResult();
            RequireTrue(allowed.HostOperationalLanAllowed, "Host with AllowedLanTerminals=2 must allow operations.");
            RequireTrue(allowed.AllowedLanTerminals == 0, "Host terminal count must not surface as commercial capacity.");
            RequireTrue(allowed.EffectiveMaxPairedTerminals == 10, "Effective paired maximum must remain the safety cap.");
            RequireTrue(allowed.HostLicenseId == "LIC-LAN-HOST-2", "Host LicenseId must surface.");
            RequireTrue(allowed.StoreId == "store-lan-1", "StoreId must surface.");

            // Hard cap clamps high entitlement.
            Install(service, Host("LIC-LAN-HOST-99", allowedLan: 99, features: new[] { LicenseFeatures.LanTerminals }));
            LocalLanTerminalEntitlementStatus capped = entitlements.GetHostEntitlementAsync().GetAwaiter().GetResult();
            RequireTrue(capped.EffectiveMaxPairedTerminals == 10, "Effective max must clamp to hard cap 10.");

            // Terminal license as host operational entitlement is rejected.
            Install(service, Terminal("LIC-LAN-TERM-AS-HOST", hostLicenseId: "LIC-LAN-HOST-2", terminalId: "term-x"));
            LocalLanTerminalEntitlementStatus termAsHost = entitlements.GetHostEntitlementAsync().GetAwaiter().GetResult();
            RequireTrue(!termAsHost.HostOperationalLanAllowed, "Terminal license must not unlock Host LAN controls.");
            RequireContains(termAsHost.Message, "Terminal license cannot unlock Host", "Terminal-as-host message must be explicit.");

            // Reinstall host for proof checks.
            Install(service, Host("LIC-LAN-HOST-2", allowedLan: 2, features: new[] { LicenseFeatures.LanTerminals }));
            LocalLanTerminalEntitlementStatus hostEnt = entitlements.GetHostEntitlementAsync().GetAwaiter().GetResult();

            // Host license as terminal proof rejected.
            string hostJson = JsonSerializer.Serialize(
                Host("LIC-LAN-HOST-2", allowedLan: 2, features: new[] { LicenseFeatures.LanTerminals }),
                JsonOptions);
            var hostAsTerm = entitlements.ValidateTerminalLicenseProof(hostJson, TestMachine, hostEnt);
            RequireTrue(hostAsTerm.IsFailure, "Host license must not be accepted as terminal proof.");
            RequireTrue(
                hostAsTerm.ErrorDetail.Code.Contains("HostLicenseAsTerminal", StringComparison.Ordinal)
                    || hostAsTerm.ErrorDetail.Message.Contains("Host license cannot be used as terminal", StringComparison.OrdinalIgnoreCase),
                "Host-as-terminal must use HostLicenseAsTerminal semantics.");

            // Wrong StoreId rejected.
            LocalLicenseDocument wrongStore = Terminal("LIC-LAN-TERM-STORE", hostLicenseId: "LIC-LAN-HOST-2", terminalId: "term-1") with
            {
                StoreId = "store-other"
            };
            var wrongStoreResult = entitlements.ValidateTerminalLicenseProof(
                JsonSerializer.Serialize(wrongStore, JsonOptions),
                TestMachine,
                hostEnt);
            RequireTrue(wrongStoreResult.IsFailure, "Wrong StoreId must be rejected.");
            RequireContains(wrongStoreResult.ErrorDetail.Message, "StoreId", "Wrong store message must mention StoreId.");

            // Wrong HostLicenseId rejected.
            LocalLicenseDocument wrongHost = Terminal("LIC-LAN-TERM-HOST", hostLicenseId: "LIC-OTHER", terminalId: "term-2");
            var wrongHostResult = entitlements.ValidateTerminalLicenseProof(
                JsonSerializer.Serialize(wrongHost, JsonOptions),
                TestMachine,
                hostEnt);
            RequireTrue(wrongHostResult.IsFailure, "Wrong HostLicenseId must be rejected.");
            RequireContains(wrongHostResult.ErrorDetail.Message, "HostLicenseId", "Wrong host message must mention HostLicenseId.");

            // Valid terminal proof accepted.
            LocalLicenseDocument goodTerm = Terminal("LIC-LAN-TERM-OK", hostLicenseId: "LIC-LAN-HOST-2", terminalId: "term-ok");
            var good = entitlements.ValidateTerminalLicenseProof(
                JsonSerializer.Serialize(goodTerm, JsonOptions),
                TestMachine,
                hostEnt);
            RequireTrue(good.IsSuccess, "Valid terminal proof must be accepted. " + (good.IsFailure ? good.ErrorDetail.Message : ""));

            // Empty proof rejected.
            var emptyProof = entitlements.ValidateTerminalLicenseProof("  ", TestMachine, hostEnt);
            RequireTrue(emptyProof.IsFailure, "Empty terminal proof must be rejected.");

            var wrongMachine = entitlements.ValidateTerminalLicenseProof(
                JsonSerializer.Serialize(goodTerm, JsonOptions),
                "SALRYN-MC-WRONG-DEVICE-CODE",
                hostEnt);
            RequireTrue(wrongMachine.IsFailure, "Terminal proof bound to another machine must be rejected.");

            // Source / UI guards.
            string entitlementService = ReadRepo("src", "Salryn.Infrastructure", "Licensing", "LocalLanTerminalEntitlementService.cs");
            string pairingGate = ReadRepo("src", "Salryn.Api.Local", "RemotePos", "RemotePosPairingGateService.cs");
            string hostLan = File.ReadAllText(Path.Combine(FindRoot(), "src", "Salryn.App.Web", "src", "pages", "HostLanControlPage.tsx"));
            string navigation = File.ReadAllText(Path.Combine(FindRoot(), "src", "Salryn.App.Web", "src", "navigation.ts"));
            string contracts = ReadRepo("src", "Salryn.Core", "Services", "Licensing", "LocalLanTerminalEntitlementContracts.cs");

            RequireContains(entitlementService, "GetHostEntitlementAsync", "Entitlement service must evaluate host LAN entitlement.");
            RequireContains(entitlementService, "ValidateTerminalLicenseProof", "Entitlement service must validate terminal proof.");
            RequireContains(entitlementService, "HostLicenseAsTerminal", "Must reject host license as terminal proof.");
            RequireContains(pairingGate, "LocalLanTerminalEntitlementService", "Pairing gate must use entitlement service.");
            RequireContains(pairingGate, "GetEffectiveMaxPairedTerminals", "Pairing gate must retain a safety maximum.");
            RequireContains(pairingGate, "PairedTerminalLimitReached", "Pairing gate must report terminal capacity overflow.");
            RequireContains(pairingGate, "ValidateTerminalLicenseProof", "Activation must require terminal license proof validation.");
            RequireContains(hostLan, "title=\"Terminal Management\"", "Internal host LAN page must use Terminal Management naming.");
            RequireNotContains(hostLan, "Terminal activation is not ready for client use", "Completed terminal activation must not retain incomplete warnings.");
            RequireContains(navigation, "group: \"Admin\"", "Terminal Management must be in normal Admin navigation.");
            RequireContains(hostLan, "hostLanEntitled", "Host LAN page must gate controls on entitlement.");
            RequireContains(contracts, "NoTerminalsPurchased", "Entitlement codes must include NoTerminalsPurchased.");

            string licensingSlice = string.Join("\n", entitlementService, contracts, pairingGate);
            RequireNotContains(licensingSlice, "PRIVATE KEY", "No private key in LAN entitlement slice.");
            RequireNotContains(licensingSlice, "HttpClient", "No network client in LAN entitlement slice.");
            RequireNotContains(licensingSlice, "Vercel", "No Vercel in LAN entitlement slice.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void Install(LocalLicenseService service, LocalLicenseDocument document)
    {
        LocalLicenseInstallResponse response = service
            .InstallAsync(new LocalLicenseInstallRequest(JsonSerializer.Serialize(document, JsonOptions)))
            .GetAwaiter()
            .GetResult();
        if (response.InstallStatus != "Installed")
        {
            throw new InvalidOperationException("Test license install failed: " + response.ErrorMessage);
        }
    }

    private static LocalLicenseDocument Host(
        string licenseId,
        int allowedLan,
        IReadOnlyList<string> features) => new(
            LicenseId: licenseId,
            CustomerName: "LAN Entitlement Customer",
            Edition: LicenseEdition.Local,
            IssuedAt: FixedNow.AddDays(-1),
            ExpiresAt: FixedNow.AddDays(60),
            MachineBinding: TestMachine,
            Features: features,
            Signature: "signed-test",
            SchemaVersion: LocalLicenseValidator.SupportedSchemaVersion,
            ApplicationId: LocalLicenseContractPolicy.RequiredApplicationId,
            StoreId: "store-lan-1",
            AllowedLanTerminals: allowedLan,
            LicenseType: LicenseType.Host);

    private static LocalLicenseDocument Terminal(
        string licenseId,
        string hostLicenseId,
        string terminalId) => new(
            LicenseId: licenseId,
            CustomerName: "LAN Entitlement Customer",
            Edition: LicenseEdition.Local,
            IssuedAt: FixedNow.AddDays(-1),
            ExpiresAt: FixedNow.AddDays(60),
            MachineBinding: TestMachine,
            Features: new[] { LicenseFeatures.LanTerminals },
            Signature: "signed-test",
            SchemaVersion: LocalLicenseValidator.SupportedSchemaVersion,
            ApplicationId: LocalLicenseContractPolicy.RequiredApplicationId,
            StoreId: "store-lan-1",
            AllowedLanTerminals: null,
            LicenseType: LicenseType.Terminal,
            HostLicenseId: hostLicenseId,
            TerminalId: terminalId);

    private static string ReadRepo(params string[] parts) =>
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
        if (text.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message + " Forbidden: " + forbidden);
        }
    }
}

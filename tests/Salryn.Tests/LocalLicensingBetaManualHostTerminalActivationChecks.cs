using System.Text.Json;
using System.Text.Json.Serialization;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Licensing;

namespace Salryn.Tests;

/// <summary>
/// LIC-BETA: Host/Terminal types, empty binding reject, terminal cannot unlock host POS,
/// machine code format, activation contact copy, no private key / cloud.
/// </summary>
public static class LocalLicensingBetaManualHostTerminalActivationChecks
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    private const string TestMachine = "SALRYN-MC-ABCD-EFGH-IJKL-MNOP";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "salryn-licensing-beta-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            LocalLicenseFileStore store = new(new LocalLicenseFileStoreOptions(root, null));
            LocalLicenseValidator accepting = new(new LocalLicenseValidatorOptions
            {
                MachineFingerprint = TestMachine,
                SignatureVerifier = _ => true
            }, () => FixedNow);
            LocalLicenseService service = new(store, accepting, () => FixedNow);
            LocalLicenseEnforcementService enforcement = new(service);

            // Empty MachineBinding fails closed.
            WriteLicense(store, Host("LIC-BETA-EMPTY-BIND", FixedNow.AddDays(30), machineBinding: "  "));
            LocalLicenseValidationResult emptyBind = accepting.Validate(store.ReadAsync().GetAwaiter().GetResult());
            RequireTrue(emptyBind.Status == LicenseStatus.Invalid, "Empty MachineBinding must be Invalid.");
            RequireTrue(emptyBind.Message.Contains("MachineBinding", StringComparison.OrdinalIgnoreCase), "Empty binding message must mention MachineBinding.");

            // Missing LicenseType fails closed.
            WriteLicense(store, Host("LIC-BETA-NO-TYPE", FixedNow.AddDays(30)) with { LicenseType = null });
            LocalLicenseValidationResult noType = accepting.Validate(store.ReadAsync().GetAwaiter().GetResult());
            RequireTrue(noType.Status == LicenseStatus.Invalid, "Missing LicenseType must be Invalid.");

            // Valid Host installs and exposes LicenseType.
            LocalLicenseInstallResponse hostInstall = service
                .InstallAsync(new LocalLicenseInstallRequest(JsonSerializer.Serialize(
                    Host("LIC-BETA-HOST", FixedNow.AddDays(30), features: new[] { LicenseFeatures.Pos, LicenseFeatures.Licensing, LicenseFeatures.LanTerminals }, allowedLan: 2),
                    JsonOptions)))
                .GetAwaiter()
                .GetResult();
            RequireTrue(hostInstall.InstallStatus == "Installed", "Valid Host license must install. " + hostInstall.ErrorMessage);
            LocalLicenseValidationResult hostStatus = service.GetStatusAsync().GetAwaiter().GetResult();
            RequireTrue(hostStatus.Status == LicenseStatus.Licensed, "Host status must be Licensed.");
            RequireTrue(hostStatus.LicenseType == LicenseType.Host, "Status must expose LicenseType Host.");
            RequireTrue(hostStatus.AllowedLanTerminals == 2, "Host AllowedLanTerminals must surface.");
            RequireTrue(hostStatus.MachineCode == TestMachine, "Machine code must surface on status.");
            RequireTrue(enforcement.IsFeatureAllowedAsync(LicenseFeatures.Pos).GetAwaiter().GetResult(), "Host must allow Pos.");

            // Terminal license with host-only features cannot unlock Pos.
            LocalLicenseDocument terminalDoc = Terminal(
                "LIC-BETA-TERM",
                FixedNow.AddDays(30),
                hostLicenseId: "LIC-BETA-HOST",
                terminalId: "term-1",
                features: new[] { LicenseFeatures.LanTerminals, LicenseFeatures.Pos, LicenseFeatures.Reports });
            LocalLicenseInstallResponse terminalInstall = service
                .InstallAsync(new LocalLicenseInstallRequest(JsonSerializer.Serialize(terminalDoc, JsonOptions)))
                .GetAwaiter()
                .GetResult();
            RequireTrue(terminalInstall.InstallStatus == "Installed", "Valid Terminal license must install. " + terminalInstall.ErrorMessage);
            LocalLicenseValidationResult termStatus = service.GetStatusAsync().GetAwaiter().GetResult();
            RequireTrue(termStatus.LicenseType == LicenseType.Terminal, "Status must expose LicenseType Terminal.");
            RequireTrue(termStatus.HostLicenseId == "LIC-BETA-HOST", "Terminal HostLicenseId must surface.");
            RequireTrue(termStatus.TerminalId == "term-1", "TerminalId must surface.");
            RequireTrue(!termStatus.EnabledFeatures.Contains(LicenseFeatures.Pos), "Terminal must not enable Pos even if listed.");
            RequireTrue(termStatus.EnabledFeatures.Contains(LicenseFeatures.LanTerminals), "Terminal must enable LanTerminals.");

            LicenseEnforcementDecision termPos = enforcement.RequireFeatureAsync(LicenseFeatures.Pos).GetAwaiter().GetResult();
            RequireTrue(!termPos.Allowed, "Terminal must not allow Pos.");
            RequireTrue(termPos.ReasonCode == LicenseEnforcementReason.WrongLicenseType, "Terminal Pos deny must use WrongLicenseType.");

            LicenseEnforcementDecision termLan = enforcement.RequireFeatureAsync(LicenseFeatures.LanTerminals).GetAwaiter().GetResult();
            RequireTrue(termLan.Allowed, "Terminal must allow LanTerminals.");

            // Host license with Terminal-only fields rejected.
            LocalLicenseDocument hostWithTermFields = Host("LIC-BETA-HOST-BAD", FixedNow.AddDays(30)) with
            {
                HostLicenseId = "x",
                TerminalId = "y"
            };
            LocalLicenseInstallResponse badHost = service
                .InstallAsync(new LocalLicenseInstallRequest(JsonSerializer.Serialize(hostWithTermFields, JsonOptions)))
                .GetAwaiter()
                .GetResult();
            RequireTrue(badHost.InstallStatus == "Rejected", "Host with Terminal fields must be rejected.");

            // Terminal with AllowedLanTerminals rejected.
            LocalLicenseDocument termWithCount = Terminal(
                "LIC-BETA-TERM-COUNT",
                FixedNow.AddDays(30),
                hostLicenseId: "LIC-BETA-HOST",
                terminalId: "term-2",
                features: new[] { LicenseFeatures.LanTerminals }) with
            {
                AllowedLanTerminals = 3
            };
            LocalLicenseInstallResponse badTerm = service
                .InstallAsync(new LocalLicenseInstallRequest(JsonSerializer.Serialize(termWithCount, JsonOptions)))
                .GetAwaiter()
                .GetResult();
            RequireTrue(badTerm.InstallStatus == "Rejected", "Terminal with AllowedLanTerminals must be rejected.");

            // Wrong ApplicationId rejected.
            LocalLicenseDocument wrongApp = Host("LIC-BETA-APP", FixedNow.AddDays(30)) with { ApplicationId = "Other.App" };
            LocalLicenseInstallResponse badApp = service
                .InstallAsync(new LocalLicenseInstallRequest(JsonSerializer.Serialize(wrongApp, JsonOptions)))
                .GetAwaiter()
                .GetResult();
            RequireTrue(badApp.InstallStatus == "Rejected", "Wrong ApplicationId must be rejected.");

            // Machine code format helper.
            byte[] sample = Convert.FromHexString("AABBCCDDEEFF00112233445566778899");
            string code = LocalMachineFingerprint.FormatMachineCode(sample);
            RequireTrue(code.StartsWith(LocalMachineFingerprint.MachineCodePrefix, StringComparison.Ordinal), "Machine code must use SALRYN-MC- prefix.");
            RequireTrue(code == "SALRYN-MC-AABB-CCDD-EEFF-0011", "Machine code grouping must match expected format.");

            // Installation id persistence.
            string installIdA = LocalMachineFingerprint.GetOrCreateInstallationId(root);
            string installIdB = LocalMachineFingerprint.GetOrCreateInstallationId(root);
            RequireTrue(installIdA == installIdB, "Installation id must persist under licensing root.");

            // Source / UI guards.
            string contracts = ReadRepoFile("src", "Salryn.Core", "Services", "Licensing", "LicenseContracts.cs");
            string validator = ReadRepoFile("src", "Salryn.Infrastructure", "Licensing", "LocalLicenseValidator.cs");
            string fingerprint = ReadRepoFile("src", "Salryn.Infrastructure", "Licensing", "LocalMachineFingerprint.cs");
            string signature = ReadRepoFile("src", "Salryn.Infrastructure", "Licensing", "LocalLicenseSignatureVerifier.cs");
            string serviceSrc = ReadRepoFile("src", "Salryn.Infrastructure", "Licensing", "LocalLicenseService.cs");
            string enforcementSrc = ReadRepoFile("src", "Salryn.Infrastructure", "Licensing", "LocalLicenseEnforcementService.cs");
            string licensePage = File.ReadAllText(Path.Combine(FindRoot(), "src", "Salryn.App.Web", "src", "pages", "LicensePage.tsx"));
            string dashboardPage = File.ReadAllText(Path.Combine(FindRoot(), "src", "Salryn.App.Web", "src", "pages", "DashboardPage.tsx"));
            string appShell = File.ReadAllText(Path.Combine(FindRoot(), "src", "Salryn.App.Web", "src", "components", "AppShell.tsx"));
            string storeSettingsPage = File.ReadAllText(Path.Combine(FindRoot(), "src", "Salryn.App.Web", "src", "pages", "StoreSettingsPage.tsx"));
            string hostLan = File.ReadAllText(Path.Combine(FindRoot(), "src", "Salryn.App.Web", "src", "pages", "HostLanControlPage.tsx"));
            string backupPage = File.ReadAllText(Path.Combine(FindRoot(), "src", "Salryn.App.Web", "src", "pages", "BackupRestorePage.tsx"));
            string navigation = File.ReadAllText(Path.Combine(FindRoot(), "src", "Salryn.App.Web", "src", "navigation.ts"));
            string policy = ReadRepoFile("src", "Salryn.Core", "Services", "Licensing", "LocalLicenseContractPolicy.cs");

            RequireContains(contracts, "enum LicenseType", "LicenseType enum must exist.");
            RequireContains(contracts, "WrongLicenseType", "WrongLicenseType enforcement reason must exist.");
            RequireContains(validator, "MachineBinding is required", "Validator must reject empty MachineBinding.");
            RequireContains(validator, "LicenseType", "Validator must require LicenseType.");
            RequireContains(fingerprint, "MachineCodePrefix", "Fingerprint must expose machine code prefix.");
            RequireContains(fingerprint, "installation-id", "Fingerprint must use installation id material.");
            RequireContains(signature, "LicenseType", "Signing payload must include LicenseType.");
            RequireContains(signature, "HostLicenseId", "Signing payload must include HostLicenseId.");
            RequireContains(signature, "TerminalId", "Signing payload must include TerminalId.");
            RequireContains(enforcementSrc, "WrongLicenseType", "Enforcement must reject host features for Terminal.");
            RequireContains(licensePage, "salryn.info@gmail.com", "License UI must show beta contact email.");
            RequireContains(licensePage, "Machine Code", "License UI must show machine code.");
            RequireContains(licensePage, "Activate Salryn", "License UI must use product activation title.");
            RequireContains(licensePage, "Copy Machine Code", "Host activation must offer copy Machine Code.");
            RequireContains(licensePage, "Salryn generates this Machine Code", "Host activation must state machine-code rule.");
            RequireContains(licensePage, "Kirjane Labs returns a signed Host license", "Host activation must explain the signed-license return flow.");
            RequireContains(licensePage, "Each LAN terminal needs its own Terminal license", "License UI must separate the Terminal license boundary.");
            RequireContains(licensePage, "Terminal users do not need a Host License ID", "Host page must not force Host License ID onto terminal users.");
            RequireContains(licensePage, "This device needs a signed local license before setup can continue.", "Minimalist fresh subtitle required.");
            RequireContains(licensePage, "Salryn is activated", "Minimalist active title required.");
            RequireNotContains(licensePage, "how many LAN terminals", "Host activation must not collect terminal count.");
            RequireNotContains(licensePage, "Allowed LAN terminals", "Host activation must not expose Allowed LAN terminals as purchase UI.");
            RequireNotContains(licensePage, "Offline-first license boundary", "License UI must not lead with developer boundary banners.");
            RequireNotContains(licensePage, "path-bearing", "License UI must not expose path-bearing technical reject jargon.");
            RequireNotContains(licensePage, "empty-binding", "License UI must not expose empty-binding technical reject jargon.");
            RequireNotContains(dashboardPage, "licensing entitlement, and client deployment remain deferred", "Dashboard must not describe completed license enforcement as deferred.");
            RequireNotContains(appShell, "LAN terminal licensing and entitlement remain deferred", "Navigation must not describe completed license enforcement as deferred.");
            RequireNotContains(storeSettingsPage, "Controlled backup and licensing controls will be added", "Settings must not describe implemented Admin pages as future work.");
            RequireContains(licensePage, "Salryn works offline after a valid signed license is installed", "License UI must state offline-after-install.");
            RequireNotContains(licensePage, "cloud activation ready", "License UI must not claim cloud activation.");
            RequireNotContains(licensePage, "subscription billing", "License UI must not claim subscription billing.");
            RequireNotContains(licensePage, "LAN production ready", "License UI must not claim LAN production ready.");
            RequireContains(hostLan, "title=\"Terminal Management\"", "Internal host LAN page must use Terminal Management naming.");
            RequireContains(hostLan, "Terminal license request (admin / support)", "Host Terminal Management must expose admin request context.");
            RequireContains(hostLan, "Copy Terminal License Request", "Host Terminal Management must offer a copy-ready request block.");
            RequireContains(navigation, "VITE_SALRYN_INTERNAL_SURFACES", "Incomplete terminal management must be hidden from normal navigation.");
            RequireNotContains(backupPage, "local license file is stored outside this database backup package", "Backup page must not show an implementation-boundary wall.");
            RequireContains(policy, "salryn.info@gmail.com", "Policy must document beta contact email.");
            RequireContains(policy, "PrivateSigningKeyAllowedInRepository = false", "Policy must forbid private keys.");

            string terminalActivation = File.ReadAllText(Path.Combine(FindRoot(), "src", "Salryn.App.Web", "src", "pages", "TerminalActivationPage.tsx"));
            RequireContains(terminalActivation, "Copy Terminal Machine Code", "Terminal activation must copy terminal machine code.");
            RequireContains(terminalActivation, "do not need a Host License ID", "Terminal activation must not require Host License ID from end users.");
            RequireNotContains(terminalActivation, "Include your store name, Host License ID", "Terminal activation must not ask end users for Host License ID.");
            RequireContains(terminalActivation, "Host licenses cannot activate a terminal", "Terminal import must reject Host licenses in copy.");
            RequireContains(terminalActivation, "Show support details", "Terminal support details must be collapsed by default.");

            string licensingSources = string.Join("\n", contracts, validator, fingerprint, signature, serviceSrc, enforcementSrc);
            RequireNotContains(licensingSources, "PRIVATE KEY", "Licensing sources must not include private keys.");
            RequireNotContains(licensingSources, "BEGIN RSA PRIVATE", "Licensing sources must not include RSA private keys.");
            RequireNotContains(licensingSources, "HttpClient", "Licensing sources must not add network client.");
            RequireNotContains(licensingSources, "Vercel", "Licensing sources must not add Vercel.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static LocalLicenseDocument Host(
        string licenseId,
        DateTimeOffset? expiresAt,
        string? machineBinding = TestMachine,
        IReadOnlyList<string>? features = null,
        int allowedLan = 0) => new(
            LicenseId: licenseId,
            CustomerName: "Beta Customer",
            Edition: LicenseEdition.Local,
            IssuedAt: FixedNow.AddDays(-1),
            ExpiresAt: expiresAt,
            MachineBinding: machineBinding,
            Features: features ?? new[] { LicenseFeatures.Pos, LicenseFeatures.Licensing },
            Signature: "signed-test",
            SchemaVersion: LocalLicenseValidator.SupportedSchemaVersion,
            ApplicationId: LocalLicenseContractPolicy.RequiredApplicationId,
            StoreId: "store-beta-1",
            AllowedLanTerminals: allowedLan,
            LicenseType: LicenseType.Host);

    private static LocalLicenseDocument Terminal(
        string licenseId,
        DateTimeOffset? expiresAt,
        string hostLicenseId,
        string terminalId,
        IReadOnlyList<string> features,
        string? machineBinding = TestMachine) => new(
            LicenseId: licenseId,
            CustomerName: "Beta Customer",
            Edition: LicenseEdition.Local,
            IssuedAt: FixedNow.AddDays(-1),
            ExpiresAt: expiresAt,
            MachineBinding: machineBinding,
            Features: features,
            Signature: "signed-test",
            SchemaVersion: LocalLicenseValidator.SupportedSchemaVersion,
            ApplicationId: LocalLicenseContractPolicy.RequiredApplicationId,
            StoreId: "store-beta-1",
            AllowedLanTerminals: null,
            LicenseType: LicenseType.Terminal,
            HostLicenseId: hostLicenseId,
            TerminalId: terminalId);

    private static void WriteLicense(LocalLicenseFileStore store, LocalLicenseDocument document)
    {
        Directory.CreateDirectory(store.RootDirectory);
        File.WriteAllText(store.LicenseFilePath, JsonSerializer.Serialize(document, JsonOptions));
    }

    private static string ReadRepoFile(params string[] pathParts)
    {
        string root = FindRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(pathParts).ToArray()));
    }

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

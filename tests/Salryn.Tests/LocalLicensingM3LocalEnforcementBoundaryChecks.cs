using System.Text.Json;
using System.Text.Json.Serialization;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Licensing;

namespace Salryn.Tests;

public static class LocalLicensingM3LocalEnforcementBoundaryChecks
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 12, 1, 0, 0, TimeSpan.Zero);
    private const string TestMachine = "SALRYN-MC-TEST-M3AA-M3BB-M3CC";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "salryn-licensing-m3-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            LocalLicenseFileStore store = new(new LocalLicenseFileStoreOptions(root, null));

            LocalLicenseService noVerifierLicenseService = new(
                store,
                new LocalLicenseValidator(new LocalLicenseValidatorOptions
                {
                    MachineFingerprint = TestMachine
                }, () => FixedNow),
                () => FixedNow);
            LocalLicenseEnforcementService noVerifierEnforcement = new(noVerifierLicenseService);

            LicenseEnforcementDecision missing = noVerifierEnforcement
                .RequireFeatureAsync(LicenseFeatures.Pos)
                .GetAwaiter()
                .GetResult();
            RequireDecision(missing, false, LicenseEnforcementReason.MissingLicense, "Missing license must deny POS feature.");

            File.WriteAllText(store.LicenseFilePath, "{not-valid-json");
            LicenseEnforcementDecision malformed = noVerifierEnforcement
                .RequireFeatureAsync(LicenseFeatures.Pos)
                .GetAwaiter()
                .GetResult();
            RequireDecision(malformed, false, LicenseEnforcementReason.InvalidLicense, "Malformed local license must deny feature access.");

            LocalLicenseService acceptingLicenseService = new(
                store,
                new LocalLicenseValidator(new LocalLicenseValidatorOptions
                {
                    MachineFingerprint = TestMachine,
                    SignatureVerifier = _ => true
                }, () => FixedNow),
                () => FixedNow);
            LocalLicenseEnforcementService acceptingEnforcement = new(acceptingLicenseService);

            WriteLicense(store, License("LIC-M3-EXPIRED", FixedNow.AddDays(-1), signature: "signed-test", features: new[] { LicenseFeatures.Pos }));
            LicenseEnforcementDecision expired = acceptingEnforcement
                .RequireFeatureAsync(LicenseFeatures.Pos)
                .GetAwaiter()
                .GetResult();
            RequireDecision(expired, false, LicenseEnforcementReason.ExpiredLicense, "Expired local license must deny feature access.");

            WriteLicense(store, License("LIC-M3-UNSIGNED", FixedNow.AddDays(30), signature: null, features: new[] { LicenseFeatures.Pos }));
            LicenseEnforcementDecision unsigned = acceptingEnforcement
                .RequireFeatureAsync(LicenseFeatures.Pos)
                .GetAwaiter()
                .GetResult();
            RequireDecision(unsigned, false, LicenseEnforcementReason.InvalidLicense, "Unsigned production license must deny feature access.");

            WriteLicense(store, License("LIC-M3-NOVERIFIER", FixedNow.AddDays(30), signature: "signed-test", features: new[] { LicenseFeatures.Pos }));
            LicenseEnforcementDecision noVerifier = noVerifierEnforcement
                .RequireFeatureAsync(LicenseFeatures.Pos)
                .GetAwaiter()
                .GetResult();
            RequireDecision(noVerifier, false, LicenseEnforcementReason.VerifierUnavailable, "Signed license with no verifier must deny feature access.");

            WriteLicense(store, License("LIC-M3-MACHINE", FixedNow.AddDays(30), signature: "signed-test", features: new[] { LicenseFeatures.Pos }, machineBinding: "machine-a"));
            LocalLicenseService wrongMachineLicenseService = new(
                store,
                new LocalLicenseValidator(new LocalLicenseValidatorOptions
                {
                    MachineFingerprint = "machine-b",
                    SignatureVerifier = _ => true
                }, () => FixedNow),
                () => FixedNow);
            LicenseEnforcementDecision machineMismatch = new LocalLicenseEnforcementService(wrongMachineLicenseService)
                .RequireFeatureAsync(LicenseFeatures.Pos)
                .GetAwaiter()
                .GetResult();
            RequireDecision(machineMismatch, false, LicenseEnforcementReason.MachineMismatch, "Machine mismatch must deny feature access.");

            WriteLicense(store, License("LIC-M3-VALID", FixedNow.AddDays(30), signature: "signed-test", features: new[] { LicenseFeatures.Pos, LicenseFeatures.Licensing }));
            string beforeEnforcement = File.ReadAllText(store.LicenseFilePath);

            LicenseEnforcementDecision validPos = acceptingEnforcement
                .RequireFeatureAsync(LicenseFeatures.Pos)
                .GetAwaiter()
                .GetResult();
            RequireDecision(validPos, true, LicenseEnforcementReason.LicensedFeatureAllowed, "Valid signed license with verifier must allow included feature.");
            RequireTrue(acceptingEnforcement.IsFeatureAllowedAsync(LicenseFeatures.Pos).GetAwaiter().GetResult(), "IsFeatureAllowedAsync must return true for allowed included feature.");

            LicenseEnforcementDecision missingFeature = acceptingEnforcement
                .RequireFeatureAsync(LicenseFeatures.Reports)
                .GetAwaiter()
                .GetResult();
            RequireDecision(missingFeature, false, LicenseEnforcementReason.FeatureNotLicensed, "Valid signed license must deny a feature that is not included.");
            RequireTrue(!acceptingEnforcement.IsFeatureAllowedAsync(LicenseFeatures.Reports).GetAwaiter().GetResult(), "IsFeatureAllowedAsync must return false for feature not included.");

            LicenseEnforcementDecision unknown = acceptingEnforcement
                .RequireFeatureAsync("NotAFeature")
                .GetAwaiter()
                .GetResult();
            RequireDecision(unknown, false, LicenseEnforcementReason.UnknownFeature, "Unknown feature must deny access.");
            RequireTrue(File.ReadAllText(store.LicenseFilePath) == beforeEnforcement, "Enforcement checks must not mutate local license file content.");

            string devRoot = Path.Combine(root, "dev");
            Directory.CreateDirectory(devRoot);
            LocalLicenseFileStore devStore = new(new LocalLicenseFileStoreOptions(devRoot, null));
            LocalLicenseService devLicenseService = new(
                devStore,
                new LocalLicenseValidator(new LocalLicenseValidatorOptions
                {
                    AllowDevelopmentMode = true,
                    DevelopmentFeatures = new[] { LicenseFeatures.Licensing }
                }, () => FixedNow),
                () => FixedNow);
            LocalLicenseEnforcementService devEnforcement = new(devLicenseService);

            LicenseEnforcementDecision devAllowed = devEnforcement
                .RequireFeatureAsync(LicenseFeatures.Licensing)
                .GetAwaiter()
                .GetResult();
            RequireDecision(devAllowed, true, LicenseEnforcementReason.DevelopmentModeAllowed, "Development mode must allow explicitly configured features.");

            LicenseEnforcementDecision devDenied = devEnforcement
                .RequireFeatureAsync(LicenseFeatures.Pos)
                .GetAwaiter()
                .GetResult();
            RequireDecision(devDenied, false, LicenseEnforcementReason.FeatureNotLicensed, "Development mode must deny features not explicitly configured.");

            string endpointSource = ReadRepoFile("src", "Salryn.Api.Local", "LocalLicenseEndpoints.cs");
            RequireContains(endpointSource, "MapGet(\"/api/license/status\"", "M1 status endpoint must remain mapped.");
            RequireContains(endpointSource, "MapPost(\"/api/license/install\"", "M2 install endpoint must remain mapped.");
            RequireContains(endpointSource, "MapGet(\"/api/license/entitlements\"", "M3 entitlement endpoint must be mapped read-only.");
            RequireContains(endpointSource, "RequireLoopbackOperatorRequest", "License API endpoints must preserve loopback/local guard.");
            RequireContains(endpointSource, "RequireAuthenticatedSession", "Read-only license API endpoints must require authenticated session.");
            RequireContains(endpointSource, "LocalApiPermissions.SettingsManage", "License install must remain admin/settings guarded.");
            RequireNotContains(endpointSource, "MapPut", "M3 must not add broad license mutation endpoints.");
            RequireNotContains(endpointSource, "MapDelete", "M3 must not add broad license mutation endpoints.");

            string apiGuardSource = ReadRepoFile("src", "Salryn.Api.Local", "Auth", "ApiLicenseGuard.cs");
            RequireContains(apiGuardSource, "RequireLicensedFeatureAsync", "API license guard helper must expose one shared feature guard.");
            RequireContains(apiGuardSource, "License.FeatureDenied", "API license guard must return deterministic denied code.");

            string implementationSource = string.Join("\n", new[]
            {
                ReadRepoFile("src", "Salryn.Core", "Services", "Licensing", "LicenseContracts.cs"),
                ReadRepoFile("src", "Salryn.Core", "Services", "Licensing", "ILocalLicenseService.cs"),
                ReadRepoFile("src", "Salryn.Core", "Services", "Licensing", "ILocalLicenseEnforcementService.cs"),
                ReadRepoFile("src", "Salryn.Infrastructure", "Licensing", "LocalLicenseService.cs"),
                ReadRepoFile("src", "Salryn.Infrastructure", "Licensing", "LocalLicenseEnforcementService.cs"),
                ReadRepoFile("src", "Salryn.Infrastructure", "Licensing", "LocalLicenseSignatureVerifier.cs"),
                ReadRepoFile("src", "Salryn.Infrastructure", "Licensing", "LocalMachineFingerprint.cs"),
                ReadRepoFile("src", "Salryn.Api.Local", "Auth", "ApiLicenseGuard.cs"),
                ReadRepoFile("src", "Salryn.Api.Local", "LocalLicenseEndpoints.cs")
            });
            RequireContains(implementationSource, "LocalLicenseSignatureVerifier.Verify", "LIC-FINAL default service must use public-key signature verification.");
            RequireContains(implementationSource, "PublicKeyPem", "LIC-FINAL verifier must carry only a public key.");
            RequireContains(implementationSource, "AllowedLanTerminals", "LIC-FINAL license schema must carry LAN terminal entitlement count.");
            RequireContains(implementationSource, "SupportExpiresAt", "LIC-FINAL license schema must carry support entitlement display.");
            RequireContains(implementationSource, "UpdatesUntil", "LIC-FINAL license schema must carry update entitlement display.");
            RequireNotContains(implementationSource, "PRIVATE KEY", "LIC-FINAL implementation must not include private keys.");
            RequireNotContains(implementationSource, "HttpClient", "Licensing M3 must not add network dependency.");
            RequireNotContains(implementationSource, "Vercel", "Licensing M3 must not add Vercel licensing.");
            RequireNotContains(implementationSource, "Payment", "Licensing M3 must not add payment dependency.");
            RequireNotContains(implementationSource, "Subscription", "Licensing M3 must not add subscription sync.");

            string webRoot = Path.Combine(FindRoot(), "src", "Salryn.App.Web", "src");
            string navigation = File.ReadAllText(Path.Combine(webRoot, "navigation.ts"));
            string permissions = File.ReadAllText(Path.Combine(webRoot, "security", "permissions.ts"));
            string app = File.ReadAllText(Path.Combine(webRoot, "App.tsx"));
            string shell = File.ReadAllText(Path.Combine(webRoot, "components", "AppShell.tsx"));
            string licenseApi = File.ReadAllText(Path.Combine(webRoot, "api", "license.ts"));
            string licensePage = File.ReadAllText(Path.Combine(webRoot, "pages", "LicensePage.tsx"));
            string backupPage = File.ReadAllText(Path.Combine(webRoot, "pages", "BackupRestorePage.tsx"));
            RequireContains(navigation, "id: \"license\", label: \"License\"", "License page must appear as an admin nav item.");
            RequireContains(permissions, "license: [Permissions.SettingsManage]", "License page must require Settings.Manage/admin-style access.");
            RequireContains(permissions, "const cashierPages: readonly AppPage[] = [\"checkout\", \"sales\"]", "Cashier page allowlist must not include license management.");
            RequireTrue(!permissions.Contains("\"license\", \"audit\"", StringComparison.Ordinal), "Manager allowlist must not include license management.");
            RequireContains(app, "LicensePage", "App must render the License page.");
            RequireContains(shell, "License: ShieldCheck", "AppShell must map the License nav label safely.");
            RequireContains(licenseApi, "/api/license/status", "License API client must use status endpoint.");
            RequireContains(licenseApi, "/api/license/install", "License API client must use install endpoint.");
            RequireContains(licenseApi, "licenseJson", "License import must send signed local license content only.");
            RequireContains(licensePage, "offline signed license", "License UI must describe offline signed licensing.");
            RequireContains(licensePage, "public-key verification", "License UI must expose public-key verification boundary.");
            RequireContains(licensePage, "LAN terminal production use is not claimed", "License UI must not claim LAN production readiness.");
            RequireContains(licensePage, "not cloud licensing", "License UI must reject cloud licensing claims.");
            RequireNotContains(licensePage, "type=\"file\"", "License UI must not expose arbitrary file upload.");
            RequireNotContains(licensePage, "private key", "License UI must not mention private key handling.");
            RequireContains(backupPage, "local license file is stored outside this database backup package", "Backup UI must not claim license preservation.");

            string productionReportEndpoint = ReadRepoFile("src", "Salryn.Api.Local", "LocalProductionReportEndpoints.cs");
            RequireNotContains(productionReportEndpoint, "LocalLicense", "Licensing M3 must not alter reporting endpoint behavior.");
            string projectionService = ReadRepoFile("src", "Salryn.Infrastructure", "Reports", "PreCrystalReportDatasetService.cs");
            RequireNotContains(projectionService, "LocalLicense", "Licensing M3 must not alter RMO/report projection semantics.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static LocalLicenseDocument License(
        string licenseId,
        DateTimeOffset? expiresAt,
        string? signature,
        IReadOnlyList<string> features,
        string? machineBinding = TestMachine) => new(
            LicenseId: licenseId,
            CustomerName: "Salryn Local Test",
            Edition: LicenseEdition.Local,
            IssuedAt: FixedNow.AddDays(-1),
            ExpiresAt: expiresAt,
            MachineBinding: machineBinding,
            Features: features,
            Signature: signature,
            SchemaVersion: LocalLicenseValidator.SupportedSchemaVersion,
            ApplicationId: LocalLicenseContractPolicy.RequiredApplicationId,
            StoreId: "store-m3-test",
            AllowedLanTerminals: 0,
            LicenseType: LicenseType.Host);

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

    private static void RequireDecision(
        LicenseEnforcementDecision decision,
        bool allowed,
        LicenseEnforcementReason reason,
        string message)
    {
        RequireTrue(decision.Allowed == allowed, message + " Allowed was " + decision.Allowed + ".");
        RequireTrue(decision.ReasonCode == reason, message + " Reason was " + decision.ReasonCode + ".");
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

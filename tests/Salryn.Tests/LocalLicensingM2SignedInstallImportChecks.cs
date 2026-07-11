using System.Text.Json;
using System.Text.Json.Serialization;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Licensing;

namespace Salryn.Tests;

public static class LocalLicensingM2SignedInstallImportChecks
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 11, 10, 30, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "salryn-licensing-m2-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            LocalLicenseFileStore store = new(new LocalLicenseFileStoreOptions(root, null));
            LocalLicenseService noVerifierService = new(
                store,
                new LocalLicenseValidator(clock: () => FixedNow),
                () => FixedNow);

            LocalLicenseValidationResult missing = noVerifierService.GetStatusAsync().GetAwaiter().GetResult();
            RequireTrue(missing.Status == LicenseStatus.Unlicensed, "Missing license must remain Unlicensed under M2.");

            LocalLicenseInstallResponse malformed = noVerifierService
                .InstallAsync(new LocalLicenseInstallRequest("{not-json"))
                .GetAwaiter()
                .GetResult();
            RequireRejected(malformed, LicenseStatus.Invalid, "Malformed install must be rejected.");
            RequireTrue(!File.Exists(store.LicenseFilePath), "Malformed install must not create a local license file.");

            LocalLicenseInstallResponse unsigned = noVerifierService
                .InstallAsync(Request(License("LIC-M2-UNSIGNED", FixedNow.AddDays(30), signature: null, features: new[] { LicenseFeatures.Pos })))
                .GetAwaiter()
                .GetResult();
            RequireRejected(unsigned, LicenseStatus.Invalid, "Unsigned production license must be rejected.");

            LocalLicenseInstallResponse noVerifier = noVerifierService
                .InstallAsync(Request(License("LIC-M2-NOVERIFIER", FixedNow.AddDays(30), signature: "signed-test", features: new[] { LicenseFeatures.Pos })))
                .GetAwaiter()
                .GetResult();
            RequireRejected(noVerifier, LicenseStatus.Invalid, "Signed production license must fail closed when verifier is unavailable.");

            LocalLicenseService acceptingService = new(
                store,
                new LocalLicenseValidator(new LocalLicenseValidatorOptions
                {
                    SignatureVerifier = _ => true
                }, () => FixedNow),
                () => FixedNow);

            LocalLicenseInstallResponse expired = acceptingService
                .InstallAsync(Request(License("LIC-M2-EXPIRED", FixedNow.AddDays(-1), signature: "signed-test", features: new[] { LicenseFeatures.Pos })))
                .GetAwaiter()
                .GetResult();
            RequireRejected(expired, LicenseStatus.Expired, "Expired signed license must be rejected before replacement.");

            LocalLicenseInstallResponse unsupportedSchema = acceptingService
                .InstallAsync(Request(License("LIC-M2-SCHEMA", FixedNow.AddDays(30), signature: "signed-test", features: new[] { LicenseFeatures.Pos }, schemaVersion: 99)))
                .GetAwaiter()
                .GetResult();
            RequireRejected(unsupportedSchema, LicenseStatus.Invalid, "Unsupported schema must be rejected.");

            LocalLicenseInstallResponse first = acceptingService
                .InstallAsync(Request(License("LIC-M2-FIRST", FixedNow.AddDays(30), signature: "signed-test", features: new[] { LicenseFeatures.Pos, LicenseFeatures.Licensing })))
                .GetAwaiter()
                .GetResult();
            RequireTrue(first.InstallStatus == "Installed" && first.ValidationStatus == LicenseStatus.Licensed, "Valid signed license must install when verifier accepts it.");
            RequireTrue(first.BackupPath is null, "First install must not report a backup when no previous license exists.");
            RequireTrue(File.Exists(store.LicenseFilePath), "Accepted install must write controlled license file.");

            LocalLicenseService rejectingVerifierService = new(
                store,
                new LocalLicenseValidator(new LocalLicenseValidatorOptions
                {
                    SignatureVerifier = _ => false
                }, () => FixedNow),
                () => FixedNow);
            LocalLicenseInstallResponse rejectedSignature = rejectingVerifierService
                .InstallAsync(Request(License("LIC-M2-REJECTED-SIGNATURE", FixedNow.AddDays(30), signature: "signed-test", features: new[] { LicenseFeatures.Pos })))
                .GetAwaiter()
                .GetResult();
            RequireRejected(rejectedSignature, LicenseStatus.Invalid, "Valid-looking signed license must not install when verifier rejects it.");
            RequireCurrentLicenseId(store, "LIC-M2-FIRST", "Rejected verifier must not corrupt existing license.");

            LocalLicenseInstallResponse second = acceptingService
                .InstallAsync(Request(License("LIC-M2-SECOND", FixedNow.AddDays(60), signature: "signed-test", features: new[] { LicenseFeatures.Pos, LicenseFeatures.Reports })))
                .GetAwaiter()
                .GetResult();
            RequireTrue(second.InstallStatus == "Installed" && second.ValidationStatus == LicenseStatus.Licensed, "Second accepted license must install.");
            RequireTrue(second.BackupPath is not null, "Replacing existing license must report a controlled backup path.");
            RequireTrue(File.Exists(second.BackupPath!), "Replacing existing license must create a backup file.");
            RequireTrue(File.ReadAllText(second.BackupPath!).Contains("LIC-M2-FIRST", StringComparison.Ordinal), "Backup must contain previous license document.");
            RequireCurrentLicenseId(store, "LIC-M2-SECOND", "Second install must become the current license.");

            LocalLicenseInstallResponse failedAfterExisting = acceptingService
                .InstallAsync(new LocalLicenseInstallRequest("{still-not-json"))
                .GetAwaiter()
                .GetResult();
            RequireRejected(failedAfterExisting, LicenseStatus.Invalid, "Failed install after existing license must be rejected.");
            RequireCurrentLicenseId(store, "LIC-M2-SECOND", "Failed install must not corrupt existing license.");

            LocalLicenseInstallResponse callerField = acceptingService
                .InstallAsync(new LocalLicenseInstallRequest("""
                    {
                      "licenseId": "LIC-M2-PATH-FIELD",
                      "customerName": "Salryn Local Test",
                      "edition": "Local",
                      "issuedAt": "2026-07-10T00:00:00+00:00",
                      "expiresAt": "2026-08-10T00:00:00+00:00",
                      "machineBinding": null,
                      "features": ["Pos"],
                      "signature": "signed-test",
                      "schemaVersion": 1,
                      "callerSuppliedPath": "C:/unsafe/license.json"
                    }
                    """))
                .GetAwaiter()
                .GetResult();
            RequireRejected(callerField, LicenseStatus.Invalid, "Caller filesystem fields must not be accepted.");
            RequireCurrentLicenseId(store, "LIC-M2-SECOND", "Caller field rejection must not corrupt existing license.");

            string endpointSource = ReadRepoFile("src", "Salryn.Api.Local", "LocalLicenseEndpoints.cs");
            RequireContains(endpointSource, "MapGet(\"/api/license/status\"", "M1 status endpoint must remain mapped.");
            RequireContains(endpointSource, "MapPost(\"/api/license/install\"", "M2 install endpoint must be mapped narrowly.");
            RequireContains(endpointSource, "RequireLoopbackOperatorRequest", "Install endpoint must preserve loopback guard.");
            RequireContains(endpointSource, "RequirePermission", "Install endpoint must require authenticated permission guard.");
            RequireContains(endpointSource, "LocalApiPermissions.SettingsManage", "Install endpoint must use the existing Settings.Manage permission.");
            RequireNotContains(endpointSource, "LicensePath", "Endpoint contract must not accept caller license path fields.");
            RequireNotContains(endpointSource, "OutputPath", "Endpoint contract must not accept caller output path fields.");

            string implementationSource = string.Join("\n", new[]
            {
                ReadRepoFile("src", "Salryn.Core", "Services", "Licensing", "LicenseContracts.cs"),
                ReadRepoFile("src", "Salryn.Infrastructure", "Licensing", "LocalLicenseFileStore.cs"),
                ReadRepoFile("src", "Salryn.Infrastructure", "Licensing", "LocalLicenseValidator.cs"),
                ReadRepoFile("src", "Salryn.Infrastructure", "Licensing", "LocalLicenseService.cs"),
                ReadRepoFile("src", "Salryn.Api.Local", "LocalLicenseEndpoints.cs")
            });
            RequireNotContains(implementationSource, "HttpClient", "Licensing M2 must not add network dependency.");
            RequireNotContains(implementationSource, "Vercel", "Licensing M2 must not add Vercel licensing.");
            RequireNotContains(implementationSource, "Payment", "Licensing M2 must not add payment dependency.");
            RequireNotContains(implementationSource, "Subscription", "Licensing M2 must not add subscription sync.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static LocalLicenseInstallRequest Request(LocalLicenseDocument document) => new(
        JsonSerializer.Serialize(document, JsonOptions));

    private static LocalLicenseDocument License(
        string licenseId,
        DateTimeOffset? expiresAt,
        string? signature,
        IReadOnlyList<string> features,
        string? machineBinding = null,
        int schemaVersion = LocalLicenseValidator.SupportedSchemaVersion) => new(
            LicenseId: licenseId,
            CustomerName: "Salryn Local Test",
            Edition: LicenseEdition.Local,
            IssuedAt: FixedNow.AddDays(-1),
            ExpiresAt: expiresAt,
            MachineBinding: machineBinding,
            Features: features,
            Signature: signature,
            SchemaVersion: schemaVersion);

    private static void RequireCurrentLicenseId(
        LocalLicenseFileStore store,
        string expectedLicenseId,
        string message)
    {
        LocalLicenseFileReadResult read = store.ReadAsync().GetAwaiter().GetResult();
        RequireTrue(read.Document?.LicenseId == expectedLicenseId, message);
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

    private static void RequireRejected(
        LocalLicenseInstallResponse response,
        LicenseStatus expectedValidationStatus,
        string message)
    {
        RequireTrue(response.InstallStatus == "Rejected", message + " Install status was " + response.InstallStatus + ".");
        RequireTrue(response.ValidationStatus == expectedValidationStatus, message + " Validation status was " + response.ValidationStatus + ".");
        RequireTrue(!string.IsNullOrWhiteSpace(response.ErrorCode), message + " Error code is required.");
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

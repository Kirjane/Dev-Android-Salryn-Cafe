using System.Text.Json;
using System.Text.Json.Serialization;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Licensing;

namespace Salryn.Tests;

public static class LocalLicensingM1FoundationChecks
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 11, 9, 0, 0, TimeSpan.Zero);
    private const string TestMachine = "SALRYN-MC-TEST-M1AA-M1BB-M1CC";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "salryn-licensing-m1-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            LocalLicenseFileStore store = new(new LocalLicenseFileStoreOptions(root, null));
            LocalLicenseValidator validator = new(clock: () => FixedNow);
            LocalLicenseService service = new(store, validator);

            LocalLicenseValidationResult missing = service.GetStatusAsync().GetAwaiter().GetResult();
            RequireTrue(missing.Status == LicenseStatus.Unlicensed, "Missing local license file must be Unlicensed.");
            RequireTrue(!service.HasFeatureAsync(LicenseFeatures.Pos).GetAwaiter().GetResult(), "Unlicensed feature checks must fail closed.");
            RequireTrue(missing.SourcePath == store.LicenseFilePath, "Missing result must expose controlled source path.");

            LocalLicenseValidator devValidator = new(new LocalLicenseValidatorOptions
            {
                AllowDevelopmentMode = true,
                DevelopmentFeatures = new[] { LicenseFeatures.Pos, LicenseFeatures.Licensing }
            }, () => FixedNow);
            LocalLicenseValidationResult dev = devValidator.Validate(store.ReadAsync().GetAwaiter().GetResult());
            RequireTrue(dev.Status == LicenseStatus.Development, "Development mode must require explicit local option.");
            RequireTrue(dev.EnabledFeatures.SequenceEqual(new[] { LicenseFeatures.Pos, LicenseFeatures.Licensing }), "Development features must come from explicit options.");

            File.WriteAllText(store.LicenseFilePath, "{not-valid-json");
            LocalLicenseValidationResult malformed = service.GetStatusAsync().GetAwaiter().GetResult();
            RequireTrue(malformed.Status == LicenseStatus.Invalid, "Malformed JSON must be Invalid.");

            WriteLicense(store, License(
                expiresAt: FixedNow.AddDays(-1),
                signature: "signed-test",
                features: new[] { LicenseFeatures.Pos, LicenseFeatures.Rmo }));
            LocalLicenseValidator acceptingExpiredSignature = new(new LocalLicenseValidatorOptions
            {
                MachineFingerprint = TestMachine,
                SignatureVerifier = _ => true
            }, () => FixedNow);
            LocalLicenseValidationResult expired = acceptingExpiredSignature.Validate(store.ReadAsync().GetAwaiter().GetResult());
            RequireTrue(expired.Status == LicenseStatus.Expired, "Expired local license must be Expired.");

            WriteLicense(store, License(
                expiresAt: FixedNow.AddDays(30),
                signature: null,
                features: new[] { LicenseFeatures.Pos, LicenseFeatures.Reports }));
            LocalLicenseValidator verifierPresent = new(new LocalLicenseValidatorOptions
            {
                MachineFingerprint = TestMachine,
                SignatureVerifier = _ => true
            }, () => FixedNow);
            LocalLicenseValidationResult unsigned = verifierPresent.Validate(store.ReadAsync().GetAwaiter().GetResult());
            RequireTrue(unsigned.Status == LicenseStatus.Invalid, "Unsigned production license must not become Licensed.");

            WriteLicense(store, License(
                expiresAt: FixedNow.AddDays(30),
                signature: "signed-test",
                features: new[] { LicenseFeatures.Pos, "UnknownFeature" }));
            LocalLicenseValidationResult noVerifier = service.GetStatusAsync().GetAwaiter().GetResult();
            RequireTrue(noVerifier.Status == LicenseStatus.Invalid, "Signed license must fail closed when no signature verifier is configured.");

            WriteLicense(store, License(
                expiresAt: FixedNow.AddDays(30),
                signature: "signed-test",
                machineBinding: "machine-a",
                features: new[] { LicenseFeatures.Pos }));
            LocalLicenseValidator wrongMachine = new(new LocalLicenseValidatorOptions
            {
                MachineFingerprint = "machine-b",
                SignatureVerifier = _ => true
            }, () => FixedNow);
            LocalLicenseValidationResult machineMismatch = wrongMachine.Validate(store.ReadAsync().GetAwaiter().GetResult());
            RequireTrue(machineMismatch.Status == LicenseStatus.Invalid && !machineMismatch.MachineMatched, "Machine binding mismatch must be Invalid and fail closed.");

            RequireThrows(
                () => new LocalLicenseFileStore(new LocalLicenseFileStoreOptions(root, "../license.json")),
                "Path traversal file name must be rejected.");
            RequireThrows(
                () => new LocalLicenseFileStore(new LocalLicenseFileStoreOptions(root, Path.Combine(root, "license.json"))),
                "Absolute caller path must be rejected.");

            string endpointSource = ReadRepoFile("src", "Salryn.Api.Local", "LocalLicenseEndpoints.cs");
            RequireContains(endpointSource, "MapGet(\"/api/license/status\"", "License status endpoint must be mapped.");
            RequireContains(endpointSource, "RequireLoopbackOperatorRequest", "License endpoint must preserve loopback/local guard.");
            RequireContains(endpointSource, "RequireAuthenticatedSession", "License status endpoint must require authenticated operator session.");
            RequireNotContains(endpointSource, "LicensePath", "License endpoint must not accept or expose caller license path fields.");
            RequireNotContains(endpointSource, "OutputPath", "License endpoint must not accept filesystem output paths.");

            string implementationSource = string.Join("\n", new[]
            {
                ReadRepoFile("src", "Salryn.Infrastructure", "Licensing", "LocalLicenseFileStore.cs"),
                ReadRepoFile("src", "Salryn.Infrastructure", "Licensing", "LocalLicenseValidator.cs"),
                ReadRepoFile("src", "Salryn.Infrastructure", "Licensing", "LocalLicenseService.cs")
            });
            RequireNotContains(implementationSource, "HttpClient", "Licensing M1 must not add network dependency.");
            RequireNotContains(implementationSource, "Vercel", "Licensing M1 must not add Vercel licensing.");
            RequireNotContains(implementationSource, "Payment", "Licensing M1 must not add payment dependency.");
            RequireNotContains(implementationSource, "Subscription", "Licensing M1 must not add subscription sync.");
            RequireNotContains(implementationSource, "Registry", "Licensing M1 must not require registry dependency.");

            string productionReportEndpoint = ReadRepoFile("src", "Salryn.Api.Local", "LocalProductionReportEndpoints.cs");
            RequireNotContains(productionReportEndpoint, "LocalLicense", "Licensing M1 must not alter reporting endpoint behavior.");
            string projectionService = ReadRepoFile("src", "Salryn.Infrastructure", "Reports", "PreCrystalReportDatasetService.cs");
            RequireNotContains(projectionService, "LocalLicense", "Licensing M1 must not alter RMO/report projection semantics.");
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
        DateTimeOffset? expiresAt,
        string? signature,
        IReadOnlyList<string> features,
        string? machineBinding = TestMachine) => new(
            LicenseId: "LIC-LOCAL-M1-TEST",
            CustomerName: "Salryn Local Test",
            Edition: LicenseEdition.Local,
            IssuedAt: FixedNow.AddDays(-10),
            ExpiresAt: expiresAt,
            MachineBinding: machineBinding,
            Features: features,
            Signature: signature,
            SchemaVersion: LocalLicenseValidator.SupportedSchemaVersion,
            ApplicationId: LocalLicenseContractPolicy.RequiredApplicationId,
            StoreId: "store-m1-test",
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

    private static void RequireThrows(Action action, string message)
    {
        try
        {
            action();
        }
        catch
        {
            return;
        }

        throw new InvalidOperationException(message);
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

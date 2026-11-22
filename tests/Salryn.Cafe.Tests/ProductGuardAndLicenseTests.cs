using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Licensing;
using Salryn.Infrastructure.Paths;
using Xunit;

namespace Salryn.Cafe.Tests;

public sealed class ProductGuardAndLicenseTests
{
    [Fact]
    public void BackupGuard_AllowsMatchingCafeProductId()
    {
        BackupProductGuardResult result = SalrynProductGuard.ValidateBackupManifestForRestore("Salryn.Cafe");
        Assert.True(result.Allowed);
        Assert.Equal(BackupRestoreProductMode.StandardCafeRestore, result.Mode);
    }

    [Fact]
    public void BackupGuard_RejectsRetailBackupManifest()
    {
        BackupProductGuardResult result = SalrynProductGuard.ValidateBackupManifestForRestore(
            "Salryn.POS",
            isVerifiedYzaV39LegacyDb: () => true);

        Assert.False(result.Allowed);
        Assert.Equal(SalrynProductGuard.ErrorWrongProductBackup, result.ErrorCode);
    }

    [Fact]
    public void BackupGuard_AllowsLegacyYzaBackup_OnlyWhenClassifierConfirmsYzaV39Legacy()
    {
        BackupProductGuardResult allowed = SalrynProductGuard.ValidateBackupManifestForRestore(
            manifestProductId: null,
            isVerifiedYzaV39LegacyDb: () => true);

        Assert.True(allowed.Allowed);
        Assert.Equal(BackupRestoreProductMode.LegacyYzaAdoptionRestore, allowed.Mode);

        BackupProductGuardResult deniedNoClassifier = SalrynProductGuard.ValidateBackupManifestForRestore(
            manifestProductId: null,
            isVerifiedYzaV39LegacyDb: null);

        Assert.False(deniedNoClassifier.Allowed);
        Assert.Equal(SalrynProductGuard.ErrorBackupMissingProductId, deniedNoClassifier.ErrorCode);

        BackupProductGuardResult deniedFailedClassifier = SalrynProductGuard.ValidateBackupManifestForRestore(
            manifestProductId: "",
            isVerifiedYzaV39LegacyDb: () => false);

        Assert.False(deniedFailedClassifier.Allowed);
        Assert.Equal(SalrynProductGuard.ErrorBackupMissingProductId, deniedFailedClassifier.ErrorCode);
    }

    [Fact]
    public async Task LocalLicenseService_RejectsRetailApplicationIdOnInstallAndRead()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "salryn-cafe-lic-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var store = new LocalLicenseFileStore(new LocalLicenseFileStoreOptions(tempDir, "license.json"));
            var service = new LocalLicenseService(store: store);

            string retailLicenseJson = """
            {
              "schemaVersion": 2,
              "licenseId": "LIC-RETAIL-001",
              "customerName": "Retail Test Store",
              "edition": "Professional",
              "applicationId": "Salryn.POS",
              "storeId": "STORE-01",
              "licenseType": "Host",
              "machineBinding": "TEST-MACHINE",
              "features": ["Pos"],
              "signature": "dummy"
            }
            """;

            LocalLicenseInstallResponse installResult = await service.InstallAsync(
                new LocalLicenseInstallRequest(retailLicenseJson));

            Assert.Equal("Rejected", installResult.InstallStatus);
            Assert.Equal(SalrynProductGuard.ErrorWrongProductLicense, installResult.ErrorCode);
            Assert.False(File.Exists(store.LicenseFilePath));

            await File.WriteAllTextAsync(store.LicenseFilePath, retailLicenseJson);
            LocalLicenseValidationResult readStatus = await service.GetStatusAsync();
            Assert.Equal(LicenseStatus.Invalid, readStatus.Status);
            Assert.Contains("Salryn.Cafe", readStatus.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void DevelopmentLicenseMode_IsCompileGatedInReleaseBuilds()
    {
        string? prior = Environment.GetEnvironmentVariable("SALRYN_LICENSE_DEVELOPMENT_MODE");
        try
        {
            Environment.SetEnvironmentVariable("SALRYN_LICENSE_DEVELOPMENT_MODE", "1");
#if DEBUG
            Assert.True(LocalLicenseService.IsExplicitDevelopmentLicenseMode());
#else
            Assert.False(LocalLicenseService.IsExplicitDevelopmentLicenseMode());
#endif
        }
        finally
        {
            Environment.SetEnvironmentVariable("SALRYN_LICENSE_DEVELOPMENT_MODE", prior);
        }
    }

    [Fact]
    public void DefaultLicenseStorePath_UsesIsolatedSalrynCafeDirectory()
    {
        string licenseRoot = SalrynProductIdentity.GetLicenseRootDirectory();
        Assert.EndsWith(
            Path.Combine("SalrynCafe", "Licensing"),
            licenseRoot,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LocalLicenseService_ValidatesSignedSalrynCafeHostLicense_AndRejectsRetailWrongMachineExpiredAndUntrustedSigner()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "salryn-cafe-valid-lic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            using RSA ephemeralSigner = RSA.Create(2048);
            string ephemeralPubPem = ephemeralSigner.ExportSubjectPublicKeyInfoPem();
            string machineCode = "CAFE-MACHINE-TEST-001";
            DateTimeOffset now = DateTimeOffset.UtcNow;

            string SignDoc(LocalLicenseDocument unsignedDoc)
            {
                byte[] payload = Encoding.UTF8.GetBytes(LocalLicenseSignatureVerifier.BuildSigningPayload(unsignedDoc));
                byte[] sig = ephemeralSigner.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                var signed = unsignedDoc with { Signature = Convert.ToBase64String(sig) };
                return JsonSerializer.Serialize(signed, new JsonSerializerOptions
                {
                    Converters = { new JsonStringEnumConverter() }
                });
            }

            var store = new LocalLicenseFileStore(new LocalLicenseFileStoreOptions(tempDir, "license.json"));
            var validator = new LocalLicenseValidator(new LocalLicenseValidatorOptions
            {
                ExpectedApplicationId = SalrynProductIdentity.ProductId,
                MachineFingerprint = machineCode,
                SignatureVerifier = doc => LocalLicenseSignatureVerifier.VerifyWithPublicKeyPem(doc, ephemeralPubPem)
            });
            var service = new LocalLicenseService(store, validator);

            // 1. Valid signed Salryn.Cafe schema-2 Host license validates and installs cleanly
            var validCafeDoc = new LocalLicenseDocument(
                LicenseId: "CAFE-LIC-001",
                CustomerName: "Cafe Yza QA",
                Edition: LicenseEdition.Professional,
                IssuedAt: now.AddMinutes(-5),
                ExpiresAt: now.AddDays(30),
                MachineBinding: machineCode,
                Features: LicenseFeatures.AllList,
                Signature: null,
                SchemaVersion: LocalLicenseContractPolicy.BetaLicenseSchemaVersion,
                ApplicationId: SalrynProductIdentity.ProductId,
                StoreId: "STORE-CAFE-01",
                AllowedLanTerminals: 2,
                SupportExpiresAt: now.AddDays(30),
                UpdatesUntil: now.AddDays(30),
                LicenseType: LicenseType.Host,
                HostLicenseId: null,
                TerminalId: null);

            string validCafeJson = SignDoc(validCafeDoc);
            LocalLicenseInstallResponse installed = await service.InstallAsync(new LocalLicenseInstallRequest(validCafeJson));
            Assert.Equal("Installed", installed.InstallStatus);
            Assert.Equal(LicenseStatus.Licensed, installed.ValidationStatus);

            LocalLicenseValidationResult status = await service.GetStatusAsync();
            Assert.Equal(LicenseStatus.Licensed, status.Status);
            Assert.Equal(SalrynProductIdentity.ProductId, status.ApplicationId);
            Assert.Equal(LicenseType.Host, status.LicenseType);
            Assert.True(status.MachineMatched);
            Assert.Equal(machineCode, status.MachineCode);

            // 2. Default embedded-key verifier MUST reject ephemeral-key-signed Cafe license (no test key in prod!)
            var prodDefaultService = new LocalLicenseService(store: new LocalLicenseFileStore(
                new LocalLicenseFileStoreOptions(Path.Combine(tempDir, "prod"), "license.json")));
            LocalLicenseInstallResponse untrusted = await prodDefaultService.InstallAsync(new LocalLicenseInstallRequest(validCafeJson));
            Assert.Equal("Rejected", untrusted.InstallStatus);

            // 3. Signed Salryn.POS Retail license MUST be rejected and preserve MachineCode on read
            string retailSignedJson = SignDoc(validCafeDoc with { LicenseId = "RETAIL-001", ApplicationId = LocalLicenseContractPolicy.RequiredApplicationId });
            LocalLicenseInstallResponse retailReject = await service.InstallAsync(new LocalLicenseInstallRequest(retailSignedJson));
            Assert.Equal("Rejected", retailReject.InstallStatus);
            Assert.Equal(SalrynProductGuard.ErrorWrongProductLicense, retailReject.ErrorCode);

            var wrongProdStore = new LocalLicenseFileStore(new LocalLicenseFileStoreOptions(Path.Combine(tempDir, "wrong-prod"), "license.json"));
            Directory.CreateDirectory(wrongProdStore.RootDirectory);
            await File.WriteAllTextAsync(wrongProdStore.LicenseFilePath, retailSignedJson);
            var wrongProdService = new LocalLicenseService(wrongProdStore, validator);
            LocalLicenseValidationResult wrongProdStatus = await wrongProdService.GetStatusAsync();
            Assert.Equal(LicenseStatus.Invalid, wrongProdStatus.Status);
            Assert.Equal(machineCode, wrongProdStatus.MachineCode);

            // 4. Wrong machine, expired, and Terminal-on-Host must fail closed
            string wrongMachineJson = SignDoc(validCafeDoc with { LicenseId = "CAFE-WM-001", MachineBinding = "OTHER-MACHINE-999" });
            Assert.Equal("Rejected", (await service.InstallAsync(new LocalLicenseInstallRequest(wrongMachineJson))).InstallStatus);

            string expiredJson = SignDoc(validCafeDoc with { LicenseId = "CAFE-EXP-001", ExpiresAt = now.AddDays(-2) });
            LocalLicenseInstallResponse expiredResp = await service.InstallAsync(new LocalLicenseInstallRequest(expiredJson));
            Assert.Equal("Rejected", expiredResp.InstallStatus);
            Assert.Equal(LicenseStatus.Expired, expiredResp.ValidationStatus);

            var terminalDoc = validCafeDoc with
            {
                LicenseId = "CAFE-TERM-001",
                LicenseType = LicenseType.Terminal,
                AllowedLanTerminals = null,
                HostLicenseId = "CAFE-LIC-001",
                TerminalId = "TERM-01",
                Features = [LicenseFeatures.LanTerminals]
            };
            LocalLicenseValidationResult terminalVal = validator.Validate(new LocalLicenseFileReadResult(
                "terminal.json",
                Exists: true,
                Malformed: false,
                Document: JsonSerializer.Deserialize<LocalLicenseDocument>(
                    SignDoc(terminalDoc),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } }),
                ErrorMessage: null));
            Assert.Equal(LicenseStatus.Licensed, terminalVal.Status);
            Assert.Equal(LicenseType.Terminal, terminalVal.LicenseType);
            Assert.NotEqual(LicenseType.Host, terminalVal.LicenseType);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}

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
}

using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Paths;
using Salryn.Tests.SmokeProof;

namespace Salryn.Tests;

public static class HardenA1A6PreBetaFoundationChecks
{
    public static void Run()
    {
        VerifyA1ProofClassificationCatalog();
        VerifyA2LicensingSmokeNamesRemainClassified();
        VerifyA3SecuritySourceAuditBoundary();
        VerifyA4PermissionGuardExpectations();
        VerifyA5LocalLicensingContractPolicy();
        VerifyA6DataPathBackupContract();
    }

    private static void VerifyA1ProofClassificationCatalog()
    {
        SmokeProofClass[] requiredClasses =
        [
            SmokeProofClass.SourceInspection,
            SmokeProofClass.CompileBoundary,
            SmokeProofClass.SyntheticBehavior,
            SmokeProofClass.FileSystemBehavior,
            SmokeProofClass.RuntimeComposition,
            SmokeProofClass.ManualProofRequired
        ];

        foreach (SmokeProofClass proofClass in requiredClasses)
        {
            if (!Enum.IsDefined(proofClass))
            {
                throw new InvalidOperationException("HARDEN-A1 proof class missing: " + proofClass);
            }
        }

        Require(
            SmokeProofCatalog.All.Count >= 7,
            "HARDEN-A1 expected at least seven pre-beta proof catalog records.");
        Require(
            SmokeProofCatalog.All.Any(record => record.ProofClass == SmokeProofClass.ManualProofRequired),
            "HARDEN-A1 expected at least one manual-proof-required record.");
        Require(
            SmokeProofCatalog.All.All(record => !string.IsNullOrWhiteSpace(record.WhatItDoesNotProve)),
            "HARDEN-A1 every proof record must state what it does not prove.");
    }

    private static void VerifyA2LicensingSmokeNamesRemainClassified()
    {
        string[] requiredChecks =
        [
            "licensing-m1-local-license-foundation",
            "licensing-m2-signed-license-install-import",
            "licensing-m3-local-enforcement-boundaries"
        ];

        foreach (string check in requiredChecks)
        {
            SmokeProofRecord? record = SmokeProofCatalog.Find(check);
            Require(record is not null, "HARDEN-A2 licensing smoke proof record missing: " + check);
            Require(
                record!.ProofClass is SmokeProofClass.SyntheticBehavior or SmokeProofClass.FileSystemBehavior,
                "HARDEN-A2 licensing proof class must not overclaim runtime/manual proof: " + check);
            Require(
                record.NextProofNeeded.Contains("Run", StringComparison.OrdinalIgnoreCase),
                "HARDEN-A2 licensing proof record must name a next proof action: " + check);
        }
    }

    private static void VerifyA3SecuritySourceAuditBoundary()
    {
        string m3Implementation = ReadRepoFile("src/Salryn.Infrastructure/Licensing/LocalLicenseEnforcementService.cs");
        string m3Api = ReadRepoFile("src/Salryn.Api.Local/LocalLicenseEndpoints.cs");
        string licenseGuard = ReadRepoFile("src/Salryn.Api.Local/Auth/ApiLicenseGuard.cs");
        string artifactRoot = ReadRepoFile("src/Salryn.Infrastructure/Reports/NativePdf/NativePdfArtifactRootPolicy.cs");

        string combinedLicensingSlice = m3Implementation + Environment.NewLine + m3Api + Environment.NewLine + licenseGuard;
        string[] forbiddenLicensingRuntimeTerms =
        [
            "HttpClient",
            "Vercel",
            "OnlineActivation",
            "PrivateSigningKey",
            "PrivateKey",
            "Stripe",
            "PayPal"
        ];

        foreach (string forbidden in forbiddenLicensingRuntimeTerms)
        {
            Require(
                !combinedLicensingSlice.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                "HARDEN-A3 forbidden licensing runtime dependency found: " + forbidden);
        }

        Require(
            artifactRoot.Contains("ResolveControlledPath", StringComparison.Ordinal),
            "HARDEN-A3 native PDF artifact root must expose controlled path resolution.");
        Require(
            artifactRoot.Contains("RejectCallerPath", StringComparison.Ordinal),
            "HARDEN-A3 report artifact root must reject caller-controlled output paths.");
        Require(
            artifactRoot.Contains("path traversal is forbidden", StringComparison.OrdinalIgnoreCase),
            "HARDEN-A3 report artifact path traversal guard text missing.");
    }

    private static void VerifyA4PermissionGuardExpectations()
    {
        string licenseEndpoints = ReadRepoFile("src/Salryn.Api.Local/LocalLicenseEndpoints.cs");
        string permissionGuards = ReadRepoFile("src/Salryn.Api.Local/Auth/LocalApiPermissionGuards.cs");
        string apiLicenseGuard = ReadRepoFile("src/Salryn.Api.Local/Auth/ApiLicenseGuard.cs");

        RequireEndpointGuard(licenseEndpoints, "MapGet(\"/api/license/status\"", "RequireLoopbackOperatorRequest");
        RequireEndpointGuard(licenseEndpoints, "MapGet(\"/api/license/status\"", "RequireAuthenticatedSession");
        RequireEndpointGuard(licenseEndpoints, "MapGet(\"/api/license/entitlements\"", "RequireLoopbackOperatorRequest");
        RequireEndpointGuard(licenseEndpoints, "MapGet(\"/api/license/entitlements\"", "RequireAuthenticatedSession");
        RequireEndpointGuard(licenseEndpoints, "MapPost(\"/api/license/install\"", "RequireLoopbackOperatorRequest");
        RequireEndpointGuard(licenseEndpoints, "MapPost(\"/api/license/install\"", "RequirePermission");
        RequireEndpointGuard(licenseEndpoints, "MapPost(\"/api/license/install\"", "LocalApiPermissions.SettingsManage");

        Require(
            permissionGuards.Contains("public const string SettingsManage = \"Settings.Manage\";", StringComparison.Ordinal),
            "HARDEN-A4 Settings.Manage permission constant missing.");
        Require(
            permissionGuards.Contains("StatusCodes.Status401Unauthorized", StringComparison.Ordinal),
            "HARDEN-A4 authentication-required 401 path missing.");
        Require(
            permissionGuards.Contains("StatusCodes.Status403Forbidden", StringComparison.Ordinal),
            "HARDEN-A4 permission-denied 403 path missing.");
        Require(
            apiLicenseGuard.Contains("License.FeatureDenied", StringComparison.Ordinal),
            "HARDEN-A4 deterministic license denial code missing.");
        Require(
            apiLicenseGuard.Contains("StatusCodes.Status403Forbidden", StringComparison.Ordinal),
            "HARDEN-A4 license guard must deny with 403.");
    }

    private static void VerifyA5LocalLicensingContractPolicy()
    {
        Require(LocalLicenseContractPolicy.RuntimeMustWorkOffline, "HARDEN-A5 local runtime must remain offline-capable.");
        Require(!LocalLicenseContractPolicy.LocalAppRequiresNetworkForVerification, "HARDEN-A5 local verification must not require network.");
        Require(!LocalLicenseContractPolicy.PrivateSigningKeyAllowedInRepository, "HARDEN-A5 private signing keys must not be allowed in repo.");
        Require(!LocalLicenseContractPolicy.LocalRuntimeMayDependOnRecurringBillingState, "HARDEN-A5 local runtime must not depend on recurring billing state.");
        Require(LocalLicenseContractPolicy.ExternalIssuerMayCreateSignedLicenseFiles, "HARDEN-A5 external signed-license issuance must remain possible later.");

        string contracts = ReadRepoFile("src/Salryn.Core/Services/Licensing/LicenseContracts.cs");
        Require(contracts.Contains("LicenseEnforcementDecision", StringComparison.Ordinal), "HARDEN-A5 M3 decision contract missing.");
        Require(contracts.Contains("LicenseEnforcementReason", StringComparison.Ordinal), "HARDEN-A5 M3 reason contract missing.");
        Require(contracts.Contains("LocalLicenseEntitlementsResponse", StringComparison.Ordinal), "HARDEN-A5 entitlement response contract missing.");
    }

    private static void VerifyA6DataPathBackupContract()
    {
        Require(SalrynLocalDataContract.IsKnownEntry("database"), "HARDEN-A6 database path contract missing.");
        Require(SalrynLocalDataContract.IsKnownEntry("license"), "HARDEN-A6 license path contract missing.");
        Require(SalrynLocalDataContract.IsKnownEntry("license-backups"), "HARDEN-A6 license backup path contract missing.");
        Require(SalrynLocalDataContract.IsKnownEntry("native-pdf-reports"), "HARDEN-A6 report artifact path contract missing.");
        Require(
            SalrynLocalDataContract.RequiredEntries.Any(entry => entry.MustPreserveAcrossUpdate),
            "HARDEN-A6 expected preserve-across-update entries.");

        string dbPaths = ReadRepoFile("src/Salryn.Infrastructure/Paths/DbPaths.cs");
        string licenseStore = ReadRepoFile("src/Salryn.Infrastructure/Licensing/LocalLicenseFileStore.cs");

        Require(dbPaths.Contains("CommonApplicationData", StringComparison.Ordinal), "HARDEN-A6 production root should remain ProgramData-backed.");
        Require(dbPaths.Contains("EnsureDirectories", StringComparison.Ordinal), "HARDEN-A6 DbPaths directory creation contract missing.");
        Require(licenseStore.Contains("license.json", StringComparison.Ordinal), "HARDEN-A6 local license file name contract missing.");
        Require(licenseStore.Contains("backup", StringComparison.OrdinalIgnoreCase), "HARDEN-A6 local license backup-before-replace seam missing.");
    }

    private static void RequireEndpointGuard(string source, string endpointMarker, string guardMarker)
    {
        int endpointIndex = source.IndexOf(endpointMarker, StringComparison.Ordinal);
        Require(endpointIndex >= 0, "Endpoint marker missing: " + endpointMarker);

        int guardIndex = source.IndexOf(guardMarker, endpointIndex, StringComparison.Ordinal);
        Require(guardIndex >= 0, "Expected guard marker after endpoint " + endpointMarker + ": " + guardMarker);
    }

    private static string ReadRepoFile(string relativePath)
    {
        string? root = FindRepoRoot();
        if (root is null)
        {
            throw new InvalidOperationException("Unable to locate repository root from " + Directory.GetCurrentDirectory());
        }

        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Required HARDEN-A1-A6 source file was not found.", path);
        }

        return File.ReadAllText(path);
    }

    private static string? FindRepoRoot()
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

        return null;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

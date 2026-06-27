using Salryn.Core.Services;

namespace Salryn.App.WinForms.Composition;

public sealed record AppOfficialOutputTextFileWorkflowReadinessRequest(
    OfficialOutputExportPackage? ExportPackage,
    string? OutputRootPath);

public sealed record AppOfficialOutputTextFileWorkflowReadinessIssue(
    string Code,
    string Message);

public sealed record AppOfficialOutputTextFileWorkflowReadinessState(
    bool DirectGatewayAvailable,
    bool LocalHostGatewayAvailable,
    bool LocalHostRuntimeHolderAvailable,
    bool LocalHostRuntimeStartupSucceeded,
    bool RequestContextProviderAvailable,
    bool SessionIdentityProviderAvailable,
    bool AuthenticatedAppIdentityAvailable,
    bool RequestContextCanBeCreated,
    bool OutputRootSupplied,
    bool TrustedExportPackageSupplied,
    bool TextFileOnly);

public sealed record AppOfficialOutputTextFileWorkflowReadinessResult(
    bool IsAvailable,
    string StatusCode,
    string StatusMessage,
    AppOfficialOutputTextFileWorkflowReadinessState State,
    IReadOnlyList<AppOfficialOutputTextFileWorkflowReadinessIssue> Issues)
{
    public static AppOfficialOutputTextFileWorkflowReadinessResult Available(
        AppOfficialOutputTextFileWorkflowReadinessState state)
    {
        return new AppOfficialOutputTextFileWorkflowReadinessResult(
            IsAvailable: true,
            StatusCode: "AppOfficialOutputTextFileWorkflowReadiness.Available",
            StatusMessage: "Official output text-file workflow readiness checks passed.",
            State: state,
            Issues: Array.Empty<AppOfficialOutputTextFileWorkflowReadinessIssue>());
    }

    public static AppOfficialOutputTextFileWorkflowReadinessResult Unavailable(
        AppOfficialOutputTextFileWorkflowReadinessState state,
        IReadOnlyList<AppOfficialOutputTextFileWorkflowReadinessIssue> issues)
    {
        return new AppOfficialOutputTextFileWorkflowReadinessResult(
            IsAvailable: false,
            StatusCode: "AppOfficialOutputTextFileWorkflowReadiness.Unavailable",
            StatusMessage: "Official output text-file workflow is not ready.",
            State: state,
            Issues: issues);
    }
}

public interface IAppOfficialOutputTextFileWorkflowReadinessGateway
{
    AppOfficialOutputTextFileWorkflowReadinessResult CheckReadiness(
        AppOfficialOutputTextFileWorkflowReadinessRequest? request);
}

public static class AppOfficialOutputTextFileWorkflowReadinessRules
{
    private const string PackageReferencePrefix = "salryn://official-output-export-package/";

    public static bool HasExplicitOutputRoot(string? outputRootPath)
    {
        return !string.IsNullOrWhiteSpace(outputRootPath);
    }

    public static bool IsTrustedExportPackage(
        OfficialOutputExportPackage? package)
    {
        if (package is null ||
            package.PackageKind != OfficialOutputExportPackageKind.PreviewTextBundle ||
            string.IsNullOrWhiteSpace(package.PackageHash) ||
            string.IsNullOrWhiteSpace(package.PackageReference))
        {
            return false;
        }

        string packageHash = package.PackageHash.Trim();
        string packageReference = package.PackageReference.Trim();

        return packageReference.StartsWith(PackageReferencePrefix, StringComparison.Ordinal) &&
            string.Equals(packageReference, PackageReferencePrefix + packageHash, StringComparison.Ordinal);
    }
}

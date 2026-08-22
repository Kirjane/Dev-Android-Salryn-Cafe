using Salryn.Core.Common;

namespace Salryn.Infrastructure.Reports.NativePdf;

public sealed record NativePdfArtifactRootPolicyOptions(
    string? RootDirectory,
    string? PolicyLabel);

public sealed class NativePdfArtifactRootPolicy
{
    public const string DefaultPolicyLabel = "Salryn.LocalApplicationData.Reporting.NativePdf";
    public const string DefaultRetentionCleanupNote =
        "Local controlled report artifacts are retained until an operator/admin cleanup workflow is implemented. Do not treat this as archival storage.";

    private NativePdfArtifactRootPolicy(string rootDirectory, string policyLabel)
    {
        RootDirectory = EnsureRoot(rootDirectory);
        PolicyLabel = string.IsNullOrWhiteSpace(policyLabel) ? DefaultPolicyLabel : policyLabel.Trim();
    }

    public string RootDirectory { get; }

    public string PolicyLabel { get; }

    public static NativePdfArtifactRootPolicy CreateDefault() =>
        new(GetDefaultRootDirectory(), DefaultPolicyLabel);

    public static NativePdfArtifactRootPolicy CreateForTesting(string rootDirectory) =>
        new(rootDirectory, "Salryn.TestControlledRoot.Reporting.NativePdf");

    public static NativePdfArtifactRootPolicy FromOptions(NativePdfArtifactRootPolicyOptions? options)
    {
        if (options is null || string.IsNullOrWhiteSpace(options.RootDirectory))
        {
            return CreateDefault();
        }

        return new NativePdfArtifactRootPolicy(options.RootDirectory, options.PolicyLabel ?? DefaultPolicyLabel);
    }

    public string CreateRelativeArtifactPath(
        string reportKind,
        DateTimeOffset createdAt,
        string artifactId)
    {
        string safeKind = NativePdfArtifactFactory.SanitizeFileToken(reportKind);
        string safeArtifactId = NativePdfArtifactFactory.SanitizeFileToken(artifactId);
        if (string.IsNullOrWhiteSpace(safeArtifactId))
        {
            safeArtifactId = Guid.NewGuid().ToString("N");
        }

        string day = createdAt.ToUniversalTime().ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        string fileName = $"{safeKind}-{createdAt.ToUniversalTime():yyyyMMddHHmmss}-{safeArtifactId}.pdf";
        return Path.Combine(day, safeKind, fileName);
    }

    public string CreateRelativeManifestPath(string relativeArtifactPath)
    {
        EnsureSafeRelativePath(relativeArtifactPath);
        return relativeArtifactPath + ".manifest.json";
    }

    public string ResolveControlledPath(string relativePath)
    {
        EnsureSafeRelativePath(relativePath);
        string combined = Path.GetFullPath(Path.Combine(RootDirectory, relativePath));
        string rootWithSeparator = RootDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? RootDirectory
            : RootDirectory + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!combined.StartsWith(rootWithSeparator, comparison) &&
            !string.Equals(combined, RootDirectory, comparison))
        {
            throw new InvalidOperationException("Resolved report artifact path escaped the controlled root.");
        }

        return combined;
    }

    public Result RejectCallerPath(string? callerPath)
    {
        if (string.IsNullOrWhiteSpace(callerPath))
        {
            return Result.Success();
        }

        return Result.Fail(Error.Validation(
            "Reports.CallerOutputPathForbidden",
            "Report export does not accept caller-supplied output roots or filesystem paths."));
    }

    public static void EnsureSafeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidOperationException("Relative report artifact path is required.");
        }

        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException("Absolute report artifact paths are forbidden.");
        }

        string normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        string[] segments = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            throw new InvalidOperationException("Relative report artifact path is empty after normalization.");
        }

        foreach (string segment in segments)
        {
            if (segment == "." || segment == ".." || segment.Contains("..", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Report artifact path traversal is forbidden.");
            }

            if (segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new InvalidOperationException("Report artifact path contains invalid filename characters.");
            }
        }
    }

    private static string GetDefaultRootDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = AppContext.BaseDirectory;
        }

        return Path.Combine(localAppData, "Salryn", "Reports", "NativePdf");
    }

    private static string EnsureRoot(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new InvalidOperationException("Controlled report artifact root is required.");
        }

        return Path.GetFullPath(rootDirectory.Trim());
    }
}

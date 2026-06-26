using Salryn.Core.Common;

namespace Salryn.Infrastructure.OfficialOutput;

public sealed class OfficialTextFileOutputPathPolicy
{
    public Result<string> ResolveOutputRoot(string? outputRootPath)
    {
        if (string.IsNullOrWhiteSpace(outputRootPath))
        {
            return Result<string>.Fail(Error.Validation(
                "OfficialTextFileOutput.OutputRootRequired",
                "Official text-file output requires an output root path."));
        }

        string root = Path.GetFullPath(outputRootPath.Trim());
        return Result<string>.Success(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    public Result<string> ResolveOfficialOutputDirectory(
        string outputRootPath,
        string packageHash)
    {
        Result<string> rootResult = ResolveOutputRoot(outputRootPath);

        if (rootResult.IsFailure)
        {
            return rootResult;
        }

        if (string.IsNullOrWhiteSpace(packageHash) || packageHash.Contains("..", StringComparison.Ordinal))
        {
            return Result<string>.Fail(Error.Validation(
                "OfficialTextFileOutput.PackageHashInvalid",
                "Official text-file output package hash is invalid."));
        }

        string child = Path.Combine("official-output", packageHash.Trim());
        return ResolveChild(rootResult.Value, child);
    }

    public Result<string> ResolveChild(
        string outputRootPath,
        string relativePath)
    {
        Result<string> rootResult = ResolveOutputRoot(outputRootPath);

        if (rootResult.IsFailure)
        {
            return rootResult;
        }

        if (string.IsNullOrWhiteSpace(relativePath) ||
            Path.IsPathRooted(relativePath) ||
            relativePath.Contains("..", StringComparison.Ordinal))
        {
            return Result<string>.Fail(Error.Validation(
                "OfficialTextFileOutput.RelativePathInvalid",
                "Official text-file output relative path is invalid."));
        }

        string fullPath = Path.GetFullPath(Path.Combine(rootResult.Value, relativePath));

        if (!IsInsideRoot(rootResult.Value, fullPath))
        {
            return Result<string>.Fail(Error.Validation(
                "OfficialTextFileOutput.PathTraversalRejected",
                "Official text-file output path traversal was rejected."));
        }

        return Result<string>.Success(fullPath);
    }

    public string BuildRelativePath(
        string packageHash,
        string fileName)
    {
        return ("official-output/" + SanitizeSegment(packageHash) + "/" + SanitizeSegment(fileName))
            .Replace('\\', '/');
    }

    public string BuildPayloadFileName(
        int index,
        string family,
        string contentHash)
    {
        string prefix = string.IsNullOrWhiteSpace(contentHash)
            ? "unknown"
            : contentHash.Trim()[..Math.Min(12, contentHash.Trim().Length)];

        return "payload-" + index.ToString("000") + "-" + SanitizeSegment(family).ToLowerInvariant() + "-" + prefix + ".txt";
    }

    private static bool IsInsideRoot(string rootPath, string fullPath)
    {
        string root = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string candidate = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate, rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeSegment(string value)
    {
        string cleaned = string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
        char[] invalid = Path.GetInvalidFileNameChars();

        foreach (char character in invalid)
        {
            cleaned = cleaned.Replace(character, '-');
        }

        cleaned = cleaned.Replace('/', '-').Replace('\\', '-').Replace("..", "-", StringComparison.Ordinal);
        return cleaned.Length == 0 ? "unknown" : cleaned;
    }
}

using System.Security;
using System.Text;
using Salryn.Core.Common;

namespace Salryn.Infrastructure.OfficialOutput;

public sealed class OfficialTextFileOutputPathPolicy
{
    private const string ProbeFilePrefix = ".salryn-write-probe-";

    public Result<string> ResolveOutputRoot(string? outputRootPath)
    {
        if (string.IsNullOrWhiteSpace(outputRootPath))
        {
            return Result<string>.Fail(Error.Validation(
                "OfficialTextFileOutput.OutputRootRequired",
                "Official text-file output requires an output root path."));
        }

        string trimmedInput = outputRootPath.Trim();

        if (HasInvalidPathCharacters(trimmedInput))
        {
            return Result<string>.Fail(Error.Validation(
                "OfficialTextFileOutput.OutputRootInvalid",
                "Official text-file output output root path is invalid."));
        }

        string fullPath;

        try
        {
            fullPath = Path.GetFullPath(trimmedInput);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Result<string>.Fail(Error.Validation(
                "OfficialTextFileOutput.OutputRootInvalid",
                "Official text-file output output root path is invalid."));
        }

        string normalizedRoot = NormalizeDirectoryPath(fullPath);

        if (IsDriveRoot(normalizedRoot))
        {
            return Result<string>.Fail(Error.Validation(
                "OfficialTextFileOutput.OutputRootDriveRootRejected",
                "Output root cannot be a drive root."));
        }

        if (File.Exists(normalizedRoot) && !Directory.Exists(normalizedRoot))
        {
            return Result<string>.Fail(Error.Validation(
                "OfficialTextFileOutput.OutputRootMustBeDirectory",
                "Output root must be an existing writable directory."));
        }

        if (!Directory.Exists(normalizedRoot))
        {
            return Result<string>.Fail(Error.Validation(
                "OfficialTextFileOutput.OutputRootMustExist",
                "Output root must be an existing writable directory."));
        }

        if (IsForbiddenRoot(normalizedRoot))
        {
            return Result<string>.Fail(Error.Validation(
                "OfficialTextFileOutput.OutputRootSystemDirectoryRejected",
                "Output root cannot be a system or application directory."));
        }

        return Result<string>.Success(normalizedRoot);
    }

    public Result<string> ValidateWritableOutputRoot(string? outputRootPath)
    {
        Result<string> rootResult = ResolveOutputRoot(outputRootPath);

        if (rootResult.IsFailure)
        {
            return rootResult;
        }

        return ProbeWritability(rootResult.Value);
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

        string fullPath;

        try
        {
            fullPath = Path.GetFullPath(Path.Combine(rootResult.Value, relativePath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Result<string>.Fail(Error.Validation(
                "OfficialTextFileOutput.RelativePathInvalid",
                "Official text-file output relative path is invalid."));
        }

        if (!IsInsideRoot(rootResult.Value, fullPath))
        {
            return Result<string>.Fail(Error.Validation(
                "OfficialTextFileOutput.PathTraversalRejected",
                "Output path is outside the selected output root."));
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

    public static Result MapWriteException(Exception exception)
    {
        return exception switch
        {
            UnauthorizedAccessException =>
                Result.Fail(Error.Validation(
                    "OfficialTextFileOutput.OutputRootNotWritable",
                    "Output root is not writable.")),
            DirectoryNotFoundException =>
                Result.Fail(Error.Validation(
                    "OfficialTextFileOutput.OutputRootMustExist",
                    "Output root must be an existing writable directory.")),
            PathTooLongException =>
                Result.Fail(Error.Validation(
                    "OfficialTextFileOutput.OutputRootInvalid",
                    "Official text-file output output root path is invalid.")),
            IOException =>
                Result.Fail(Error.Validation(
                    "OfficialTextFileOutput.OutputRootNotWritable",
                    "Output root is not writable.")),
            SecurityException =>
                Result.Fail(Error.Validation(
                    "OfficialTextFileOutput.OutputRootNotWritable",
                    "Output root is not writable.")),
            ArgumentException =>
                Result.Fail(Error.Validation(
                    "OfficialTextFileOutput.OutputRootInvalid",
                    "Official text-file output output root path is invalid.")),
            NotSupportedException =>
                Result.Fail(Error.Validation(
                    "OfficialTextFileOutput.OutputRootInvalid",
                    "Official text-file output output root path is invalid.")),
            _ => Result.Fail(Error.Validation(
                "OfficialTextFileOutput.WriteFailed",
                "Official text-file output write failed."))
        };
    }

    private Result<string> ProbeWritability(string rootPath)
    {
        string probePath = Path.Combine(rootPath, ProbeFilePrefix + Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            File.WriteAllText(probePath, "probe", Encoding.UTF8);
            File.Delete(probePath);

            if (File.Exists(probePath))
            {
                TryDeleteQuiet(probePath);
                return Result<string>.Fail(Error.Validation(
                    "OfficialTextFileOutput.OutputRootNotWritable",
                    "Output root is not writable."));
            }

            return Result<string>.Success(rootPath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException or PathTooLongException or SecurityException)
        {
            TryDeleteQuiet(probePath);
            Result mapped = MapWriteException(ex);

            return Result<string>.Fail(mapped.ErrorDetail);
        }
    }

    private static bool IsDriveRoot(string fullPath)
    {
        string? pathRoot = Path.GetPathRoot(fullPath);

        if (string.IsNullOrWhiteSpace(pathRoot))
        {
            return false;
        }

        string trimmedPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string trimmedRoot = pathRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(trimmedPath, trimmedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsForbiddenRoot(string fullPath)
    {
        foreach (string forbiddenRoot in EnumerateForbiddenRoots())
        {
            if (IsInsideRoot(forbiddenRoot, fullPath) ||
                string.Equals(
                    NormalizeDirectoryPath(fullPath),
                    NormalizeDirectoryPath(forbiddenRoot),
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateForbiddenRoots()
    {
        foreach (Environment.SpecialFolder folder in new[]
        {
            Environment.SpecialFolder.Windows,
            Environment.SpecialFolder.System,
            Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86,
        })
        {
            string? path = TryGetFullPath(Environment.GetFolderPath(folder));

            if (path is not null)
            {
                yield return path;
            }
        }

        string? applicationRoot = TryGetFullPath(AppContext.BaseDirectory);

        if (applicationRoot is not null)
        {
            yield return applicationRoot;
        }
    }

    private static string? TryGetFullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return NormalizeDirectoryPath(Path.GetFullPath(path.Trim()));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool HasInvalidPathCharacters(string path)
    {
        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return true;
        }

        return path.Contains('\0', StringComparison.Ordinal);
    }

    private static string NormalizeDirectoryPath(string fullPath)
    {
        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsInsideRoot(string rootPath, string fullPath)
    {
        string root = NormalizeDirectoryPath(rootPath) + Path.DirectorySeparatorChar;
        string candidate = NormalizeDirectoryPath(fullPath);

        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate, NormalizeDirectoryPath(rootPath), StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteQuiet(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
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

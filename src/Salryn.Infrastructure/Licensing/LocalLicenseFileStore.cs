using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Salryn.Core.Services.Licensing;

namespace Salryn.Infrastructure.Licensing;

public sealed record LocalLicenseFileStoreOptions(
    string? RootDirectory,
    string? FileName);

public sealed record LocalLicenseFileReadResult(
    string SourcePath,
    bool Exists,
    bool Malformed,
    LocalLicenseDocument? Document,
    string? ErrorMessage);

public sealed record LocalLicenseFileReplaceResult(
    string SourcePath,
    string? BackupPath);

public sealed class LocalLicenseFileStore
{
    public const string DefaultFileName = "license.json";
    public const string PolicyLabel = "Salryn.LocalApplicationData.Licensing";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _rootDirectory;
    private readonly string _fileName;

    public LocalLicenseFileStore(LocalLicenseFileStoreOptions? options = null)
    {
        _rootDirectory = EnsureRoot(string.IsNullOrWhiteSpace(options?.RootDirectory)
            ? GetDefaultRootDirectory()
            : options.RootDirectory!);
        _fileName = EnsureSafeFileName(string.IsNullOrWhiteSpace(options?.FileName)
            ? DefaultFileName
            : options.FileName!);
    }

    public string RootDirectory => _rootDirectory;

    public string LicenseFilePath => Path.Combine(_rootDirectory, _fileName);

    public async Task<LocalLicenseFileReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = LicenseFilePath;
        if (!File.Exists(path))
        {
            return new LocalLicenseFileReadResult(path, Exists: false, Malformed: false, Document: null, ErrorMessage: null);
        }

        try
        {
            string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            LocalLicenseDocument? document = JsonSerializer.Deserialize<LocalLicenseDocument>(json, JsonOptions);
            if (document is null)
            {
                return new LocalLicenseFileReadResult(path, Exists: true, Malformed: true, Document: null, ErrorMessage: "License JSON did not contain a document.");
            }

            return new LocalLicenseFileReadResult(path, Exists: true, Malformed: false, Document: document, ErrorMessage: null);
        }
        catch (JsonException ex)
        {
            return new LocalLicenseFileReadResult(path, Exists: true, Malformed: true, Document: null, ErrorMessage: ex.Message);
        }
        catch (NotSupportedException ex)
        {
            return new LocalLicenseFileReadResult(path, Exists: true, Malformed: true, Document: null, ErrorMessage: ex.Message);
        }
    }

    public async Task<LocalLicenseFileReplaceResult> ReplaceAsync(
        string canonicalLicenseJson,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(canonicalLicenseJson))
        {
            throw new InvalidOperationException("Canonical local license JSON is required before replacement.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_rootDirectory);

        string tempFileName = EnsureSafeFileName("license-" + Guid.NewGuid().ToString("N") + ".tmp");
        string tempPath = Path.Combine(_rootDirectory, tempFileName);
        string targetPath = LicenseFilePath;
        string? backupPath = null;

        try
        {
            await File.WriteAllTextAsync(tempPath, canonicalLicenseJson, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(targetPath))
            {
                backupPath = CreateSystemBackupPath();
                ReplaceExistingLicense(tempPath, targetPath, backupPath);
            }
            else
            {
                File.Move(tempPath, targetPath);
            }

            return new LocalLicenseFileReplaceResult(targetPath, backupPath);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public static string EnsureSafeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException("Local license file name is required.");
        }

        string trimmed = fileName.Trim();
        if (Path.IsPathRooted(trimmed) || trimmed.Contains("/", StringComparison.Ordinal) || trimmed.Contains("\\", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Local license store does not accept caller-supplied paths.");
        }

        if (trimmed == "." || trimmed == ".." || trimmed.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Local license path traversal is forbidden.");
        }

        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException("Local license file name contains invalid characters.");
        }

        return trimmed;
    }

    private string CreateSystemBackupPath()
    {
        string backupFileName = EnsureSafeFileName(
            "license-" +
            DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture) +
            "-" +
            Guid.NewGuid().ToString("N") +
            ".bak.json");

        return Path.Combine(_rootDirectory, backupFileName);
    }

    private static void ReplaceExistingLicense(
        string tempPath,
        string targetPath,
        string backupPath)
    {
        try
        {
            File.Replace(tempPath, targetPath, backupPath, ignoreMetadataErrors: true);
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            if (!File.Exists(backupPath))
            {
                File.Copy(targetPath, backupPath, overwrite: false);
            }

            File.Move(tempPath, targetPath, overwrite: true);
        }
    }

    private static string GetDefaultRootDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = AppContext.BaseDirectory;
        }

        return Path.Combine(localAppData, "Salryn", "Licensing");
    }

    private static string EnsureRoot(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new InvalidOperationException("Controlled local license root is required.");
        }

        return Path.GetFullPath(rootDirectory.Trim());
    }
}

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

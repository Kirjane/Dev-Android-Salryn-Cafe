using System.Text.Json;
using System.Text.Json.Serialization;
using Salryn.Core.Services.Licensing;

namespace Salryn.Infrastructure.Licensing;

public sealed class LocalLicenseService : ILocalLicenseService
{
    private const string Installed = "Installed";
    private const string Rejected = "Rejected";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly HashSet<string> AllowedDocumentFields = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(LocalLicenseDocument.LicenseId),
        nameof(LocalLicenseDocument.CustomerName),
        nameof(LocalLicenseDocument.Edition),
        nameof(LocalLicenseDocument.IssuedAt),
        nameof(LocalLicenseDocument.ExpiresAt),
        nameof(LocalLicenseDocument.MachineBinding),
        nameof(LocalLicenseDocument.Features),
        nameof(LocalLicenseDocument.Signature),
        nameof(LocalLicenseDocument.SchemaVersion),
        nameof(LocalLicenseDocument.ApplicationId),
        nameof(LocalLicenseDocument.StoreId),
        nameof(LocalLicenseDocument.AllowedLanTerminals),
        nameof(LocalLicenseDocument.SupportExpiresAt),
        nameof(LocalLicenseDocument.UpdatesUntil)
    };

    private readonly LocalLicenseFileStore _store;
    private readonly LocalLicenseValidator _validator;
    private readonly Func<DateTimeOffset> _clock;

    public LocalLicenseService(
        LocalLicenseFileStore? store = null,
        LocalLicenseValidator? validator = null,
        Func<DateTimeOffset>? clock = null)
    {
        _store = store ?? new LocalLicenseFileStore();
        _validator = validator ?? CreateDefaultValidator();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<LocalLicenseValidationResult> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        LocalLicenseFileReadResult read = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
        return _validator.Validate(read);
    }

    public async Task<LocalLicenseInstallResponse> InstallAsync(
        LocalLicenseInstallRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        LocalLicenseInstallDraft draft = BuildDraft(request.LicenseJson);
        if (draft.ErrorCode is not null)
        {
            return Reject(
                LicenseStatus.Invalid,
                _store.LicenseFilePath,
                draft.ErrorCode,
                draft.ErrorMessage ?? "Local license install request is invalid.");
        }

        LocalLicenseFileReadResult candidate = new(
            _store.LicenseFilePath,
            Exists: true,
            Malformed: false,
            Document: draft.Document,
            ErrorMessage: null);

        LocalLicenseValidationResult validation = _validator.Validate(candidate);
        if (validation.Status != LicenseStatus.Licensed)
        {
            return Reject(
                validation.Status,
                validation.SourcePath,
                "License.Install.ValidationRejected",
                validation.Message);
        }

        LocalLicenseFileReplaceResult replacement = await _store
            .ReplaceAsync(draft.CanonicalJson!, cancellationToken)
            .ConfigureAwait(false);

        LocalLicenseValidationResult afterInstall = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (afterInstall.Status != LicenseStatus.Licensed)
        {
            return Reject(
                afterInstall.Status,
                afterInstall.SourcePath,
                "License.Install.PostWriteValidationFailed",
                afterInstall.Message,
                replacement.BackupPath);
        }

        return new LocalLicenseInstallResponse(
            Installed,
            afterInstall.Status,
            afterInstall.SourcePath,
            replacement.BackupPath,
            null,
            null,
            _clock());
    }

    public async Task<bool> HasFeatureAsync(string feature, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(feature))
        {
            return false;
        }

        LocalLicenseValidationResult status = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return _validator.IsFeatureEnabled(status, feature.Trim());
    }

    private static LocalLicenseValidator CreateDefaultValidator() => new(new LocalLicenseValidatorOptions
    {
        MachineFingerprint = LocalMachineFingerprint.Current(),
        SignatureVerifier = LocalLicenseSignatureVerifier.Verify
    });

    private static LocalLicenseInstallDraft BuildDraft(string? licenseJson)
    {
        if (string.IsNullOrWhiteSpace(licenseJson))
        {
            return LocalLicenseInstallDraft.Failed(
                "License.Install.EmptyDocument",
                "License document content is required.");
        }

        try
        {
            using JsonDocument parsed = JsonDocument.Parse(licenseJson);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                return LocalLicenseInstallDraft.Failed(
                    "License.Install.MalformedJson",
                    "License document JSON must be an object.");
            }

            foreach (JsonProperty property in parsed.RootElement.EnumerateObject())
            {
                if (property.Name.Contains("path", StringComparison.OrdinalIgnoreCase))
                {
                    return LocalLicenseInstallDraft.Failed(
                        "License.Install.CallerPathRejected",
                        "License install does not accept caller filesystem paths.");
                }

                if (!AllowedDocumentFields.Contains(property.Name))
                {
                    return LocalLicenseInstallDraft.Failed(
                        "License.Install.UnsupportedField",
                        "License document contains an unsupported field: " + property.Name);
                }
            }

            LocalLicenseDocument? document = JsonSerializer.Deserialize<LocalLicenseDocument>(licenseJson, JsonOptions);
            if (document is null)
            {
                return LocalLicenseInstallDraft.Failed(
                    "License.Install.MalformedJson",
                    "License document JSON did not contain a license document.");
            }

            LocalLicenseDocument canonical = Canonicalize(document);
            string canonicalJson = JsonSerializer.Serialize(canonical, JsonOptions);
            return LocalLicenseInstallDraft.Ready(canonical, canonicalJson);
        }
        catch (JsonException ex)
        {
            return LocalLicenseInstallDraft.Failed(
                "License.Install.MalformedJson",
                "License document JSON is malformed: " + ex.Message);
        }
        catch (NotSupportedException ex)
        {
            return LocalLicenseInstallDraft.Failed(
                "License.Install.MalformedJson",
                "License document JSON is not supported: " + ex.Message);
        }
    }

    private static LocalLicenseDocument Canonicalize(LocalLicenseDocument document)
    {
        string[]? normalizedFeatures = document.Features?
            .Where(feature => !string.IsNullOrWhiteSpace(feature))
            .Select(feature => feature.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new LocalLicenseDocument(
            Clean(document.LicenseId),
            Clean(document.CustomerName),
            document.Edition,
            document.IssuedAt,
            document.ExpiresAt,
            Clean(document.MachineBinding),
            normalizedFeatures,
            Clean(document.Signature),
            document.SchemaVersion,
            Clean(document.ApplicationId),
            Clean(document.StoreId),
            document.AllowedLanTerminals,
            document.SupportExpiresAt,
            document.UpdatesUntil);
    }

    private static string? Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private LocalLicenseInstallResponse Reject(
        LicenseStatus validationStatus,
        string? sourcePath,
        string errorCode,
        string errorMessage,
        string? backupPath = null) => new(
            Rejected,
            validationStatus,
            sourcePath,
            backupPath,
            errorCode,
            errorMessage,
            _clock());

    private sealed record LocalLicenseInstallDraft(
        LocalLicenseDocument? Document,
        string? CanonicalJson,
        string? ErrorCode,
        string? ErrorMessage)
    {
        public static LocalLicenseInstallDraft Ready(
            LocalLicenseDocument document,
            string canonicalJson) => new(document, canonicalJson, ErrorCode: null, ErrorMessage: null);

        public static LocalLicenseInstallDraft Failed(
            string errorCode,
            string errorMessage) => new(null, null, errorCode, errorMessage);
    }
}

using Salryn.Core.Services.Licensing;

namespace Salryn.Infrastructure.Licensing;

public sealed class LocalLicenseValidatorOptions
{
    public string? MachineFingerprint { get; init; }

    public bool AllowDevelopmentMode { get; init; }

    public int GracePeriodDays { get; init; }

    public IReadOnlyList<string>? DevelopmentFeatures { get; init; }

    public Func<LocalLicenseDocument, bool>? SignatureVerifier { get; init; }

    /// <summary>
    /// Product-specific expected ApplicationId (e.g. "Salryn.Cafe" for Salryn Cafe).
    /// Defaults to <see cref="LocalLicenseContractPolicy.RequiredApplicationId"/> when omitted.
    /// </summary>
    public string? ExpectedApplicationId { get; init; }
}

public sealed class LocalLicenseValidator
{
    public const int SupportedSchemaVersion = LocalLicenseContractPolicy.BetaLicenseSchemaVersion;

    private readonly LocalLicenseValidatorOptions _options;
    private readonly Func<DateTimeOffset> _clock;

    public LocalLicenseValidator(
        LocalLicenseValidatorOptions? options = null,
        Func<DateTimeOffset>? clock = null)
    {
        _options = options ?? new LocalLicenseValidatorOptions();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    internal string? ConfiguredMachineFingerprint =>
        string.IsNullOrWhiteSpace(_options.MachineFingerprint)
            ? null
            : _options.MachineFingerprint.Trim();

    public LocalLicenseValidationResult Validate(LocalLicenseFileReadResult read)
    {
        ArgumentNullException.ThrowIfNull(read);
        DateTimeOffset now = _clock();
        string? machineCode = string.IsNullOrWhiteSpace(_options.MachineFingerprint)
            ? null
            : _options.MachineFingerprint.Trim();

        if (!read.Exists)
        {
            return _options.AllowDevelopmentMode
                ? Result(
                    LicenseStatus.Development,
                    LicenseEdition.Trial,
                    _options.DevelopmentFeatures ?? LicenseFeatures.AllList,
                    expiresAt: null,
                    daysRemaining: null,
                    machineMatched: true,
                    "Explicit local development licensing mode is active. This is not a production license.",
                    read.SourcePath,
                    now,
                    machineCode: machineCode)
                : Result(
                    LicenseStatus.Unlicensed,
                    edition: null,
                    Array.Empty<string>(),
                    expiresAt: null,
                    daysRemaining: null,
                    machineMatched: false,
                    "No local license file was found. Manual activation is required.",
                    read.SourcePath,
                    now,
                    machineCode: machineCode);
        }

        if (read.Malformed || read.Document is null)
        {
            return Result(
                LicenseStatus.Invalid,
                edition: null,
                Array.Empty<string>(),
                expiresAt: null,
                daysRemaining: null,
                machineMatched: false,
                "Local license JSON is malformed or unreadable: " + (read.ErrorMessage ?? "unknown error"),
                read.SourcePath,
                now,
                machineCode: machineCode);
        }

        LocalLicenseDocument document = read.Document;
        if (document.SchemaVersion != SupportedSchemaVersion)
        {
            return Invalid(document, read.SourcePath, now, "Unsupported local license schema version. Beta licenses require schema version " + SupportedSchemaVersion + ".", machineCode);
        }

        if (string.IsNullOrWhiteSpace(document.LicenseId) || string.IsNullOrWhiteSpace(document.CustomerName))
        {
            return Invalid(document, read.SourcePath, now, "Local license is missing required identity fields.", machineCode);
        }

        if (document.LicenseType is null)
        {
            return Invalid(document, read.SourcePath, now, "Local license is missing required LicenseType (Host or Terminal).", machineCode);
        }

        if (!Enum.IsDefined(document.LicenseType.Value))
        {
            return Invalid(document, read.SourcePath, now, "Local license LicenseType is not recognized.", machineCode);
        }

        if (!Enum.IsDefined(document.Edition))
        {
            return Invalid(document, read.SourcePath, now, "Local license edition is not recognized.", machineCode);
        }

        string requiredApplicationId = string.IsNullOrWhiteSpace(_options.ExpectedApplicationId)
            ? LocalLicenseContractPolicy.RequiredApplicationId
            : _options.ExpectedApplicationId.Trim();

        if (string.IsNullOrWhiteSpace(document.ApplicationId) ||
            !string.Equals(
                document.ApplicationId.Trim(),
                requiredApplicationId,
                StringComparison.Ordinal))
        {
            return Invalid(
                document,
                read.SourcePath,
                now,
                "Local license ApplicationId must be " + requiredApplicationId + ".",
                machineCode);
        }

        if (string.IsNullOrWhiteSpace(document.StoreId))
        {
            return Invalid(document, read.SourcePath, now, "Local license StoreId is required.", machineCode);
        }

        if (string.IsNullOrWhiteSpace(document.MachineBinding))
        {
            return Invalid(
                document,
                read.SourcePath,
                now,
                "Local production license MachineBinding is required and must not be empty.",
                machineCode);
        }

        string? typeError = ValidateLicenseTypeFields(document);
        if (typeError is not null)
        {
            return Invalid(document, read.SourcePath, now, typeError, machineCode);
        }

        string[] features = NormalizeAndFilterFeatures(document).ToArray();
        if (features.Length == 0)
        {
            return Invalid(
                document,
                read.SourcePath,
                now,
                document.LicenseType == LicenseType.Terminal
                    ? "Terminal license must enable LanTerminals and must not unlock host-only features."
                    : "Local license does not enable any known features for this license type.",
                machineCode);
        }

        bool machineMatched = MachineMatches(document);
        if (!machineMatched)
        {
            return Result(
                LicenseStatus.Invalid,
                document.Edition,
                Array.Empty<string>(),
                document.ExpiresAt,
                DaysRemaining(document.ExpiresAt, now),
                machineMatched: false,
                "Local license machine binding does not match this installation.",
                read.SourcePath,
                now,
                document,
                machineCode);
        }

        if (document.ExpiresAt is not null && document.ExpiresAt.Value <= now)
        {
            int daysPastExpiry = (int)Math.Floor((now - document.ExpiresAt.Value).TotalDays);
            if (_options.GracePeriodDays > 0 && daysPastExpiry <= _options.GracePeriodDays)
            {
                return Result(
                    LicenseStatus.Grace,
                    document.Edition,
                    features,
                    document.ExpiresAt,
                    DaysRemaining(document.ExpiresAt, now),
                    machineMatched,
                    "Local license is expired but inside the explicit grace window.",
                    read.SourcePath,
                    now,
                    document,
                    machineCode);
            }

            return Result(
                LicenseStatus.Expired,
                document.Edition,
                Array.Empty<string>(),
                document.ExpiresAt,
                DaysRemaining(document.ExpiresAt, now),
                machineMatched,
                "Local license is expired.",
                read.SourcePath,
                now,
                document,
                machineCode);
        }

        if (string.IsNullOrWhiteSpace(document.Signature))
        {
            return Invalid(document, read.SourcePath, now, "Local production license is unsigned and fails closed.", machineCode);
        }

        if (_options.SignatureVerifier is null)
        {
            return Invalid(document, read.SourcePath, now, "Local license signature verifier is not configured; signed-license validation fails closed.", machineCode);
        }

        if (!_options.SignatureVerifier(document))
        {
            return Invalid(document, read.SourcePath, now, "Local license signature is invalid.", machineCode);
        }

        return Result(
            LicenseStatus.Licensed,
            document.Edition,
            features,
            document.ExpiresAt,
            DaysRemaining(document.ExpiresAt, now),
            machineMatched,
            document.LicenseType == LicenseType.Terminal
                ? "Local Terminal license is valid. This does not unlock host POS features."
                : "Local Host license is valid.",
            read.SourcePath,
            now,
            document,
            machineCode);
    }

    public bool IsFeatureEnabled(LocalLicenseValidationResult status, string feature)
    {
        if (status.Status is not (LicenseStatus.Licensed or LicenseStatus.Grace or LicenseStatus.Development))
        {
            return false;
        }

        if (status.Status != LicenseStatus.Development &&
            status.LicenseType == LicenseType.Terminal &&
            LicenseFeatures.HostOnly.Contains(feature))
        {
            return false;
        }

        return status.EnabledFeatures.Contains(feature, StringComparer.Ordinal);
    }

    private static string? ValidateLicenseTypeFields(LocalLicenseDocument document)
    {
        if (document.LicenseType == LicenseType.Host)
        {
            if (document.AllowedLanTerminals < 0)
            {
                return "Host license compatibility field AllowedLanTerminals must not be negative.";
            }

            if (!string.IsNullOrWhiteSpace(document.HostLicenseId) || !string.IsNullOrWhiteSpace(document.TerminalId))
            {
                return "Host license must not include HostLicenseId or TerminalId fields.";
            }

            return null;
        }

        if (document.LicenseType == LicenseType.Terminal)
        {
            if (document.AllowedLanTerminals is not null)
            {
                return "Terminal license must not include the reserved Host compatibility field AllowedLanTerminals; each terminal is licensed individually.";
            }

            if (string.IsNullOrWhiteSpace(document.HostLicenseId))
            {
                return "Terminal license must include HostLicenseId.";
            }

            if (string.IsNullOrWhiteSpace(document.TerminalId))
            {
                return "Terminal license must include TerminalId.";
            }

            return null;
        }

        return "Local license LicenseType is not recognized.";
    }

    private static IEnumerable<string> NormalizeAndFilterFeatures(LocalLicenseDocument document)
    {
        IEnumerable<string> known = NormalizeKnownFeatures(document.Features);
        if (document.LicenseType == LicenseType.Terminal)
        {
            return known.Where(feature => LicenseFeatures.TerminalAllowed.Contains(feature));
        }

        return known;
    }

    private static IEnumerable<string> NormalizeKnownFeatures(IReadOnlyList<string>? features)
    {
        if (features is null)
        {
            yield break;
        }

        foreach (string feature in features)
        {
            if (string.IsNullOrWhiteSpace(feature))
            {
                continue;
            }

            string normalized = feature.Trim();
            if (LicenseFeatures.All.Contains(normalized))
            {
                yield return normalized;
            }
        }
    }

    private LocalLicenseValidationResult Invalid(
        LocalLicenseDocument document,
        string sourcePath,
        DateTimeOffset now,
        string message,
        string? machineCode) => Result(
            LicenseStatus.Invalid,
            document.Edition,
            Array.Empty<string>(),
            document.ExpiresAt,
            DaysRemaining(document.ExpiresAt, now),
            machineMatched: false,
            message,
            sourcePath,
            now,
            document,
            machineCode);

    private bool MachineMatches(LocalLicenseDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.MachineBinding))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(_options.MachineFingerprint) &&
            string.Equals(
                document.MachineBinding.Trim(),
                _options.MachineFingerprint.Trim(),
                StringComparison.OrdinalIgnoreCase);
    }

    private static int? DaysRemaining(DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        if (expiresAt is null)
        {
            return null;
        }

        return (int)Math.Ceiling((expiresAt.Value - now).TotalDays);
    }

    private LocalLicenseValidationResult Result(
        LicenseStatus status,
        LicenseEdition? edition,
        IReadOnlyList<string> features,
        DateTimeOffset? expiresAt,
        int? daysRemaining,
        bool machineMatched,
        string message,
        string sourcePath,
        DateTimeOffset timestamp,
        LocalLicenseDocument? document = null,
        string? machineCode = null)
    {
        string? code = machineCode ?? (string.IsNullOrWhiteSpace(_options.MachineFingerprint)
            ? null
            : _options.MachineFingerprint.Trim());

        return new(
            status,
            edition,
            features,
            expiresAt,
            daysRemaining,
            machineMatched,
            message,
            sourcePath,
            timestamp,
            document?.LicenseId,
            document?.CustomerName,
            document?.ApplicationId,
            document?.StoreId,
            document?.LicenseType == LicenseType.Host ? document.AllowedLanTerminals : null,
            document?.SupportExpiresAt,
            document?.UpdatesUntil,
            code,
            document?.LicenseType,
            document?.HostLicenseId,
            document?.TerminalId,
            code);
    }
}

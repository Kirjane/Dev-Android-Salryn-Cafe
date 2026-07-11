using Salryn.Core.Services.Licensing;

namespace Salryn.Infrastructure.Licensing;

public sealed class LocalLicenseValidatorOptions
{
    public string? MachineFingerprint { get; init; }

    public bool AllowDevelopmentMode { get; init; }

    public int GracePeriodDays { get; init; }

    public IReadOnlyList<string>? DevelopmentFeatures { get; init; }

    public Func<LocalLicenseDocument, bool>? SignatureVerifier { get; init; }
}

public sealed class LocalLicenseValidator
{
    public const int SupportedSchemaVersion = 1;

    private readonly LocalLicenseValidatorOptions _options;
    private readonly Func<DateTimeOffset> _clock;

    public LocalLicenseValidator(
        LocalLicenseValidatorOptions? options = null,
        Func<DateTimeOffset>? clock = null)
    {
        _options = options ?? new LocalLicenseValidatorOptions();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public LocalLicenseValidationResult Validate(LocalLicenseFileReadResult read)
    {
        ArgumentNullException.ThrowIfNull(read);
        DateTimeOffset now = _clock();

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
                    now)
                : Result(
                    LicenseStatus.Unlicensed,
                    edition: null,
                    Array.Empty<string>(),
                    expiresAt: null,
                    daysRemaining: null,
                    machineMatched: false,
                    "No local license file was found.",
                    read.SourcePath,
                    now);
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
                now);
        }

        LocalLicenseDocument document = read.Document;
        if (document.SchemaVersion != SupportedSchemaVersion)
        {
            return Invalid(document, read.SourcePath, now, "Unsupported local license schema version.");
        }

        if (string.IsNullOrWhiteSpace(document.LicenseId) || string.IsNullOrWhiteSpace(document.CustomerName))
        {
            return Invalid(document, read.SourcePath, now, "Local license is missing required identity fields.");
        }

        if (!Enum.IsDefined(document.Edition))
        {
            return Invalid(document, read.SourcePath, now, "Local license edition is not recognized.");
        }

        string[] features = NormalizeFeatures(document.Features).ToArray();
        if (features.Length == 0)
        {
            return Invalid(document, read.SourcePath, now, "Local license does not enable any known features.");
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
                now);
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
                    now);
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
                now);
        }

        if (string.IsNullOrWhiteSpace(document.Signature))
        {
            return Invalid(document, read.SourcePath, now, "Local production license is unsigned and fails closed.");
        }

        if (_options.SignatureVerifier is null)
        {
            return Invalid(document, read.SourcePath, now, "Local license signature verifier is not configured; signed-license validation fails closed in M1.");
        }

        if (!_options.SignatureVerifier(document))
        {
            return Invalid(document, read.SourcePath, now, "Local license signature is invalid.");
        }

        return Result(
            LicenseStatus.Licensed,
            document.Edition,
            features,
            document.ExpiresAt,
            DaysRemaining(document.ExpiresAt, now),
            machineMatched,
            "Local license is valid.",
            read.SourcePath,
            now);
    }

    public bool IsFeatureEnabled(LocalLicenseValidationResult status, string feature)
    {
        if (status.Status is not (LicenseStatus.Licensed or LicenseStatus.Grace or LicenseStatus.Development))
        {
            return false;
        }

        return status.EnabledFeatures.Contains(feature, StringComparer.Ordinal);
    }

    private static LocalLicenseValidationResult Invalid(
        LocalLicenseDocument document,
        string sourcePath,
        DateTimeOffset now,
        string message) => Result(
            LicenseStatus.Invalid,
            document.Edition,
            Array.Empty<string>(),
            document.ExpiresAt,
            DaysRemaining(document.ExpiresAt, now),
            machineMatched: false,
            message,
            sourcePath,
            now);

    private bool MachineMatches(LocalLicenseDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.MachineBinding))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(_options.MachineFingerprint) &&
            string.Equals(document.MachineBinding.Trim(), _options.MachineFingerprint.Trim(), StringComparison.Ordinal);
    }

    private static IEnumerable<string> NormalizeFeatures(IReadOnlyList<string>? features)
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

    private static int? DaysRemaining(DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        if (expiresAt is null)
        {
            return null;
        }

        return (int)Math.Ceiling((expiresAt.Value - now).TotalDays);
    }

    private static LocalLicenseValidationResult Result(
        LicenseStatus status,
        LicenseEdition? edition,
        IReadOnlyList<string> features,
        DateTimeOffset? expiresAt,
        int? daysRemaining,
        bool machineMatched,
        string message,
        string sourcePath,
        DateTimeOffset timestamp) => new(
            status,
            edition,
            features,
            expiresAt,
            daysRemaining,
            machineMatched,
            message,
            sourcePath,
            timestamp);
}

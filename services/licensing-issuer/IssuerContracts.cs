using System.Text.Json;
using System.Text.Json.Serialization;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Licensing;

namespace Salryn.LicensingIssuer.Server;

public static class IssuerConstants
{
    public const string ProductCafe = "Salryn.Cafe";
    public const string ProductRetail = "Salryn.POS";
    public const string RoleAdmin = "IssuerAdmin";
    public const string RoleReviewer = "IssuerReviewer";
    public const string StatusDraft = "Draft";
    public const string StatusAwaitingApproval = "AwaitingApproval";
    public const string StatusApproved = "Approved";
    public const string StatusIssued = "Issued";
    public const string StatusSuperseded = "Superseded";
    public const string StatusRejected = "Rejected";
    public const string SessionCookieName = "salryn_issuer_session";
    public const string CsrfHeaderName = "X-Salryn-Issuer-Csrf";

    public static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

public sealed record IssuerAdminUser(
    Guid Id,
    string Username,
    string DisplayName,
    string Role,
    bool IsActive,
    DateTimeOffset CreatedAtUtc);

public sealed record IssuerSessionInfo(
    bool IsAuthenticated,
    string? CsrfToken,
    IssuerAdminUser? User);

public sealed record IssuerCustomerRecord(
    Guid Id,
    string CustomerName,
    string? ContactEmail,
    string? Notes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record IssuerStoreRecord(
    Guid Id,
    Guid CustomerId,
    string StoreCode,
    string StoreName,
    string ProductId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record IssuerLicenseRequestRecord(
    Guid Id,
    string LicenseId,
    string? IdempotencyKey,
    string Status,
    Guid CustomerId,
    string CustomerName,
    Guid StoreId,
    string StoreCode,
    string StoreName,
    string ApplicationId,
    LicenseType LicenseType,
    LicenseEdition Edition,
    string MachineBinding,
    IReadOnlyList<string> Features,
    int? AllowedLanTerminals,
    string? HostLicenseId,
    string? TerminalId,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? SupportExpiresAtUtc,
    DateTimeOffset? UpdatesUntilUtc,
    Guid? SupersedesRequestId,
    string CanonicalSigningPayload,
    string? SignedLicenseJson,
    string? SignedLicenseSha256,
    string? SignerKeySpkiSha256,
    string? SignerMode,
    bool VerifierPassed,
    string? ReviewNote,
    string CreatedBy,
    string? ApprovedBy,
    string? IssuedBy,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ApprovedAtUtc,
    DateTimeOffset? IssuedAtServerUtc);

public sealed record IssuerAuditEntry(
    long Id,
    string ActorUsername,
    string ActorRole,
    string Action,
    string EntityType,
    string EntityId,
    string DetailJson,
    DateTimeOffset CreatedAtUtc);

public sealed record IssuerDashboardSummary(
    int DraftCount,
    int AwaitingApprovalCount,
    int ApprovedCount,
    int IssuedCount,
    int ExpiredIssuedCount,
    int SupersededCount,
    int RejectedCount,
    int CustomerCount,
    int StoreCount,
    SignerStatusSnapshot Signer);

public sealed record SignerStatusSnapshot(
    bool IsConfigured,
    bool CanIssue,
    string SignerMode,
    string? PublicKeySpkiSha256,
    string TrustedRuntimeSpkiSha256,
    bool MatchesTrustedRuntimeKey,
    string Message);

public sealed record LoginRequestDto(string Username, string Password);

public sealed record CreateCustomerRequestDto(string CustomerName, string? ContactEmail, string? Notes);

public sealed record CreateStoreRequestDto(Guid CustomerId, string StoreCode, string StoreName, string ProductId);

public sealed record CreateLicenseDraftRequestDto(
    Guid CustomerId,
    Guid StoreId,
    string ApplicationId,
    string LicenseType,
    string Edition,
    string MachineBinding,
    IReadOnlyList<string> Features,
    int? AllowedLanTerminals,
    string? HostLicenseId,
    string? TerminalId,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? SupportExpiresAtUtc,
    DateTimeOffset? UpdatesUntilUtc,
    string? IdempotencyKey,
    Guid? SupersedesRequestId);

public sealed record ReviewDecisionRequestDto(string? Note);

public sealed record ReissueRequestDto(
    string? MachineBinding,
    DateTimeOffset? ExpiresAtUtc,
    IReadOnlyList<string>? Features,
    int? AllowedLanTerminals,
    string? Note,
    string? IdempotencyKey);

public sealed record DraftValidationResult(
    bool IsValid,
    string? ErrorCode,
    string? ErrorMessage,
    IReadOnlyList<string> Warnings,
    LocalLicenseDocument? Document,
    string? CanonicalSigningPayload);

public static class IssuerLicenseContractValidator
{
    private static readonly HashSet<string> AllowedProducts = new(StringComparer.Ordinal)
    {
        IssuerConstants.ProductCafe,
        IssuerConstants.ProductRetail
    };

    public static DraftValidationResult BuildAndValidateDraft(
        string licenseId,
        string customerName,
        string storeCode,
        string applicationId,
        LicenseType licenseType,
        LicenseEdition edition,
        string machineBinding,
        IReadOnlyList<string>? rawFeatures,
        int? allowedLanTerminals,
        string? hostLicenseId,
        string? terminalId,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset? expiresAtUtc,
        DateTimeOffset? supportExpiresAtUtc,
        DateTimeOffset? updatesUntilUtc)
    {
        List<string> warnings = [];

        string cleanAppId = (applicationId ?? string.Empty).Trim();
        if (!AllowedProducts.Contains(cleanAppId))
        {
            return Fail("Issuer.ProductUnsupported", $"ApplicationId must be '{IssuerConstants.ProductCafe}' or '{IssuerConstants.ProductRetail}'.");
        }

        string cleanMachine = (machineBinding ?? string.Empty).Trim();
        if (cleanMachine.Length < 6 || cleanMachine.Length > 128 || cleanMachine.Any(char.IsWhiteSpace))
        {
            return Fail("Issuer.MachineBindingInvalid", "MachineBinding must be 6-128 non-whitespace characters copied from the workstation activation screen.");
        }

        if (expiresAtUtc.HasValue && expiresAtUtc.Value <= issuedAtUtc)
        {
            return Fail("Issuer.ExpiryInPast", "ExpiresAtUtc must be in the future.");
        }

        if (licenseType == LicenseType.Host)
        {
            if (allowedLanTerminals is < 0 or > 100)
            {
                return Fail("Issuer.AllowedLanTerminalsInvalid", "AllowedLanTerminals for Host licenses must be between 0 and 100.");
            }
            if (!string.IsNullOrWhiteSpace(hostLicenseId) || !string.IsNullOrWhiteSpace(terminalId))
            {
                return Fail("Issuer.HostDisallowedTerminalFields", "Host licenses must not include HostLicenseId or TerminalId.");
            }
        }
        else if (licenseType == LicenseType.Terminal)
        {
            if (allowedLanTerminals is not null)
            {
                return Fail("Issuer.TerminalDisallowedLanCount", "Terminal licenses must not include AllowedLanTerminals.");
            }
            if (string.IsNullOrWhiteSpace(hostLicenseId) || string.IsNullOrWhiteSpace(terminalId))
            {
                return Fail("Issuer.TerminalRequiredFields", "Terminal licenses require both HostLicenseId and TerminalId.");
            }
        }
        else
        {
            return Fail("Issuer.LicenseTypeInvalid", "LicenseType must be Host or Terminal.");
        }

        string[] normalizedFeatures = (rawFeatures ?? Array.Empty<string>())
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim())
            .Where(f => LicenseFeatures.AllList.Contains(f, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (licenseType == LicenseType.Terminal)
        {
            string[] invalidForTerminal = normalizedFeatures
                .Where(f => !LicenseFeatures.TerminalAllowed.Contains(f))
                .ToArray();
            if (invalidForTerminal.Length > 0)
            {
                return Fail("Issuer.TerminalHostFeaturesDenied", "Terminal license cannot grant host-only features: " + string.Join(", ", invalidForTerminal));
            }
            if (!normalizedFeatures.Contains(LicenseFeatures.LanTerminals, StringComparer.Ordinal))
            {
                return Fail("Issuer.TerminalRequiresLanFeature", "Terminal license must include the LanTerminals feature.");
            }
        }
        else if (normalizedFeatures.Length == 0)
        {
            return Fail("Issuer.FeaturesRequired", "At least one valid Salryn feature must be enabled.");
        }

        if (!expiresAtUtc.HasValue)
        {
            warnings.Add("No expiry date configured (perpetual license). Confirm this is authorized for this customer.");
        }

        if (string.Equals(cleanAppId, IssuerConstants.ProductCafe, StringComparison.Ordinal))
        {
            warnings.Add("Offline license delivery: once distributed, this signed file cannot be remotely revoked without a future runtime revocation design.");
        }

        var doc = new LocalLicenseDocument(
            LicenseId: licenseId.Trim(),
            CustomerName: customerName.Trim(),
            Edition: edition,
            IssuedAt: issuedAtUtc.ToUniversalTime(),
            ExpiresAt: expiresAtUtc?.ToUniversalTime(),
            MachineBinding: cleanMachine,
            Features: normalizedFeatures,
            Signature: null,
            SchemaVersion: LocalLicenseContractPolicy.BetaLicenseSchemaVersion,
            ApplicationId: cleanAppId,
            StoreId: storeCode.Trim(),
            AllowedLanTerminals: licenseType == LicenseType.Host ? (allowedLanTerminals ?? 0) : null,
            SupportExpiresAt: supportExpiresAtUtc?.ToUniversalTime(),
            UpdatesUntil: updatesUntilUtc?.ToUniversalTime(),
            LicenseType: licenseType,
            HostLicenseId: licenseType == LicenseType.Terminal ? hostLicenseId!.Trim() : null,
            TerminalId: licenseType == LicenseType.Terminal ? terminalId!.Trim() : null);

        string canonicalPayload = LocalLicenseSignatureVerifier.BuildSigningPayload(doc);
        return new DraftValidationResult(true, null, null, warnings, doc, canonicalPayload);
    }

    private static DraftValidationResult Fail(string code, string msg) =>
        new(false, code, msg, Array.Empty<string>(), null, null);
}

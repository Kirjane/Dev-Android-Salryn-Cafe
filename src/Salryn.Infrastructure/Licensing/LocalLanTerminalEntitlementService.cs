using System.Text.Json;
using System.Text.Json.Serialization;
using Salryn.Core.Common;
using Salryn.Core.Services.Licensing;

namespace Salryn.Infrastructure.Licensing;

/// <summary>
/// Evaluates Host LAN terminal commercial entitlement from the local offline license.
/// Does not require cloud. Private signing material is never used here.
/// Abuse hard-cap defaults mirror RemotePosCapacityPolicy (10 paired / 3 sessions).
/// </summary>
public sealed class LocalLanTerminalEntitlementService
{
    public const int DefaultHardCap = 10;
    public const int DefaultMaxActiveSessions = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ILocalLicenseService _licenses;
    private readonly Func<LocalLicenseDocument, bool> _signatureVerifier;
    private readonly int _hardCap;
    private readonly int _defaultMaxActiveSessions;

    public LocalLanTerminalEntitlementService(
        ILocalLicenseService? licenses = null,
        int hardCap = DefaultHardCap,
        int defaultMaxActiveSessions = DefaultMaxActiveSessions,
        Func<LocalLicenseDocument, bool>? signatureVerifier = null)
    {
        _licenses = licenses ?? new LocalLicenseService();
        _signatureVerifier = signatureVerifier ?? LocalLicenseSignatureVerifier.Verify;
        _hardCap = hardCap > 0 ? hardCap : DefaultHardCap;
        _defaultMaxActiveSessions = defaultMaxActiveSessions > 0
            ? defaultMaxActiveSessions
            : DefaultMaxActiveSessions;
    }

    public async Task<LocalLanTerminalEntitlementStatus> GetHostEntitlementAsync(
        CancellationToken cancellationToken = default)
    {
        LocalLicenseValidationResult status = await _licenses
            .GetStatusAsync(cancellationToken)
            .ConfigureAwait(false);

        string contact = LocalLicenseContractPolicy.BetaActivationContactEmail;

        if (status.Status is not (LicenseStatus.Licensed or LicenseStatus.Grace or LicenseStatus.Development))
        {
            return Blocked(
                status,
                allowed: 0,
                "Operational LAN terminals require a valid signed Host license with terminal entitlement. Contact "
                    + contact
                    + " to license additional terminals.",
                contact);
        }

        if (status.LicenseType == LicenseType.Terminal)
        {
            return Blocked(
                status,
                allowed: 0,
                "A Terminal license cannot unlock Host LAN operational controls. Install a Host license on the host workstation.",
                contact);
        }

        if (status.LicenseType != LicenseType.Host)
        {
            return Blocked(
                status,
                allowed: 0,
                "Host LAN operational controls require LicenseType Host.",
                contact);
        }

        if (status.Status != LicenseStatus.Development &&
            !status.EnabledFeatures.Contains(LicenseFeatures.LanTerminals, StringComparer.Ordinal))
        {
            return Blocked(
                status,
                allowed: 0,
                "Host license does not include LanTerminals. Contact " + contact + " to license LAN terminals.",
                contact);
        }

        int allowed = status.AllowedLanTerminals ?? 0;
        if (allowed <= 0)
        {
            return Blocked(
                status,
                allowed: 0,
                "Host license allows 0 LAN terminals. Contact " + contact + " to license additional operational terminals.",
                contact);
        }

        int effectivePaired = Math.Min(allowed, _hardCap);
        int effectiveSessions = Math.Min(_defaultMaxActiveSessions, effectivePaired);

        return new LocalLanTerminalEntitlementStatus(
            HostOperationalLanAllowed: true,
            AllowedLanTerminals: allowed,
            EffectiveMaxPairedTerminals: effectivePaired,
            EffectiveMaxActiveSessions: Math.Max(1, effectiveSessions),
            LicenseStatus: status.Status,
            LicenseType: status.LicenseType,
            HostLicenseId: status.LicenseId,
            StoreId: status.StoreId,
            Message: "Host license allows " + allowed + " operational LAN terminal(s). Effective max paired is "
                + effectivePaired + " (hard cap " + _hardCap + "). Each terminal still requires its own Terminal license.",
            ContactEmail: contact);
    }

    /// <summary>
    /// Validates a terminal license document presented to the host (optional protocol field).
    /// Host license cannot satisfy terminal proof.
    /// </summary>
    public Result ValidateTerminalLicenseProof(
        string? terminalLicenseJson,
        LocalLanTerminalEntitlementStatus hostEntitlement)
    {
        ArgumentNullException.ThrowIfNull(hostEntitlement);

        if (string.IsNullOrWhiteSpace(terminalLicenseJson))
        {
            return Result.Fail(Error.Validation(
                LocalLanTerminalEntitlementCodes.TerminalProofInvalid,
                "Terminal license proof is required for operational LAN terminal activation."));
        }

        LocalLicenseDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<LocalLicenseDocument>(terminalLicenseJson, JsonOptions);
        }
        catch (JsonException)
        {
            return Result.Fail(Error.Validation(
                LocalLanTerminalEntitlementCodes.TerminalProofInvalid,
                "Terminal license proof JSON is malformed."));
        }

        if (document is null)
        {
            return Result.Fail(Error.Validation(
                LocalLanTerminalEntitlementCodes.TerminalProofInvalid,
                "Terminal license proof JSON did not contain a license document."));
        }

        if (document.LicenseType == LicenseType.Host)
        {
            return Result.Fail(Error.Forbidden(
                LocalLanTerminalEntitlementCodes.HostLicenseAsTerminal,
                "Host license cannot be used as terminal proof. Each LAN terminal requires a separate Terminal license."));
        }

        if (document.LicenseType != LicenseType.Terminal)
        {
            return Result.Fail(Error.Forbidden(
                LocalLanTerminalEntitlementCodes.WrongLicenseType,
                "Terminal license proof must have LicenseType Terminal."));
        }

        // Terminal machine binding is enforced on the terminal device. Host verifies commercial identity.
        LocalLicenseValidatorOptions options = new()
        {
            MachineFingerprint = string.IsNullOrWhiteSpace(document.MachineBinding)
                ? null
                : document.MachineBinding.Trim(),
            SignatureVerifier = _signatureVerifier
        };
        LocalLicenseValidator terminalValidator = new(options);
        LocalLicenseFileReadResult read = new(
            "terminal-proof",
            Exists: true,
            Malformed: false,
            Document: document,
            ErrorMessage: null);
        LocalLicenseValidationResult validation = terminalValidator.Validate(read);
        if (validation.Status != LicenseStatus.Licensed)
        {
            return Result.Fail(Error.Forbidden(
                LocalLanTerminalEntitlementCodes.TerminalProofInvalid,
                "Terminal license proof failed validation: " + validation.Message));
        }

        if (!string.IsNullOrWhiteSpace(hostEntitlement.StoreId) &&
            !string.Equals(
                hostEntitlement.StoreId.Trim(),
                document.StoreId?.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return Result.Fail(Error.Forbidden(
                LocalLanTerminalEntitlementCodes.TerminalStoreMismatch,
                "Terminal license StoreId does not match the Host license StoreId."));
        }

        if (!string.IsNullOrWhiteSpace(hostEntitlement.HostLicenseId) &&
            !string.Equals(
                hostEntitlement.HostLicenseId.Trim(),
                document.HostLicenseId?.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return Result.Fail(Error.Forbidden(
                LocalLanTerminalEntitlementCodes.TerminalHostMismatch,
                "Terminal license HostLicenseId does not match this Host LicenseId."));
        }

        if (string.IsNullOrWhiteSpace(document.TerminalId))
        {
            return Result.Fail(Error.Validation(
                LocalLanTerminalEntitlementCodes.TerminalProofInvalid,
                "Terminal license must include TerminalId."));
        }

        return Result.Success();
    }

    private static LocalLanTerminalEntitlementStatus Blocked(
        LocalLicenseValidationResult status,
        int allowed,
        string message,
        string contact) => new(
            HostOperationalLanAllowed: false,
            AllowedLanTerminals: allowed,
            EffectiveMaxPairedTerminals: 0,
            EffectiveMaxActiveSessions: 0,
            LicenseStatus: status.Status,
            LicenseType: status.LicenseType,
            HostLicenseId: status.LicenseId,
            StoreId: status.StoreId,
            Message: message,
            ContactEmail: contact);
}

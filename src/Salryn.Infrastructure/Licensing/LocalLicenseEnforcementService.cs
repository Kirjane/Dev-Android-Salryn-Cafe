using Salryn.Core.Services.Licensing;

namespace Salryn.Infrastructure.Licensing;

public sealed class LocalLicenseEnforcementService : ILocalLicenseEnforcementService
{
    private readonly ILocalLicenseService _licenses;

    public LocalLicenseEnforcementService(ILocalLicenseService licenses)
    {
        _licenses = licenses ?? throw new ArgumentNullException(nameof(licenses));
    }

    public async Task<LicenseEnforcementDecision> RequireFeatureAsync(
        string feature,
        CancellationToken cancellationToken = default)
    {
        string requiredFeature = feature?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(requiredFeature) || !LicenseFeatures.All.Contains(requiredFeature))
        {
            return Deny(
                requiredFeature,
                LicenseStatus.Invalid,
                LicenseEnforcementReason.UnknownFeature,
                "Requested local license feature is not recognized.",
                Array.Empty<string>());
        }

        LocalLicenseValidationResult status = await _licenses
            .GetStatusAsync(cancellationToken)
            .ConfigureAwait(false);

        if (status.Status == LicenseStatus.Licensed)
        {
            if (status.LicenseType == LicenseType.Terminal &&
                LicenseFeatures.HostOnly.Contains(requiredFeature))
            {
                return Deny(
                    requiredFeature,
                    status,
                    LicenseEnforcementReason.WrongLicenseType,
                    "Terminal license cannot unlock host-only features. Install a Host license on the host workstation.");
            }

            if (status.LicenseType == LicenseType.Host &&
                requiredFeature == LicenseFeatures.LanTerminals)
            {
                return Allow(
                    requiredFeature,
                    status,
                    LicenseEnforcementReason.LicensedFeatureAllowed,
                    "Valid Host license allows host-local Terminal Management; each client still requires its own signed Terminal license.");
            }

            return status.EnabledFeatures.Contains(requiredFeature, StringComparer.Ordinal)
                ? Allow(
                    requiredFeature,
                    status,
                    LicenseEnforcementReason.LicensedFeatureAllowed,
                    "Licensed feature is allowed by the local license.")
                : Deny(
                    requiredFeature,
                    status,
                    LicenseEnforcementReason.FeatureNotLicensed,
                    "Local license does not include the requested feature.");
        }

        if (status.Status == LicenseStatus.Development)
        {
            return status.EnabledFeatures.Contains(requiredFeature, StringComparer.Ordinal)
                ? Allow(
                    requiredFeature,
                    status,
                    LicenseEnforcementReason.DevelopmentModeAllowed,
                    "Feature is allowed by explicit local development licensing mode.")
                : Deny(
                    requiredFeature,
                    status,
                    LicenseEnforcementReason.FeatureNotLicensed,
                    "Explicit local development licensing mode does not include the requested feature.");
        }

        LicenseEnforcementReason reason = ReasonForDeniedStatus(status);
        return Deny(
            requiredFeature,
            status,
            reason,
            status.Message);
    }

    public async Task<bool> IsFeatureAllowedAsync(
        string feature,
        CancellationToken cancellationToken = default)
    {
        LicenseEnforcementDecision decision = await RequireFeatureAsync(feature, cancellationToken)
            .ConfigureAwait(false);
        return decision.Allowed;
    }

    private static LicenseEnforcementReason ReasonForDeniedStatus(LocalLicenseValidationResult status)
    {
        return status.Status switch
        {
            LicenseStatus.Unlicensed => LicenseEnforcementReason.MissingLicense,
            LicenseStatus.Expired => LicenseEnforcementReason.ExpiredLicense,
            LicenseStatus.Grace => LicenseEnforcementReason.ExpiredLicense,
            LicenseStatus.Invalid => InvalidReason(status),
            _ => LicenseEnforcementReason.InvalidLicense
        };
    }

    private static LicenseEnforcementReason InvalidReason(LocalLicenseValidationResult status)
    {
        if (!status.MachineMatched &&
            status.Message.Contains("machine binding", StringComparison.OrdinalIgnoreCase))
        {
            return LicenseEnforcementReason.MachineMismatch;
        }

        if (status.Message.Contains("verifier", StringComparison.OrdinalIgnoreCase))
        {
            return LicenseEnforcementReason.VerifierUnavailable;
        }

        if (status.Message.Contains("LicenseType", StringComparison.OrdinalIgnoreCase) ||
            status.Message.Contains("Host license", StringComparison.OrdinalIgnoreCase) ||
            status.Message.Contains("Terminal license", StringComparison.OrdinalIgnoreCase))
        {
            return LicenseEnforcementReason.WrongLicenseType;
        }

        return LicenseEnforcementReason.InvalidLicense;
    }

    private static LicenseEnforcementDecision Allow(
        string requiredFeature,
        LocalLicenseValidationResult status,
        LicenseEnforcementReason reason,
        string message) => new(
            true,
            requiredFeature,
            status.Status,
            reason,
            message,
            status.EnabledFeatures);

    private static LicenseEnforcementDecision Deny(
        string requiredFeature,
        LocalLicenseValidationResult status,
        LicenseEnforcementReason reason,
        string message) => Deny(
            requiredFeature,
            status.Status,
            reason,
            message,
            status.EnabledFeatures);

    private static LicenseEnforcementDecision Deny(
        string requiredFeature,
        LicenseStatus status,
        LicenseEnforcementReason reason,
        string message,
        IReadOnlyList<string> enabledFeatures) => new(
            false,
            requiredFeature,
            status,
            reason,
            message,
            enabledFeatures);
}

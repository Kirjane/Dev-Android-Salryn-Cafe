namespace Salryn.Core.Services.Licensing;

public interface ILocalLicenseEnforcementService
{
    Task<LicenseEnforcementDecision> RequireFeatureAsync(
        string feature,
        CancellationToken cancellationToken = default);

    Task<bool> IsFeatureAllowedAsync(
        string feature,
        CancellationToken cancellationToken = default);
}

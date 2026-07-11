namespace Salryn.Core.Services.Licensing;

public interface ILocalLicenseService
{
    Task<LocalLicenseValidationResult> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<bool> HasFeatureAsync(string feature, CancellationToken cancellationToken = default);
}

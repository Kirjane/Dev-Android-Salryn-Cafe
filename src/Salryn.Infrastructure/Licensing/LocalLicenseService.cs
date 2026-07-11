using Salryn.Core.Services.Licensing;

namespace Salryn.Infrastructure.Licensing;

public sealed class LocalLicenseService : ILocalLicenseService
{
    private readonly LocalLicenseFileStore _store;
    private readonly LocalLicenseValidator _validator;

    public LocalLicenseService(
        LocalLicenseFileStore? store = null,
        LocalLicenseValidator? validator = null)
    {
        _store = store ?? new LocalLicenseFileStore();
        _validator = validator ?? new LocalLicenseValidator();
    }

    public async Task<LocalLicenseValidationResult> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        LocalLicenseFileReadResult read = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
        return _validator.Validate(read);
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
}

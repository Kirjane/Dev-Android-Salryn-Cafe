using Salryn.Core.Common;
using Salryn.Core.Official;

namespace Salryn.Core.Services;

/// <summary>
/// Host-local internal dry-run package service (RMO-BIR-M8).
/// Persists engineering packages only — never mutates sales/sequence/EJ live paths.
/// </summary>
public interface IHostOfficialDryRunPackageService
{
    Task<Result<OfficialDryRunPackageContract>> CreateAsync(
        OfficialDryRunPackageCreateRequest request,
        string? createdByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Build and persist an internal dry-run package from an operational sale (read-only).
    /// Never mutates the sale, sequence, EJ, shift, payment, or inventory.
    /// </summary>
    Task<Result<OfficialDryRunPackageContract>> CreateFromSaleAsync(
        Guid saleId,
        string? createdByUserId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<OfficialDryRunPackageSummaryContract>>> ListRecentAsync(
        int take = 20,
        CancellationToken cancellationToken = default);

    Task<Result<OfficialDryRunPackageContract>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}

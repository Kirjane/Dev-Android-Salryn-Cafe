using Salryn.Core.Common;
using Salryn.Core.Official;

namespace Salryn.Core.Services;

/// <summary>
/// Host-local official setup (RMO-BIR-M4). Never posts sales or advances series as allocation.
/// </summary>
public interface IHostOfficialSetupService
{
    Task<Result<OfficialSetupSnapshotContract>> GetSnapshotAsync(
        CancellationToken cancellationToken = default);

    Task<Result<OfficialSetupSnapshotContract>> SaveBusinessAsync(
        OfficialSetupBusinessSaveRequest request,
        string? updatedByUserId,
        CancellationToken cancellationToken = default);

    Task<Result<OfficialSetupSnapshotContract>> SaveMachineAsync(
        OfficialSetupMachineSaveRequest request,
        string? updatedByUserId,
        CancellationToken cancellationToken = default);

    Task<Result<OfficialSetupSnapshotContract>> SaveReceiptAsync(
        OfficialSetupReceiptSaveRequest request,
        string? updatedByUserId,
        CancellationToken cancellationToken = default);

    Task<Result<OfficialSetupSnapshotContract>> SaveSeriesAsync(
        OfficialSetupSeriesSaveRequest request,
        string? updatedByUserId,
        CancellationToken cancellationToken = default);
}

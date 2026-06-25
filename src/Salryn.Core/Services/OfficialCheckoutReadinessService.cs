using Salryn.Core.Common;

namespace Salryn.Core.Services;

public sealed record OfficialCheckoutReadinessRequest(
    Guid BirMachineProfileId,
    string DocumentKind,
    string SequenceScopeCode,
    int Year);

public sealed record OfficialCheckoutReadinessBlock(
    string Code,
    string Message);

public sealed record OfficialCheckoutReadinessResult(
    Guid BirMachineProfileId,
    string DocumentKind,
    string SequenceScopeCode,
    int Year,
    Guid? ActiveSequenceId,
    OfficialDocumentSnapshotDraftMachineProfileInput? MachineProfile,
    IReadOnlyList<OfficialCheckoutReadinessBlock> Blocks)
{
    public bool IsReady => Blocks.Count == 0;
}

public interface IOfficialCheckoutReadinessService
{
    Task<Result<OfficialCheckoutReadinessResult>> CheckAsync(
        OfficialCheckoutReadinessRequest request,
        CancellationToken cancellationToken = default);
}

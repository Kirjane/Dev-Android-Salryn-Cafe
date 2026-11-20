namespace Salryn.Sync.Contracts;

public sealed record SyncOperationFinalResolutionV1(
    Guid OperationId,
    long ReceivedSequence,
    DateTimeOffset ReceivedAtUtc,
    string Status,
    string ResultCode,
    string ResultJson,
    DateTimeOffset ResolvedAtUtc)
{
    public bool IsApplied =>
        string.Equals(Status, SaleCommittedV1Contract.Applied, StringComparison.Ordinal);

    public bool IsRejected =>
        string.Equals(Status, SaleCommittedV1Contract.Rejected, StringComparison.Ordinal);

    public bool IsNeedsReview =>
        string.Equals(Status, SaleCommittedV1Contract.NeedsReview, StringComparison.Ordinal);
}

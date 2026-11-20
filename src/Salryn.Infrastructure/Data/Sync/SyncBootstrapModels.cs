namespace Salryn.Infrastructure.Data.Sync;

public sealed record SyncBootstrapEvidence(
    Guid StoreId,
    Guid CentralInstanceId,
    long BaselineServerSequence,
    string SnapshotId,
    string SnapshotHash,
    DateTimeOffset AppliedAtUtc);

public sealed record SyncBootstrapApplyResult(
    bool IsSuccess,
    SyncRemoteState? State,
    SyncBootstrapEvidence? Evidence,
    bool IsReplay,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static SyncBootstrapApplyResult Success(
        SyncRemoteState state,
        SyncBootstrapEvidence evidence,
        bool isReplay) =>
        new(true, state, evidence, isReplay, null, null);

    public static SyncBootstrapApplyResult Fail(
        SyncRemoteState? state,
        string code,
        string message) =>
        new(false, state, null, false, code, message);
}

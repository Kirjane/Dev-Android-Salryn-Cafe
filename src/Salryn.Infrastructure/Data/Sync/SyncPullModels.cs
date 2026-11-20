namespace Salryn.Infrastructure.Data.Sync;

public sealed record SyncRemoteState(
    Guid StoreId,
    Guid? CentralInstanceId,
    long LastReceivedServerSequence,
    long LastAppliedServerSequence,
    string State,
    string? LastError,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record SyncRemoteBindingResult(
    bool IsSuccess,
    SyncRemoteState? State,
    bool IsReplay,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static SyncRemoteBindingResult Success(SyncRemoteState state, bool isReplay) =>
        new(true, state, isReplay, null, null);

    public static SyncRemoteBindingResult Fail(string code, string message) =>
        new(false, null, false, code, message);
}

public sealed record SyncPullPageAcceptanceResult(
    bool IsSuccess,
    SyncRemoteState? State,
    int InsertedCount,
    int DuplicateCount,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static SyncPullPageAcceptanceResult Success(
        SyncRemoteState state,
        int insertedCount,
        int duplicateCount) =>
        new(true, state, insertedCount, duplicateCount, null, null);

    public static SyncPullPageAcceptanceResult Fail(
        SyncRemoteState? state,
        string code,
        string message) =>
        new(false, state, 0, 0, code, message);
}

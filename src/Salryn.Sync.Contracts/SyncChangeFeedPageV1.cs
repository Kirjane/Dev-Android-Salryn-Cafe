namespace Salryn.Sync.Contracts;

public sealed record SyncChangeFeedPageV1(
    Guid CentralInstanceId,
    long AfterServerSequence,
    long HighWatermarkServerSequence,
    IReadOnlyList<AppliedBusinessChangeV1> Changes,
    bool HasMore);

public sealed record SyncChangeFeedReadResult(
    bool IsSuccess,
    SyncChangeFeedPageV1? Page,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static SyncChangeFeedReadResult Success(SyncChangeFeedPageV1 page) =>
        new(true, page, null, null);

    public static SyncChangeFeedReadResult Fail(string code, string message) =>
        new(false, null, code, message);
}

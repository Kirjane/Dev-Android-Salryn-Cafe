namespace Salryn.Infrastructure.Data.Sync;

public enum SyncProjectionFailurePoint
{
    None = 0,
    BeforeDomainWrite = 1,
    AfterDomainWriteBeforeResolution = 2
}

public interface ISyncProjectionFailureInjector
{
    void ThrowIfRequested(SyncProjectionFailurePoint point, long serverSequence);
}

public sealed class NoOpSyncProjectionFailureInjector : ISyncProjectionFailureInjector
{
    public static NoOpSyncProjectionFailureInjector Instance { get; } = new();

    private NoOpSyncProjectionFailureInjector()
    {
    }

    public void ThrowIfRequested(SyncProjectionFailurePoint point, long serverSequence)
    {
    }
}

public sealed record SyncProjectionResult(
    bool IsSuccess,
    bool HasWork,
    long? ServerSequence,
    string? ApplyStatus,
    bool IsOriginEcho,
    SyncRemoteState? State,
    string? Code,
    string? Message)
{
    public static SyncProjectionResult NoWork(SyncRemoteState state) =>
        new(true, false, null, null, false, state, "Sync.ProjectionCaughtUp", null);

    public static SyncProjectionResult Applied(
        long serverSequence,
        bool isOriginEcho,
        SyncRemoteState state,
        string code) =>
        new(true, true, serverSequence, Salryn.Sync.Contracts.SyncProjectionContract.Applied,
            isOriginEcho, state, code, null);

    public static SyncProjectionResult Reconciliation(
        long? serverSequence,
        SyncRemoteState? state,
        string code,
        string message) =>
        new(false, serverSequence.HasValue, serverSequence,
            Salryn.Sync.Contracts.SyncProjectionContract.ReconciliationRequired,
            false, state, code, message);

    public static SyncProjectionResult Fail(
        long? serverSequence,
        SyncRemoteState? state,
        string code,
        string message) =>
        new(false, serverSequence.HasValue, serverSequence, null, false, state, code, message);
}

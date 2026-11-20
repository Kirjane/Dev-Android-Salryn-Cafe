namespace Salryn.Mobile.Foundation.P0;

public sealed record P0TransportResult(bool Acknowledged, string Message)
{
    public static P0TransportResult Ack(string message = "ACK") => new(true, message);

    public static P0TransportResult Fail(string message) => new(false, message);
}

public interface IP0OutboxTransport
{
    Task<P0TransportResult> SendAsync(
        P0OutboxItem item,
        CancellationToken cancellationToken = default);
}

public sealed record P0DispatchSummary(int Attempted, int Acknowledged, int Failed);

public sealed class P0OutboxDispatcher
{
    private readonly P0LocalStore _store;
    private readonly IP0OutboxTransport _transport;

    public P0OutboxDispatcher(P0LocalStore store, IP0OutboxTransport transport)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public async Task<P0DispatchSummary> DispatchPendingAsync(
        CancellationToken cancellationToken = default)
    {
        // GetPendingAsync materializes rows and disposes its SQLite connection before this method
        // invokes transport. Network work is therefore outside the local SQLite transaction boundary.
        IReadOnlyList<P0OutboxItem> pending =
            await _store.GetPendingAsync(cancellationToken).ConfigureAwait(false);

        int acknowledged = 0;
        int failed = 0;

        foreach (P0OutboxItem item in pending)
        {
            P0TransportResult result =
                await _transport.SendAsync(item, cancellationToken).ConfigureAwait(false);

            if (result.Acknowledged)
            {
                bool changed =
                    await _store.MarkAcknowledgedAsync(item.OperationId, cancellationToken).ConfigureAwait(false);
                if (changed)
                {
                    acknowledged++;
                }
            }
            else
            {
                await _store.RecordAttemptFailureAsync(
                    item.OperationId,
                    result.Message,
                    cancellationToken).ConfigureAwait(false);
                failed++;
            }
        }

        return new P0DispatchSummary(pending.Count, acknowledged, failed);
    }
}

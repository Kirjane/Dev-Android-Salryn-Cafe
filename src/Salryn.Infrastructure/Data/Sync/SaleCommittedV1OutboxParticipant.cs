using Salryn.Infrastructure.Data.Transactions;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public interface ISaleCommittedV1OutboxParticipant
{
    SyncOperationEnvelopeV1 Enqueue(
        SqliteSharedTransactionContext context,
        SyncEnqueueRequest request);
}

public sealed class SaleCommittedV1OutboxParticipant : ISaleCommittedV1OutboxParticipant
{
    private readonly SqliteSyncOutboxStore _outbox;

    public SaleCommittedV1OutboxParticipant(SqliteSyncOutboxStore outbox)
    {
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
    }

    public SyncOperationEnvelopeV1 Enqueue(
        SqliteSharedTransactionContext context,
        SyncEnqueueRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);

        return _outbox.Enqueue(
            context.Connection,
            context.Transaction,
            request);
    }
}

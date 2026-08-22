using Salryn.Core.Common;
using Salryn.Core.Repositories;
using Salryn.Infrastructure.Data.Transactions;

namespace Salryn.Infrastructure.Data.Services;

// Kirch: why - bridges the Core append port to the shared SQLite transaction boundary without owning SQL or output.
public sealed class SqliteElectronicJournalAppendAtomicPersistenceAdapter : IElectronicJournalAppendAtomicPersistencePort
{
    private readonly ISqliteSharedTransactionBoundary _transactionBoundary;
    private readonly ISqliteSharedTransactionParticipant<ElectronicJournalAppendPersistenceRequest, ElectronicJournalAppendPersistenceResult> _appendParticipant;

    public SqliteElectronicJournalAppendAtomicPersistenceAdapter(
        ISqliteSharedTransactionBoundary transactionBoundary,
        ISqliteSharedTransactionParticipant<ElectronicJournalAppendPersistenceRequest, ElectronicJournalAppendPersistenceResult> appendParticipant)
    {
        _transactionBoundary = transactionBoundary ?? throw new ArgumentNullException(nameof(transactionBoundary));
        _appendParticipant = appendParticipant ?? throw new ArgumentNullException(nameof(appendParticipant));
    }

    public Task<Result<ElectronicJournalAppendPersistenceResult>> CommitOriginalAppendAsync(
        ElectronicJournalAppendPersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        return _transactionBoundary.ExecuteAsync<ElectronicJournalAppendPersistenceResult>(
            (context, token) => _appendParticipant.ExecuteAsync(context, request, token),
            cancellationToken);
    }
}

using Salryn.Core.Common;
using Salryn.Core.Repositories;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data.Transactions;

namespace Salryn.Infrastructure.Data.Services;

public sealed record SqliteElectronicJournalAppendServiceCompositionRequest(
    SqliteConnectionFactory ConnectionFactory,
    ElectronicJournalAppendContractValidator? Validator = null,
    Func<Guid>? JournalEntryIdFactory = null,
    Func<DateTimeOffset>? Clock = null);

// Kirch: why - composes the append service from SQLite pieces without app startup, checkout, renderer, or output wiring.
public static class SqliteElectronicJournalAppendServiceComposition
{
    public static Result<IElectronicJournalAppendService> Create(
        SqliteElectronicJournalAppendServiceCompositionRequest? request)
    {
        if (request is null)
        {
            return Result<IElectronicJournalAppendService>.Fail(Error.Validation(
                "ElectronicJournalAppend.SqliteCompositionRequestRequired",
                "SQLite electronic journal append service composition request is required."));
        }

        if (request.ConnectionFactory is null)
        {
            return Result<IElectronicJournalAppendService>.Fail(Error.Validation(
                "ElectronicJournalAppend.SqliteConnectionFactoryRequired",
                "SQLite connection factory is required."));
        }

        ISqliteSharedTransactionBoundary transactionBoundary = new SqliteSharedTransactionBoundary(
            request.ConnectionFactory);

        ISqliteSharedTransactionParticipant<ElectronicJournalAppendPersistenceRequest, ElectronicJournalAppendPersistenceResult> appendParticipant =
            new SqliteElectronicJournalAppendParticipant();

        IElectronicJournalAppendAtomicPersistencePort atomicPersistencePort =
            new SqliteElectronicJournalAppendAtomicPersistenceAdapter(
                transactionBoundary,
                appendParticipant);

        return ElectronicJournalAppendServiceRegistrationBoundary.Create(
            new ElectronicJournalAppendServiceRegistrationRequest(
                AtomicPersistencePort: atomicPersistencePort,
                Validator: request.Validator,
                JournalEntryIdFactory: request.JournalEntryIdFactory,
                Clock: request.Clock));
    }
}

using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Repositories;

public sealed record ElectronicJournalAppendPersistenceRequest(
    Guid JournalEntryId,
    ElectronicJournalAppendContract Contract,
    DateTimeOffset AppendedAt);

public sealed record ElectronicJournalAppendPersistenceResult(
    Guid JournalEntryId,
    Guid OriginalEventId,
    string OfficialDocumentNumber,
    DateTimeOffset AppendedAt);

// Kirch: why - Infrastructure must persist the journal anchor and ORIGINAL event through a Core-owned port.
public interface IElectronicJournalAppendPersistencePort
{
    Task<Result<ElectronicJournalAppendPersistenceResult>> AppendOriginalAsync(
        ElectronicJournalAppendPersistenceRequest request,
        CancellationToken cancellationToken = default);
}

// Kirch: why - the later SQLite implementation must commit all append evidence as one unit.
public interface IElectronicJournalAppendAtomicPersistencePort
{
    Task<Result<ElectronicJournalAppendPersistenceResult>> CommitOriginalAppendAsync(
        ElectronicJournalAppendPersistenceRequest request,
        CancellationToken cancellationToken = default);
}

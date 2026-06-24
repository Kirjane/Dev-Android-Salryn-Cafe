using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Repositories;

namespace Salryn.Core.Services;

// Kirch: why - coordinates validation and the atomic persistence port without owning storage or output.
public sealed class ElectronicJournalAppendService : IElectronicJournalAppendService
{
    private readonly IElectronicJournalAppendAtomicPersistencePort _persistencePort;
    private readonly ElectronicJournalAppendContractValidator _validator;
    private readonly Func<Guid> _journalEntryIdFactory;
    private readonly Func<DateTimeOffset> _clock;

    public ElectronicJournalAppendService(
        IElectronicJournalAppendAtomicPersistencePort persistencePort,
        ElectronicJournalAppendContractValidator? validator = null,
        Func<Guid>? journalEntryIdFactory = null,
        Func<DateTimeOffset>? clock = null)
    {
        _persistencePort = persistencePort ?? throw new ArgumentNullException(nameof(persistencePort));
        _validator = validator ?? new ElectronicJournalAppendContractValidator();
        _journalEntryIdFactory = journalEntryIdFactory ?? Guid.NewGuid;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<Result<ElectronicJournalAppendResult>> AppendOriginalAsync(
        ElectronicJournalAppendContract contract,
        CancellationToken cancellationToken = default)
    {
        Result validation = _validator.Validate(contract);

        if (validation.IsFailure)
        {
            return Result<ElectronicJournalAppendResult>.Fail(validation.ErrorDetail);
        }

        Guid journalEntryId = _journalEntryIdFactory();

        if (journalEntryId == Guid.Empty)
        {
            return Result<ElectronicJournalAppendResult>.Fail(Error.Unexpected(
                "ElectronicJournalAppend.JournalEntryIdRequired",
                "Generated electronic journal entry id must not be empty."));
        }

        DateTimeOffset appendedAt = _clock();

        if (appendedAt == default)
        {
            return Result<ElectronicJournalAppendResult>.Fail(Error.Unexpected(
                "ElectronicJournalAppend.AppendedAtRequired",
                "Electronic journal append timestamp is required."));
        }

        ElectronicJournalAppendPersistenceRequest request = new(
            JournalEntryId: journalEntryId,
            Contract: contract,
            AppendedAt: appendedAt);

        Result<ElectronicJournalAppendPersistenceResult> persistenceResult =
            await _persistencePort.CommitOriginalAppendAsync(request, cancellationToken);

        if (persistenceResult.IsFailure)
        {
            return Result<ElectronicJournalAppendResult>.Fail(persistenceResult.ErrorDetail);
        }

        ElectronicJournalAppendPersistenceResult persisted = persistenceResult.Value;

        return Result<ElectronicJournalAppendResult>.Success(new ElectronicJournalAppendResult(
            JournalEntryId: persisted.JournalEntryId,
            OriginalEventId: persisted.OriginalEventId,
            OfficialDocumentNumber: persisted.OfficialDocumentNumber,
            AppendedAt: persisted.AppendedAt));
    }
}

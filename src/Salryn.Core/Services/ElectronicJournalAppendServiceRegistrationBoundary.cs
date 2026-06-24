using Salryn.Core.Common;
using Salryn.Core.Repositories;

namespace Salryn.Core.Services;

public sealed record ElectronicJournalAppendServiceRegistrationRequest(
    IElectronicJournalAppendAtomicPersistencePort AtomicPersistencePort,
    ElectronicJournalAppendContractValidator? Validator = null,
    Func<Guid>? JournalEntryIdFactory = null,
    Func<DateTimeOffset>? Clock = null);

// Kirch: why - lets app composition create the append service through Core ports only.
public static class ElectronicJournalAppendServiceRegistrationBoundary
{
    public static Result<IElectronicJournalAppendService> Create(
        ElectronicJournalAppendServiceRegistrationRequest? request)
    {
        if (request is null)
        {
            return Result<IElectronicJournalAppendService>.Fail(Error.Validation(
                "ElectronicJournalAppend.RegistrationRequestRequired",
                "Electronic journal append service registration request is required."));
        }

        if (request.AtomicPersistencePort is null)
        {
            return Result<IElectronicJournalAppendService>.Fail(Error.Validation(
                "ElectronicJournalAppend.AtomicPersistencePortRequired",
                "Electronic journal append atomic persistence port is required."));
        }

        IElectronicJournalAppendService service = new ElectronicJournalAppendService(
            persistencePort: request.AtomicPersistencePort,
            validator: request.Validator,
            journalEntryIdFactory: request.JournalEntryIdFactory,
            clock: request.Clock);

        return Result<IElectronicJournalAppendService>.Success(service);
    }
}

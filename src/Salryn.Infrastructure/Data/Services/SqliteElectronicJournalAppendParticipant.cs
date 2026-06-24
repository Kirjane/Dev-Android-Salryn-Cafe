using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Repositories;
using Salryn.Infrastructure.Data.Transactions;

namespace Salryn.Infrastructure.Data.Services;

// Kirch: why - reserves the shared-transaction journal append adapter without claiming persistence is implemented.
public sealed class SqliteElectronicJournalAppendParticipant :
    ISqliteSharedTransactionParticipant<ElectronicJournalAppendPersistenceRequest, ElectronicJournalAppendPersistenceResult>
{
    public Task<Result<ElectronicJournalAppendPersistenceResult>> ExecuteAsync(
        SqliteSharedTransactionContext context,
        ElectronicJournalAppendPersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (context is null)
        {
            return Task.FromResult(Result<ElectronicJournalAppendPersistenceResult>.Fail(Error.Validation(
                "ElectronicJournalAppend.SqliteContextRequired",
                "Shared SQLite transaction context is required.")));
        }

        cancellationToken.ThrowIfCancellationRequested();

        Result validation = ValidateRequest(request);

        if (validation.IsFailure)
        {
            return Task.FromResult(Result<ElectronicJournalAppendPersistenceResult>.Fail(validation.ErrorDetail));
        }

        return Task.FromResult(Result<ElectronicJournalAppendPersistenceResult>.Fail(Error.Failure(
            "ElectronicJournalAppend.SqliteParticipantNotImplemented",
            "SQLite electronic journal append participant is registered as a skeleton only. Journal entry and ORIGINAL event inserts are deferred to the approved persistence phase.")));
    }

    private static Result ValidateRequest(ElectronicJournalAppendPersistenceRequest? request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.PersistenceRequestRequired",
                "Electronic journal append persistence request is required."));
        }

        if (request.JournalEntryId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.JournalEntryIdRequired",
                "Electronic journal entry id is required."));
        }

        if (request.AppendedAt == default)
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.AppendedAtRequired",
                "Electronic journal append timestamp is required."));
        }

        if (request.Contract is null)
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.ContractRequired",
                "Electronic journal append contract is required."));
        }

        ElectronicJournalAppendContract contract = request.Contract;

        if (contract.BirMachineProfileId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.MachineProfileRequired",
                "Machine profile id is required."));
        }

        if (contract.SaleId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.SaleRequired",
                "Sale id is required."));
        }

        if (contract.OfficialDocumentSequenceId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.SequenceRequired",
                "Official document sequence id is required."));
        }

        if (string.IsNullOrWhiteSpace(contract.OfficialDocumentNumber))
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.OfficialDocumentNumberRequired",
                "Official document number is required."));
        }

        if (contract.CashierUserId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.CashierRequired",
                "Cashier user id is required."));
        }

        if (contract.OriginalEvent is null)
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.OriginalEventRequired",
                "ORIGINAL event contract is required."));
        }

        if (contract.OriginalEvent.EventId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.OriginalEventIdRequired",
                "ORIGINAL event id is required."));
        }

        if (contract.OriginalEvent.CashierUserId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.OriginalEventCashierRequired",
                "ORIGINAL event cashier id is required."));
        }

        if (contract.SnapshotEvidence is not null)
        {
            return ValidateSnapshotEvidence(contract.SnapshotEvidence);
        }

        return Result.Success();
    }

    private static Result ValidateSnapshotEvidence(ElectronicJournalSnapshotEvidence snapshotEvidence)
    {
        if (string.IsNullOrWhiteSpace(snapshotEvidence.PayloadJson))
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.SnapshotPayloadRequired",
                "Snapshot payload JSON is required when snapshot evidence is supplied."));
        }

        if (string.IsNullOrWhiteSpace(snapshotEvidence.PayloadHash))
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.SnapshotHashRequired",
                "Snapshot payload hash is required when snapshot evidence is supplied."));
        }

        if (string.IsNullOrWhiteSpace(snapshotEvidence.HashAlgorithm))
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.SnapshotHashAlgorithmRequired",
                "Snapshot hash algorithm is required when snapshot evidence is supplied."));
        }

        return Result.Success();
    }

    internal static string ToDatabaseDocumentKind(ElectronicJournalDocumentKind documentKind)
    {
        return documentKind switch
        {
            ElectronicJournalDocumentKind.OfficialReceipt => "OFFICIAL_RECEIPT",
            ElectronicJournalDocumentKind.OfficialInvoice => "OFFICIAL_INVOICE",
            _ => string.Empty
        };
    }

    internal const string OriginalEventType = "ORIGINAL";
}

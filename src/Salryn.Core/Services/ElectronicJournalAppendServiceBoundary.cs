using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Services;

public sealed record ElectronicJournalAppendResult(
    Guid JournalEntryId,
    Guid OriginalEventId,
    string OfficialDocumentNumber,
    DateTimeOffset AppendedAt);

// Kirch: J21-5G names the future append service boundary without adding persistence or checkout behavior.
public interface IElectronicJournalAppendService
{
    Task<Result<ElectronicJournalAppendResult>> AppendOriginalAsync(
        ElectronicJournalAppendContract contract,
        CancellationToken cancellationToken = default);
}

// Kirch: Validates append contract shape before any database append service is allowed.
public sealed class ElectronicJournalAppendContractValidator
{
    public Result Validate(ElectronicJournalAppendContract? contract)
    {
        if (contract is null)
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.ContractRequired",
                "Electronic journal append contract is required."));
        }

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

        if (contract.AtomicBoundarySteps is null || contract.AtomicBoundarySteps.Count == 0)
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.AtomicBoundaryRequired",
                "Atomic append boundary steps are required."));
        }

        if (contract.SnapshotEvidence is not null)
        {
            Result snapshotValidation = ValidateSnapshotEvidence(contract.SnapshotEvidence);

            if (snapshotValidation.IsFailure)
            {
                return snapshotValidation;
            }
        }

        Result boundaryValidation = ElectronicJournalAppendAtomicBoundaryRules.Validate(contract);

        if (boundaryValidation.IsFailure)
        {
            return boundaryValidation;
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
}

using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Services;

public sealed record OfficialDocumentJournalAppendContractBuildRequest(
    Guid BirMachineProfileId,
    Guid SaleId,
    Guid OfficialDocumentSequenceId,
    string OfficialDocumentNumber,
    OfficialDocumentSnapshotDraft SnapshotDraft,
    Guid CashierUserId,
    string? TerminalId,
    Guid OriginalEventId,
    DateTimeOffset OriginalEventCreatedAt);

// Kirch: why - official checkout needs one validated handoff from snapshot evidence into journal append.
public sealed class OfficialDocumentJournalAppendContractBuilder
{
    private readonly OfficialDocumentSnapshotEvidenceBuilder _snapshotEvidenceBuilder;

    public OfficialDocumentJournalAppendContractBuilder(
        OfficialDocumentSnapshotEvidenceBuilder? snapshotEvidenceBuilder = null)
    {
        _snapshotEvidenceBuilder = snapshotEvidenceBuilder ?? new OfficialDocumentSnapshotEvidenceBuilder();
    }

    public Result<ElectronicJournalAppendContract> Build(
        OfficialDocumentJournalAppendContractBuildRequest request)
    {
        Result validation = ValidateRequest(request);

        if (validation.IsFailure)
        {
            return Result<ElectronicJournalAppendContract>.Fail(validation.ErrorDetail);
        }

        Result<ElectronicJournalSnapshotEvidence> evidenceResult =
            _snapshotEvidenceBuilder.Build(request.SnapshotDraft);

        if (evidenceResult.IsFailure)
        {
            return Result<ElectronicJournalAppendContract>.Fail(evidenceResult.ErrorDetail);
        }

        ElectronicJournalAppendContract contract = new(
            BirMachineProfileId: request.BirMachineProfileId,
            SaleId: request.SaleId,
            OfficialDocumentSequenceId: request.OfficialDocumentSequenceId,
            OfficialDocumentNumber: request.OfficialDocumentNumber.Trim(),
            DocumentKind: MapDocumentKind(request.SnapshotDraft.DocumentKind),
            TransactionTimestamp: request.SnapshotDraft.Header.TransactionDateTime,
            CashierUserId: request.CashierUserId,
            TerminalId: NormalizeOptional(request.TerminalId),
            SnapshotEvidence: evidenceResult.Value,
            OriginalEvent: new ElectronicJournalOriginalEventContract(
                EventId: request.OriginalEventId,
                CashierUserId: request.CashierUserId,
                CreatedAt: request.OriginalEventCreatedAt),
            AtomicBoundarySteps: new[]
            {
                ElectronicJournalAtomicBoundaryStep.OfficialSequenceAllocation,
                ElectronicJournalAtomicBoundaryStep.SnapshotPayloadHashPersistence,
                ElectronicJournalAtomicBoundaryStep.JournalAnchorInsert,
                ElectronicJournalAtomicBoundaryStep.OriginalEventInsert
            },
            DeferredComplianceNotes: new[]
            {
                "Official document journal append contract builder only.",
                "No checkout sale persistence.",
                "No printer or official output claim.",
                "No X/Z reading generation."
            });

        ElectronicJournalAppendContractValidator validator = new();
        Result contractValidation = validator.Validate(contract);

        return contractValidation.IsFailure
            ? Result<ElectronicJournalAppendContract>.Fail(contractValidation.ErrorDetail)
            : Result<ElectronicJournalAppendContract>.Success(contract);
    }

    private static Result ValidateRequest(OfficialDocumentJournalAppendContractBuildRequest? request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentJournalContract.RequestRequired",
                "Official document journal contract build request is required."));
        }

        if (request.BirMachineProfileId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentJournalContract.MachineProfileRequired",
                "Machine profile id is required."));
        }

        if (request.SaleId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentJournalContract.SaleRequired",
                "Sale id is required."));
        }

        if (request.OfficialDocumentSequenceId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentJournalContract.SequenceRequired",
                "Official document sequence id is required."));
        }

        if (string.IsNullOrWhiteSpace(request.OfficialDocumentNumber))
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentJournalContract.DocumentNumberRequired",
                "Official document number is required."));
        }

        if (request.SnapshotDraft is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentJournalContract.SnapshotDraftRequired",
                "Official document snapshot draft is required."));
        }

        if (request.SnapshotDraft.Header is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentJournalContract.SnapshotHeaderRequired",
                "Official document snapshot header is required."));
        }

        if (!string.Equals(
            request.OfficialDocumentNumber.Trim(),
            request.SnapshotDraft.Header.DocumentNumber?.Trim(),
            StringComparison.Ordinal))
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentJournalContract.DocumentNumberMismatch",
                "Official document number must match the snapshot header document number."));
        }

        if (request.CashierUserId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentJournalContract.CashierRequired",
                "Cashier user id is required."));
        }

        if (request.OriginalEventId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentJournalContract.OriginalEventRequired",
                "ORIGINAL journal event id is required."));
        }

        if (request.OriginalEventCreatedAt == default)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentJournalContract.OriginalEventCreatedAtRequired",
                "ORIGINAL journal event timestamp is required."));
        }

        return Result.Success();
    }

    private static ElectronicJournalDocumentKind MapDocumentKind(
        OfficialDocumentSnapshotDraftKind documentKind)
    {
        return documentKind switch
        {
            OfficialDocumentSnapshotDraftKind.OfficialReceipt => ElectronicJournalDocumentKind.OfficialReceipt,
            OfficialDocumentSnapshotDraftKind.OfficialInvoice => ElectronicJournalDocumentKind.OfficialInvoice,
            _ => throw new InvalidOperationException("Unsupported official document snapshot kind.")
        };
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}

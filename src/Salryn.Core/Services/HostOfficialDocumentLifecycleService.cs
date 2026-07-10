using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Official;
using Salryn.Core.Services.InvoiceOutput;

namespace Salryn.Core.Services;

public enum OfficialDocumentLifecycleAction
{
    Reprint = 1,
    Void = 2,
    Refund = 3,
    Cancel = 4
}

public sealed record OfficialDocumentLifecycleActionRequest(
    OfficialDocumentLifecycleAction Action,
    Guid? JournalEntryId,
    string? OfficialDocumentNumber,
    Guid? SaleId,
    string Reason,
    Guid ActorUserId,
    string? TerminalId);

public sealed record OfficialDocumentLifecycleActionResult(
    OfficialDocumentLifecycleAction Action,
    Guid EventId,
    Guid JournalEntryId,
    string OfficialDocumentNumber,
    string Reason,
    Guid ActorUserId,
    DateTimeOffset CreatedAt,
    bool SequenceAdvanced,
    bool OriginalImmutable,
    string Summary,
    InvoiceOutputPackage? ReprintOutput);

public interface IHostOfficialDocumentLifecycleService
{
    Task<Result<OfficialDocumentLifecycleActionResult>> ExecuteAsync(
        OfficialDocumentLifecycleActionRequest request,
        CancellationToken cancellationToken = default);

    Result<OfficialDocumentLifecycleStatus> GetStatus();
}

public sealed record OfficialDocumentLifecycleStatus(
    bool ProductionActivationEnabled,
    bool LiveOfficialPostingAllowed,
    bool ReprintImplemented,
    bool VoidRefundEventsImplemented,
    bool AdjustmentSeriesAllocationImplemented,
    string Summary,
    IReadOnlyList<string> Notes);

/// <summary>
/// Host lifecycle facade: reprint uses existing append-only path; void/refund append markers only.
/// Never rewrites original official documents. Never invents accreditation.
/// </summary>
public sealed class HostOfficialDocumentLifecycleService : IHostOfficialDocumentLifecycleService
{
    public const int MaxReasonLength = 500;

    private readonly IOfficialDocumentReprintService? _reprintService;
    private readonly IOfficialDocumentLifecycleMutation? _mutation;
    private readonly IInvoiceOutputService? _invoiceOutput;

    public HostOfficialDocumentLifecycleService(
        IOfficialDocumentReprintService? reprintService = null,
        IOfficialDocumentLifecycleMutation? mutation = null,
        IInvoiceOutputService? invoiceOutput = null)
    {
        _reprintService = reprintService;
        _mutation = mutation;
        _invoiceOutput = invoiceOutput;
    }

    public Result<OfficialDocumentLifecycleStatus> GetStatus()
    {
        return Result<OfficialDocumentLifecycleStatus>.Success(new OfficialDocumentLifecycleStatus(
            ProductionActivationEnabled: ProductionOfficialPostingActivation.IsEnabled,
            LiveOfficialPostingAllowed: false,
            ReprintImplemented: _reprintService is not null,
            VoidRefundEventsImplemented: _mutation is not null,
            AdjustmentSeriesAllocationImplemented: false,
            Summary: "Host official-document lifecycle facade (engineering). M4-RC1 completion path applies operational sale/stock effects + append-only EJ. Production activation remains OFF.",
            Notes: new[]
            {
                "Original official documents remain immutable.",
                "Reprint does not allocate a new official sales number.",
                "Void/refund apply sale/stock effects and append lifecycle events; adjustment series allocation is not productized in M4-RC1.",
                "Actor must come from authenticated host session; client-supplied actor is rejected.",
                OfficialSafetyConstants.NoOfficialNumberIssued,
                ProductionOfficialPostingActivation.NotAComplianceClaimNote
            }));
    }

    public async Task<Result<OfficialDocumentLifecycleActionResult>> ExecuteAsync(
        OfficialDocumentLifecycleActionRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request is null)
        {
            return Result<OfficialDocumentLifecycleActionResult>.Fail(Error.Validation(
                "DocumentLifecycle.RequestRequired",
                "Lifecycle action request is required."));
        }

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > MaxReasonLength)
        {
            return Result<OfficialDocumentLifecycleActionResult>.Fail(Error.Validation(
                "DocumentLifecycle.ReasonRequired",
                "A non-empty reason (max 500 chars) is required."));
        }

        if (request.ActorUserId == Guid.Empty)
        {
            return Result<OfficialDocumentLifecycleActionResult>.Fail(Error.Validation(
                "DocumentLifecycle.ActorRequired",
                "Actor user id is required."));
        }

        return request.Action switch
        {
            OfficialDocumentLifecycleAction.Reprint => await ExecuteReprintAsync(request, cancellationToken)
                .ConfigureAwait(false),
            OfficialDocumentLifecycleAction.Void => await ExecuteMutationAsync(
                request, OfficialDocumentLifecycleAction.Void, "VOID", cancellationToken).ConfigureAwait(false),
            OfficialDocumentLifecycleAction.Refund => await ExecuteMutationAsync(
                request, OfficialDocumentLifecycleAction.Refund, "REFUND", cancellationToken).ConfigureAwait(false),
            OfficialDocumentLifecycleAction.Cancel => await ExecuteMutationAsync(
                request, OfficialDocumentLifecycleAction.Cancel, "CANCEL", cancellationToken).ConfigureAwait(false),
            _ => Result<OfficialDocumentLifecycleActionResult>.Fail(Error.Validation(
                "DocumentLifecycle.UnknownAction",
                "Unknown lifecycle action."))
        };
    }

    private async Task<Result<OfficialDocumentLifecycleActionResult>> ExecuteReprintAsync(
        OfficialDocumentLifecycleActionRequest request,
        CancellationToken cancellationToken)
    {
        if (_reprintService is null)
        {
            return Result<OfficialDocumentLifecycleActionResult>.Fail(Error.Forbidden(
                "DocumentLifecycle.ReprintNotComposed",
                "Reprint service is not composed."));
        }

        Result<OfficialDocumentReprintResult> reprint = await _reprintService.AppendAsync(
            new OfficialDocumentReprintRequest(
                request.JournalEntryId,
                request.OfficialDocumentNumber,
                DocumentKind: null,
                BirMachineProfileId: null,
                RegistrationScopeCode: null,
                request.Reason.Trim(),
                request.ActorUserId,
                request.TerminalId),
            cancellationToken).ConfigureAwait(false);

        if (reprint.IsFailure)
        {
            return Result<OfficialDocumentLifecycleActionResult>.Fail(reprint.ErrorDetail);
        }

        InvoiceOutputPackage? output = null;
        if (_invoiceOutput is not null && request.SaleId is Guid saleId && saleId != Guid.Empty)
        {
            Result<InvoiceOutputPackage> package = await _invoiceOutput.GenerateAsync(
                new InvoiceOutputRequest(
                    saleId,
                    reprint.Value.OfficialDocumentNumber,
                    InvoiceOutputMode.InternalDemo,
                    new[] { InvoiceOutputFormat.Html, InvoiceOutputFormat.ThermalText }),
                cancellationToken).ConfigureAwait(false);

            if (package.IsSuccess)
            {
                // Surface reprint marker via boundary notes without mutating snapshot hash identity.
                output = package.Value with
                {
                    Model = package.Value.Model with
                    {
                        BoundaryNotes = package.Value.Model.BoundaryNotes
                            .Concat(new[] { "REPRINT — same official number; no sequence advance." })
                            .ToArray(),
                        RequiresInternalWatermark = true,
                        InternalWatermark = InvoiceOutputConstants.InternalWatermark
                    }
                };
            }
        }

        return Result<OfficialDocumentLifecycleActionResult>.Success(new OfficialDocumentLifecycleActionResult(
            OfficialDocumentLifecycleAction.Reprint,
            reprint.Value.EventId,
            reprint.Value.JournalEntryId,
            reprint.Value.OfficialDocumentNumber,
            reprint.Value.Reason,
            reprint.Value.ActorUserId,
            reprint.Value.CreatedAt,
            SequenceAdvanced: false,
            OriginalImmutable: true,
            Summary: "Reprint event appended. Original document immutable. No new official sales number allocated.",
            ReprintOutput: output));
    }

    private async Task<Result<OfficialDocumentLifecycleActionResult>> ExecuteMutationAsync(
        OfficialDocumentLifecycleActionRequest request,
        OfficialDocumentLifecycleAction action,
        string eventType,
        CancellationToken cancellationToken)
    {
        if (_mutation is null)
        {
            return Result<OfficialDocumentLifecycleActionResult>.Fail(Error.Forbidden(
                "DocumentLifecycle.MutationNotComposed",
                "Lifecycle mutation service is not composed."));
        }

        Result<OfficialDocumentLifecycleMutationResult> mutation = await _mutation.AppendAsync(
            new OfficialDocumentLifecycleMutationRequest(
                eventType,
                request.JournalEntryId,
                request.OfficialDocumentNumber,
                request.Reason.Trim(),
                request.ActorUserId,
                request.TerminalId),
            cancellationToken).ConfigureAwait(false);

        if (mutation.IsFailure)
        {
            return Result<OfficialDocumentLifecycleActionResult>.Fail(mutation.ErrorDetail);
        }

        return Result<OfficialDocumentLifecycleActionResult>.Success(new OfficialDocumentLifecycleActionResult(
            action,
            mutation.Value.EventId,
            mutation.Value.JournalEntryId,
            mutation.Value.OfficialDocumentNumber,
            mutation.Value.Reason,
            mutation.Value.ActorUserId,
            mutation.Value.CreatedAt,
            SequenceAdvanced: false,
            OriginalImmutable: true,
            Summary: eventType + " lifecycle event appended. Original official document remains immutable. Sales sequence not advanced.",
            ReprintOutput: null));
    }
}

public sealed record OfficialDocumentLifecycleMutationRequest(
    string EventType,
    Guid? JournalEntryId,
    string? OfficialDocumentNumber,
    string Reason,
    Guid ActorUserId,
    string? TerminalId);

public sealed record OfficialDocumentLifecycleMutationResult(
    Guid EventId,
    Guid JournalEntryId,
    string OfficialDocumentNumber,
    string Reason,
    Guid ActorUserId,
    DateTimeOffset CreatedAt);

public interface IOfficialDocumentLifecycleMutation
{
    Task<Result<OfficialDocumentLifecycleMutationResult>> AppendAsync(
        OfficialDocumentLifecycleMutationRequest request,
        CancellationToken cancellationToken = default);
}

using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Repositories;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data.Transactions;

namespace Salryn.Infrastructure.Data.Services;

public sealed record SqliteOfficialCheckoutPersistenceRequest(
    CompleteCheckoutSaleRequest CheckoutSale,
    OfficialDocumentSequenceAllocationRequest SequenceRequest,
    OfficialDocumentSnapshotDraftMachineProfileInput MachineProfile,
    OfficialDocumentSnapshotDraftKind DocumentKind,
    string CashierIdentifier,
    string? TerminalIdentifier,
    string TaxModeCode,
    string? TaxModeNotes);

public sealed record SqliteOfficialCheckoutPersistenceResult(
    Guid SaleId,
    string OfficialDocumentNumber,
    Guid JournalEntryId,
    Guid OriginalEventId,
    DateTimeOffset AppendedAt);

// Kirch: why - official checkout persistence must commit sale, sequence, snapshot hash, and journal together.
public sealed class SqliteOfficialCheckoutPersistenceService
{
    private readonly ISqliteSharedTransactionBoundary _transactionBoundary;
    private readonly SharedSqliteOfficialDocumentSequenceAllocationParticipant _sequenceParticipant;
    private readonly SharedSqliteCompletedSaleWriteParticipant _saleParticipant;
    private readonly SqliteElectronicJournalAppendParticipant _journalParticipant;
    private readonly IOfficialCheckoutReadinessService _readinessService;
    private readonly OfficialDocumentSnapshotDraftBuilder _snapshotDraftBuilder;
    private readonly OfficialDocumentJournalAppendContractBuilder _journalContractBuilder;
    private readonly Func<Guid> _journalEntryIdFactory;
    private readonly Func<Guid> _originalEventIdFactory;
    private readonly Func<DateTimeOffset> _clock;

    public SqliteOfficialCheckoutPersistenceService(
        SqliteConnectionFactory connectionFactory)
        : this(
            new SqliteSharedTransactionBoundary(connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory))),
            new SharedSqliteOfficialDocumentSequenceAllocationParticipant(),
            new SharedSqliteCompletedSaleWriteParticipant(),
            new SqliteElectronicJournalAppendParticipant(),
            new SqliteOfficialCheckoutReadinessService(connectionFactory),
            new OfficialDocumentSnapshotDraftBuilder(),
            new OfficialDocumentJournalAppendContractBuilder(),
            Guid.NewGuid,
            Guid.NewGuid,
            () => DateTimeOffset.UtcNow)
    {
    }

    internal SqliteOfficialCheckoutPersistenceService(
        ISqliteSharedTransactionBoundary transactionBoundary,
        SharedSqliteOfficialDocumentSequenceAllocationParticipant sequenceParticipant,
        SharedSqliteCompletedSaleWriteParticipant saleParticipant,
        SqliteElectronicJournalAppendParticipant journalParticipant,
        IOfficialCheckoutReadinessService readinessService,
        OfficialDocumentSnapshotDraftBuilder snapshotDraftBuilder,
        OfficialDocumentJournalAppendContractBuilder journalContractBuilder,
        Func<Guid> journalEntryIdFactory,
        Func<Guid> originalEventIdFactory,
        Func<DateTimeOffset> clock)
    {
        _transactionBoundary = transactionBoundary ?? throw new ArgumentNullException(nameof(transactionBoundary));
        _sequenceParticipant = sequenceParticipant ?? throw new ArgumentNullException(nameof(sequenceParticipant));
        _saleParticipant = saleParticipant ?? throw new ArgumentNullException(nameof(saleParticipant));
        _journalParticipant = journalParticipant ?? throw new ArgumentNullException(nameof(journalParticipant));
        _readinessService = readinessService ?? throw new ArgumentNullException(nameof(readinessService));
        _snapshotDraftBuilder = snapshotDraftBuilder ?? throw new ArgumentNullException(nameof(snapshotDraftBuilder));
        _journalContractBuilder = journalContractBuilder ?? throw new ArgumentNullException(nameof(journalContractBuilder));
        _journalEntryIdFactory = journalEntryIdFactory ?? throw new ArgumentNullException(nameof(journalEntryIdFactory));
        _originalEventIdFactory = originalEventIdFactory ?? throw new ArgumentNullException(nameof(originalEventIdFactory));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<Result<SqliteOfficialCheckoutPersistenceResult>> CompleteOfficialSaleAsync(
        SqliteOfficialCheckoutPersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        Result validation = ValidateRequest(request);

        if (validation.IsFailure)
        {
            return Result<SqliteOfficialCheckoutPersistenceResult>.Fail(validation.ErrorDetail);
        }

        Result<OfficialCheckoutReadinessResult> readinessResult =
            await _readinessService.CheckAsync(
                new OfficialCheckoutReadinessRequest(
                    request.SequenceRequest.BirMachineProfileId,
                    request.SequenceRequest.DocumentKind,
                    request.SequenceRequest.SequenceScopeCode,
                    request.SequenceRequest.Year),
                cancellationToken);

        if (readinessResult.IsFailure)
        {
            return Result<SqliteOfficialCheckoutPersistenceResult>.Fail(readinessResult.ErrorDetail);
        }

        OfficialCheckoutReadinessResult readiness = readinessResult.Value;

        if (!readiness.IsReady || readiness.MachineProfile is null)
        {
            return Result<SqliteOfficialCheckoutPersistenceResult>.Fail(Error.Forbidden(
                "OfficialCheckout.ReadinessBlocked",
                BuildReadinessBlockedMessage(readiness)));
        }

        return await _transactionBoundary.ExecuteAsync<SqliteOfficialCheckoutPersistenceResult>(
            (context, token) => CompleteOfficialSaleInTransactionAsync(context, request, readiness.MachineProfile, token),
            cancellationToken);
    }

    private async Task<Result<SqliteOfficialCheckoutPersistenceResult>> CompleteOfficialSaleInTransactionAsync(
        SqliteSharedTransactionContext context,
        SqliteOfficialCheckoutPersistenceRequest request,
        OfficialDocumentSnapshotDraftMachineProfileInput trustedMachineProfile,
        CancellationToken cancellationToken)
    {
        Result<OfficialDocumentSequenceAllocation> allocationResult =
            await _sequenceParticipant.ExecuteAsync(
                context,
                request.SequenceRequest,
                cancellationToken);

        if (allocationResult.IsFailure)
        {
            return Result<SqliteOfficialCheckoutPersistenceResult>.Fail(allocationResult.ErrorDetail);
        }

        OfficialDocumentSequenceAllocation allocation = allocationResult.Value;
        Sale officialSale = CloneSaleWithOfficialDocumentNumber(
            request.CheckoutSale.Sale,
            allocation.FormattedDocumentNumber);

        Result<OfficialDocumentSnapshotDraft> snapshotResult =
            _snapshotDraftBuilder.Build(new OfficialDocumentSnapshotDraftBuildRequest(
                request.DocumentKind,
                // Kirch: why - official taxpayer and machine evidence must come from persisted readiness, not UI/client input.
                trustedMachineProfile,
                officialSale,
                request.CheckoutSale.Items,
                request.CheckoutSale.Payments,
                allocation.FormattedDocumentNumber,
                request.CashierIdentifier,
                request.TerminalIdentifier,
                request.TaxModeCode,
                request.TaxModeNotes));

        if (snapshotResult.IsFailure)
        {
            return Result<SqliteOfficialCheckoutPersistenceResult>.Fail(snapshotResult.ErrorDetail);
        }

        Guid originalEventId = _originalEventIdFactory();
        DateTimeOffset appendedAt = _clock();

        Result<ElectronicJournalAppendContract> contractResult =
            _journalContractBuilder.Build(new OfficialDocumentJournalAppendContractBuildRequest(
                allocation.BirMachineProfileId,
                officialSale.Id,
                allocation.SequenceId,
                allocation.FormattedDocumentNumber,
                snapshotResult.Value,
                officialSale.CashierUserId,
                request.TerminalIdentifier,
                originalEventId,
                appendedAt));

        if (contractResult.IsFailure)
        {
            return Result<SqliteOfficialCheckoutPersistenceResult>.Fail(contractResult.ErrorDetail);
        }

        CompleteCheckoutSaleRequest officialCheckoutSale = new(
            officialSale,
            request.CheckoutSale.Items,
            request.CheckoutSale.Payments,
            request.CheckoutSale.StockMovements);

        Result<SharedSqliteCompletedSaleWriteResult> saleResult =
            await _saleParticipant.ExecuteAsync(
                context,
                officialCheckoutSale,
                cancellationToken);

        if (saleResult.IsFailure)
        {
            return Result<SqliteOfficialCheckoutPersistenceResult>.Fail(saleResult.ErrorDetail);
        }

        Guid journalEntryId = _journalEntryIdFactory();

        ElectronicJournalAppendPersistenceRequest appendRequest = new(
            JournalEntryId: journalEntryId,
            Contract: contractResult.Value,
            AppendedAt: appendedAt);

        Result<ElectronicJournalAppendPersistenceResult> journalResult =
            await _journalParticipant.ExecuteAsync(context, appendRequest, cancellationToken);

        if (journalResult.IsFailure)
        {
            return Result<SqliteOfficialCheckoutPersistenceResult>.Fail(journalResult.ErrorDetail);
        }

        return Result<SqliteOfficialCheckoutPersistenceResult>.Success(
            new SqliteOfficialCheckoutPersistenceResult(
                officialSale.Id,
                allocation.FormattedDocumentNumber,
                journalEntryId,
                originalEventId,
                appendedAt));
    }

    private static Result ValidateRequest(SqliteOfficialCheckoutPersistenceRequest? request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialCheckout.PersistenceRequestRequired",
                "Official checkout persistence request is required."));
        }

        if (request.CheckoutSale is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialCheckout.CheckoutSaleRequired",
                "Official checkout persistence requires a checkout sale."));
        }

        if (request.CheckoutSale.Sale is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialCheckout.SaleRequired",
                "Official checkout persistence requires a sale."));
        }

        if (request.SequenceRequest is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialCheckout.SequenceRequestRequired",
                "Official checkout persistence requires a sequence allocation request."));
        }

        if (!string.Equals(
            NormalizeDocumentKind(request.SequenceRequest.DocumentKind),
            ToSequenceDocumentKind(request.DocumentKind),
            StringComparison.Ordinal))
        {
            return Result.Fail(Error.Validation(
                "OfficialCheckout.DocumentKindMismatch",
                "Official checkout sequence document kind must match snapshot document kind."));
        }

        if (request.CheckoutSale.Sale.CashierSessionId is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialCheckout.CashierSessionRequired",
                "Official checkout persistence requires a cashier session id already enforced by checkout."));
        }

        return Result.Success();
    }

    private static Sale CloneSaleWithOfficialDocumentNumber(
        Sale sale,
        string officialDocumentNumber)
    {
        return new Sale
        {
            Id = sale.Id,
            ReceiptNumber = officialDocumentNumber.Trim(),
            SaleDate = sale.SaleDate,
            CashierUserId = sale.CashierUserId,
            CashierSessionId = sale.CashierSessionId,
            Subtotal = sale.Subtotal,
            Discount = sale.Discount,
            Tax = sale.Tax,
            Total = sale.Total,
            AmountPaid = sale.AmountPaid,
            Change = sale.Change,
            Status = sale.Status,
            VoidedByUserId = sale.VoidedByUserId,
            VoidReason = sale.VoidReason,
            VoidedAt = sale.VoidedAt,
            ReceiptSnapshot = sale.ReceiptSnapshot,
            Items = sale.Items,
            Payments = sale.Payments,
            CreatedAt = sale.CreatedAt
        };
    }

    private static string BuildReadinessBlockedMessage(OfficialCheckoutReadinessResult readiness)
    {
        if (readiness.Blocks.Count == 0)
        {
            return "Official checkout is not ready because trusted persisted machine profile evidence is unavailable.";
        }

        return "Official checkout is not ready for official output: "
            + string.Join(" ", readiness.Blocks.Select(block => $"{block.Code}: {block.Message}"));
    }

    private static string NormalizeDocumentKind(string? documentKind)
    {
        return string.IsNullOrWhiteSpace(documentKind)
            ? string.Empty
            : documentKind.Trim().ToUpperInvariant();
    }

    private static string ToSequenceDocumentKind(OfficialDocumentSnapshotDraftKind kind)
    {
        return kind switch
        {
            OfficialDocumentSnapshotDraftKind.OfficialReceipt => "OFFICIAL_RECEIPT",
            OfficialDocumentSnapshotDraftKind.OfficialInvoice => "OFFICIAL_INVOICE",
            _ => string.Empty
        };
    }
}

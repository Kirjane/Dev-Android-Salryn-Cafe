using System.Security.Cryptography;
using System.Text;
using Salryn.Api.Local.Contracts;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;

namespace Salryn.Api.Local.Services;

public static class HostOperatorCheckoutStatus
{
    public const string Completed = "Completed";
    public const string OpenShiftRequired = "OpenShiftRequired";
    public const string ValidationFailed = "ValidationFailed";
    public const string ProductUnavailable = "ProductUnavailable";
    public const string StockChanged = "StockChanged";
    public const string PaymentInsufficient = "PaymentInsufficient";
}

public sealed record HostOperatorCheckoutOperationResult(
    bool Ok,
    string Code,
    string Message,
    HostOperatorCheckoutResponse? Checkout);

public sealed class HostOperatorCheckoutApiService
{
    private readonly IProductReadRepository _productReadRepository;
    private readonly IPosCheckoutApplicationService _checkoutApplicationService;
    private readonly ICashierSessionService _cashierSessionService;
    private readonly ISaleReadRepository _saleReadRepository;
    private readonly HostOperatorReceiptNumberAllocator _receiptAllocator;
    private readonly HostCheckoutRequestStore _requestStore;
    private readonly IFileReceiptWriter _fileReceiptWriter;
    private readonly IAuditWriteRepository _auditWriteRepository;
    private readonly object _idempotencyGateSync = new();
    private readonly Dictionary<string, IdempotencyGate> _idempotencyGates = new();

    public HostOperatorCheckoutApiService(
        IProductReadRepository productReadRepository,
        IPosCheckoutApplicationService checkoutApplicationService,
        ICashierSessionService cashierSessionService,
        ISaleReadRepository saleReadRepository,
        HostOperatorReceiptNumberAllocator receiptAllocator,
        HostCheckoutRequestStore requestStore,
        IFileReceiptWriter fileReceiptWriter,
        IAuditWriteRepository auditWriteRepository)
    {
        _productReadRepository = productReadRepository
            ?? throw new ArgumentNullException(nameof(productReadRepository));
        _checkoutApplicationService = checkoutApplicationService
            ?? throw new ArgumentNullException(nameof(checkoutApplicationService));
        _cashierSessionService = cashierSessionService
            ?? throw new ArgumentNullException(nameof(cashierSessionService));
        _saleReadRepository = saleReadRepository
            ?? throw new ArgumentNullException(nameof(saleReadRepository));
        _receiptAllocator = receiptAllocator
            ?? throw new ArgumentNullException(nameof(receiptAllocator));
        _requestStore = requestStore
            ?? throw new ArgumentNullException(nameof(requestStore));
        _fileReceiptWriter = fileReceiptWriter
            ?? throw new ArgumentNullException(nameof(fileReceiptWriter));
        _auditWriteRepository = auditWriteRepository
            ?? throw new ArgumentNullException(nameof(auditWriteRepository));
    }

    public async Task<HostOperatorCheckoutOperationResult> SubmitAsync(
        Guid userId,
        string? cashierDisplayName,
        HostOperatorCheckoutRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ClientRequestId) || request.ClientRequestId.Length > 128)
        {
            return Fail(
                "HostOperator.Checkout.ClientRequestIdRequired",
                HostOperatorCheckoutStatus.ValidationFailed,
                "A valid checkout request id is required.");
        }

        string requestHash = BuildRequestHash(userId, request);
        string key = $"{userId:N}:{request.ClientRequestId}";
        IdempotencyGate gate = RentIdempotencyGate(key);
        try
        {
            await gate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            ReturnIdempotencyGate(key, gate);
            throw;
        }

        try
        {
            HostCheckoutRequestRecord? durable = await _requestStore
                .FindAsync(userId, request.ClientRequestId, cancellationToken)
                .ConfigureAwait(false);
            if (durable is not null)
            {
                if (!string.Equals(durable.RequestHash, requestHash, StringComparison.Ordinal))
                {
                    return Fail(
                        "HostOperator.Checkout.ClientRequestIdReused",
                        HostOperatorCheckoutStatus.ValidationFailed,
                        "This checkout request id was already used for different sale details.");
                }

                HostOperatorCheckoutOperationResult reconciled =
                    await ReconcileAsync(durable, cancellationToken).ConfigureAwait(false);
                if (reconciled.Ok)
                {
                    return WithDuplicate(reconciled);
                }
            }

            return await ExecuteSubmitAsync(
                    userId,
                    cashierDisplayName,
                    request,
                    requestHash,
                    durable,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            HostCheckoutRequestRecord? durable = await _requestStore
                .FindAsync(userId, request.ClientRequestId, CancellationToken.None)
                .ConfigureAwait(false);
            if (durable is not null)
            {
                return await ReconcileAsync(durable, CancellationToken.None).ConfigureAwait(false);
            }

            return Fail(
                "HostOperator.Checkout.RetryableUnknown",
                HostOperatorCheckoutStatus.ValidationFailed,
                "Checkout could not be confirmed. Retry with the same request id.");
        }
        finally
        {
            gate.Semaphore.Release();
            ReturnIdempotencyGate(key, gate);
        }
    }

    private async Task<HostOperatorCheckoutOperationResult> ExecuteSubmitAsync(
        Guid userId,
        string? cashierDisplayName,
        HostOperatorCheckoutRequest request,
        string requestHash,
        HostCheckoutRequestRecord? durableRequest,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            return Fail(
                "HostOperator.Checkout.InvalidUser",
                HostOperatorCheckoutStatus.ValidationFailed,
                "Operator user id is required.");
        }

        if (request.Lines is null || request.Lines.Count == 0)
        {
            return Fail(
                "HostOperator.Checkout.EmptyCart",
                HostOperatorCheckoutStatus.ValidationFailed,
                "Checkout requires at least one line.");
        }

        if (request.Payments is null || request.Payments.Count == 0)
        {
            return Fail(
                "HostOperator.Checkout.PaymentRequired",
                HostOperatorCheckoutStatus.ValidationFailed,
                "At least one payment is required.");
        }

        Result<CashierSessionReadModel?> openSessionResult = await _cashierSessionService
            .FindOpenSessionByUserIdAsync(userId, cancellationToken)
            .ConfigureAwait(false);

        if (openSessionResult.IsFailure)
        {
            return Fail(
                openSessionResult.ErrorDetail.Code,
                HostOperatorCheckoutStatus.ValidationFailed,
                openSessionResult.ErrorDetail.Message);
        }

        if (openSessionResult.Value is null)
        {
            return Fail(
                "HostOperator.Checkout.OpenShiftRequired",
                HostOperatorCheckoutStatus.OpenShiftRequired,
                "An open cashier shift is required before checkout.");
        }

        List<PosCheckoutPaymentInput> payments = new();
        foreach (HostOperatorCheckoutPaymentRequest payment in request.Payments)
        {
            PaymentMethod method;
            if (string.Equals(payment.Method, "Cash", StringComparison.OrdinalIgnoreCase))
            {
                method = PaymentMethod.Cash;
            }
            else if (string.Equals(payment.Method, "AccountsReceivable", StringComparison.OrdinalIgnoreCase))
            {
                method = PaymentMethod.AccountsReceivable;
            }
            else
            {
                return Fail(
                    "HostOperator.Checkout.UnsupportedPaymentMethod",
                    HostOperatorCheckoutStatus.ValidationFailed,
                    "Payment method must be Cash or AccountsReceivable.");
            }

            if (payment.AmountCents <= 0)
            {
                return Fail(
                    "HostOperator.Checkout.InvalidPaymentAmount",
                    HostOperatorCheckoutStatus.ValidationFailed,
                    "Payment amount must be greater than zero.");
            }

            payments.Add(new PosCheckoutPaymentInput(method, payment.AmountCents));
        }

        List<PosCheckoutLineInput> lines = new();
        foreach (HostOperatorCheckoutLineRequest line in request.Lines)
        {
            if (line.ProductId == Guid.Empty)
            {
                return Fail(
                    "HostOperator.Checkout.InvalidProductId",
                    HostOperatorCheckoutStatus.ValidationFailed,
                    "Checkout line product id is required.");
            }

            if (line.QuantityX1000 <= 0)
            {
                return Fail(
                    "HostOperator.Checkout.InvalidQuantity",
                    HostOperatorCheckoutStatus.ValidationFailed,
                    "Checkout line quantity must be greater than zero.");
            }

            Result<ProductLookupReadModel?> productResult = await _productReadRepository
                .FindByIdAsync(line.ProductId, cancellationToken)
                .ConfigureAwait(false);

            if (productResult.IsFailure)
            {
                return Fail(
                    productResult.ErrorDetail.Code,
                    HostOperatorCheckoutStatus.ValidationFailed,
                    productResult.ErrorDetail.Message);
            }

            ProductLookupReadModel? product = productResult.Value;
            if (product is null || !product.IsActive)
            {
                return Fail(
                    "HostOperator.Checkout.ProductUnavailable",
                    HostOperatorCheckoutStatus.ProductUnavailable,
                    "Product was not found or is inactive.");
            }

            lines.Add(new PosCheckoutLineInput(
                product.Id,
                product.Name,
                product.Barcode,
                product.UnitName,
                line.QuantityX1000,
                product.SellingPrice.Cents,
                product.CostPrice?.Cents,
                0,
                product.TrackStock,
                product.TaxClassification));
        }

        HostCheckoutRequestRecord ledger = durableRequest
            ?? await _requestStore.GetOrCreatePendingAsync(
                    userId,
                    request.ClientRequestId!,
                    requestHash,
                    _receiptAllocator.AllocateNext(openSessionResult.Value.Id),
                    cancellationToken)
                .ConfigureAwait(false);

        if (!string.Equals(ledger.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return Fail(
                "HostOperator.Checkout.ClientRequestIdReused",
                HostOperatorCheckoutStatus.ValidationFailed,
                "This checkout request id was already used for different sale details.");
        }

        string receiptNumber = ledger.ReceiptNumber;

        PosCheckoutApplicationRequest applicationRequest = new(
            userId,
            openSessionResult.Value.Id,
            receiptNumber,
            lines,
            payments,
            SaleDiscountCents: 0,
            TaxCents: 0,
            ReceiptSnapshot: null,
            CashierDisplayName: cashierDisplayName,
            CustomerId: request.CustomerId,
            CustomerFullName: request.CustomerFullName,
            CustomerPhone: request.CustomerPhone,
            ReceivableDueDate: request.DueDate,
            ReceivableNote: request.Note);

        Result completion = await _checkoutApplicationService
            .CompleteSaleAsync(applicationRequest, cancellationToken)
            .ConfigureAwait(false);

        if (completion.IsFailure)
        {
            return MapFailure(completion.ErrorDetail);
        }

        Result<SaleDetailReadModel?> saleResult = await _saleReadRepository
            .FindSaleByReceiptNoAsync(receiptNumber, cancellationToken)
            .ConfigureAwait(false);

        SaleDetailReadModel? sale = saleResult.IsSuccess ? saleResult.Value : null;
        bool receiptSnapshotSaved = !string.IsNullOrWhiteSpace(sale?.ReceiptSnapshot);
        FileReceiptWriteResult fileReceipt = await WriteFileReceiptAfterCommitAsync(sale, saleResult, cancellationToken)
            .ConfigureAwait(false);

        string message = BuildCompletedMessage(fileReceipt);

        await _requestStore.MarkCompletedAsync(
                ledger,
                sale?.Id.ToString(),
                sale?.Total.Cents,
                sale?.Change.Cents,
                message,
                cancellationToken)
            .ConfigureAwait(false);

        await OperatorAuditTrail.TryWriteAsync(
                _auditWriteRepository,
                userId,
                OperatorAuditActions.SaleCompleted,
                "Sale",
                sale?.Id.ToString() ?? receiptNumber,
                new
                {
                    receiptNumber,
                    saleId = sale?.Id.ToString(),
                    totalCents = sale?.Total.Cents,
                    lineCount = request.Lines.Count
                },
                reason: "Host operator checkout committed.",
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return new HostOperatorCheckoutOperationResult(
            true,
            "HostOperator.Checkout.Completed",
            message,
            new HostOperatorCheckoutResponse(
                HostOperatorCheckoutStatus.Completed,
                receiptNumber,
                sale?.Id.ToString(),
                sale?.Total.Cents,
                sale?.Change.Cents,
                message,
                receiptSnapshotSaved,
                fileReceipt.Status,
                fileReceipt.FilePath,
                fileReceipt.ErrorMessage,
                request.ClientRequestId,
                Duplicate: false));
    }

    private async Task<HostOperatorCheckoutOperationResult> ReconcileAsync(
        HostCheckoutRequestRecord record,
        CancellationToken cancellationToken)
    {
        if (record.State == HostCheckoutRequestStore.Completed)
        {
            return CompletedFromRecord(record, duplicate: false);
        }

        Result<SaleDetailReadModel?> saleResult = await _saleReadRepository
            .FindSaleByReceiptNoAsync(record.ReceiptNumber, cancellationToken)
            .ConfigureAwait(false);
        SaleDetailReadModel? sale = saleResult.IsSuccess ? saleResult.Value : null;
        if (sale is null)
        {
            return Fail(
                "HostOperator.Checkout.PendingConfirmation",
                HostOperatorCheckoutStatus.ValidationFailed,
                "Checkout is not committed; retry the original request with the same request id.");
        }

        const string message = "Sale completed on host. The original result was reconciled.";
        await _requestStore.MarkCompletedAsync(
                record,
                sale.Id.ToString(),
                sale.Total.Cents,
                sale.Change.Cents,
                message,
                cancellationToken)
            .ConfigureAwait(false);

        return new HostOperatorCheckoutOperationResult(
            true,
            "HostOperator.Checkout.Completed",
            message,
            new HostOperatorCheckoutResponse(
                HostOperatorCheckoutStatus.Completed,
                record.ReceiptNumber,
                sale.Id.ToString(),
                sale.Total.Cents,
                sale.Change.Cents,
                message,
                !string.IsNullOrWhiteSpace(sale.ReceiptSnapshot),
                ClientRequestId: record.ClientRequestId));
    }

    private static HostOperatorCheckoutOperationResult CompletedFromRecord(
        HostCheckoutRequestRecord record,
        bool duplicate) =>
        new(
            true,
            "HostOperator.Checkout.Completed",
            record.Message,
            new HostOperatorCheckoutResponse(
                HostOperatorCheckoutStatus.Completed,
                record.ReceiptNumber,
                record.SaleId,
                record.TotalCents,
                record.ChangeCents,
                record.Message,
                ClientRequestId: record.ClientRequestId,
                Duplicate: duplicate));

    private static HostOperatorCheckoutOperationResult WithDuplicate(
        HostOperatorCheckoutOperationResult result) =>
        result.Checkout is null
            ? result
            : result with { Checkout = result.Checkout with { Duplicate = true } };

    private static string BuildRequestHash(Guid userId, HostOperatorCheckoutRequest request)
    {
        StringBuilder canonical = new();
        canonical.Append(userId.ToString("N"));
        foreach (HostOperatorCheckoutLineRequest line in (request.Lines ?? Array.Empty<HostOperatorCheckoutLineRequest>())
                     .OrderBy(line => line.ProductId))
        {
            canonical.Append("|L:").Append(line.ProductId.ToString("N")).Append(':').Append(line.QuantityX1000);
        }
        foreach (HostOperatorCheckoutPaymentRequest payment in request.Payments ?? Array.Empty<HostOperatorCheckoutPaymentRequest>())
        {
            canonical.Append("|P:").Append(payment.Method.Trim().ToUpperInvariant()).Append(':').Append(payment.AmountCents);
        }
        canonical.Append("|C:").Append(request.CustomerId?.ToString("N") ?? string.Empty);
        canonical.Append("|D:").Append(request.DueDate?.ToString("O") ?? string.Empty);
        canonical.Append("|N:").Append(request.Note?.Trim() ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private IdempotencyGate RentIdempotencyGate(string key)
    {
        lock (_idempotencyGateSync)
        {
            if (!_idempotencyGates.TryGetValue(key, out IdempotencyGate? gate))
            {
                gate = new IdempotencyGate();
                _idempotencyGates.Add(key, gate);
            }
            gate.Renters++;
            return gate;
        }
    }

    private void ReturnIdempotencyGate(string key, IdempotencyGate gate)
    {
        bool dispose = false;
        lock (_idempotencyGateSync)
        {
            gate.Renters--;
            if (gate.Renters == 0 &&
                _idempotencyGates.TryGetValue(key, out IdempotencyGate? current) &&
                ReferenceEquals(current, gate))
            {
                _idempotencyGates.Remove(key);
                dispose = true;
            }
        }
        if (dispose)
        {
            gate.Semaphore.Dispose();
        }
    }

    private sealed class IdempotencyGate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int Renters { get; set; }
    }

    private async Task<FileReceiptWriteResult> WriteFileReceiptAfterCommitAsync(
        SaleDetailReadModel? sale,
        Result<SaleDetailReadModel?> saleResult,
        CancellationToken cancellationToken)
    {
        if (saleResult.IsFailure)
        {
            return FileReceiptWriteResult.NotAttempted(
                "Sale was committed, but sale detail reload failed: " + saleResult.ErrorDetail.Message);
        }

        if (sale is null)
        {
            return FileReceiptWriteResult.NotAttempted(
                "Sale was committed, but sale detail was not found for receipt reload.");
        }

        if (string.IsNullOrWhiteSpace(sale.ReceiptSnapshot))
        {
            return FileReceiptWriteResult.NotAttempted(
                "Sale was committed, but receipt snapshot was empty.");
        }

        return await _fileReceiptWriter
            .WriteAsync(sale, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string BuildCompletedMessage(FileReceiptWriteResult fileReceipt)
    {
        return fileReceipt.Status switch
        {
            "Written" => "Sale completed on host. Receipt snapshot saved and file receipt written.",
            "Failed" => "Sale completed on host. Receipt snapshot saved, but file receipt write failed; sale remains committed.",
            _ => "Sale completed on host. Receipt file was not written; sale remains committed."
        };
    }

    private HostOperatorCheckoutOperationResult MapFailure(Error error)
    {
        string code = error.Code ?? string.Empty;
        string status = code switch
        {
            _ when code.Contains("NegativeStock", StringComparison.OrdinalIgnoreCase) =>
                HostOperatorCheckoutStatus.StockChanged,
            _ when code.Contains("ProductNotFoundOrInactive", StringComparison.OrdinalIgnoreCase) =>
                HostOperatorCheckoutStatus.ProductUnavailable,
            _ when code.Contains("OpenCashierSessionRequired", StringComparison.OrdinalIgnoreCase) =>
                HostOperatorCheckoutStatus.OpenShiftRequired,
            _ when code.Contains("InsufficientPayment", StringComparison.OrdinalIgnoreCase) =>
                HostOperatorCheckoutStatus.PaymentInsufficient,
            _ => HostOperatorCheckoutStatus.ValidationFailed,
        };

        return Fail(code, status, error.Message);
    }

    private static HostOperatorCheckoutOperationResult Fail(
        string code,
        string status,
        string message) =>
        new(false, code, message, new HostOperatorCheckoutResponse(status, null, null, null, null, message));
}

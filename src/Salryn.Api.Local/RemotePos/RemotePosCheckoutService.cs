using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;

namespace Salryn.Api.Local.RemotePos;

public static class RemotePosCheckoutStatus
{
    public const string Completed = "Completed";
    public const string DuplicateRequest = "DuplicateRequest";
    public const string StockChanged = "StockChanged";
    public const string ProductUnavailable = "ProductUnavailable";
    public const string PriceChanged = "PriceChanged";
    public const string PaymentInsufficient = "PaymentInsufficient";
    public const string OpenShiftRequired = "OpenShiftRequired";
    public const string ValidationFailed = "ValidationFailed";
    public const string RetryableUnknown = "RetryableUnknown";
    public const string PendingConfirmation = "PendingConfirmation";
    public const string NotFound = "NotFound";
}

public sealed record RemotePosCartOperationResult(bool Ok, string Code, string Message, RemotePosCartResponse? Cart);

public sealed record RemotePosShiftOperationResult(bool Ok, string Code, string Message, RemotePosShiftResponse? Shift);

/// <summary>
/// LAN-M16 remote checkout runtime. Owns cart lifecycle (host-side, in memory, keyed by operator
/// session), validates products/pricing on the host, and drives a REAL host-owned sale through
/// the standard <see cref="IPosCheckoutApplicationService"/> path (server-trusted re-pricing,
/// atomic sale + inventory decrement). Writes are idempotent per client request id so a LAN drop
/// + retry never double-posts. The receipt/document number is host-allocated; the client never
/// supplies it.
/// </summary>
public sealed class RemotePosCheckoutService
{
    private readonly IProductReadRepository _productReadRepository;
    private readonly IPosCheckoutApplicationService _checkoutApplicationService;
    private readonly ICashierSessionService _cashierSessionService;
    private readonly ISaleReadRepository _saleReadRepository;
    private readonly IAuditWriteRepository _auditWriteRepository;
    private readonly RemotePosReceiptNumberAllocator _receiptAllocator;
    private readonly RemotePosCheckoutRequestStore _requestStore;

    private readonly ConcurrentDictionary<Guid, RemotePosCart> _carts = new();
    private readonly object _idempotencyGateSync = new();
    private readonly Dictionary<string, IdempotencyGate> _idempotencyGates = new();

    public RemotePosCheckoutService(
        IProductReadRepository productReadRepository,
        IPosCheckoutApplicationService checkoutApplicationService,
        ICashierSessionService cashierSessionService,
        ISaleReadRepository saleReadRepository,
        IAuditWriteRepository auditWriteRepository,
        RemotePosReceiptNumberAllocator receiptAllocator,
        RemotePosCheckoutRequestStore requestStore)
    {
        _productReadRepository = productReadRepository ?? throw new ArgumentNullException(nameof(productReadRepository));
        _checkoutApplicationService = checkoutApplicationService ?? throw new ArgumentNullException(nameof(checkoutApplicationService));
        _cashierSessionService = cashierSessionService ?? throw new ArgumentNullException(nameof(cashierSessionService));
        _saleReadRepository = saleReadRepository ?? throw new ArgumentNullException(nameof(saleReadRepository));
        _auditWriteRepository = auditWriteRepository ?? throw new ArgumentNullException(nameof(auditWriteRepository));
        _receiptAllocator = receiptAllocator ?? throw new ArgumentNullException(nameof(receiptAllocator));
        _requestStore = requestStore ?? throw new ArgumentNullException(nameof(requestStore));
    }

    // ---- Cart lifecycle -------------------------------------------------------------------

    public RemotePosCartResponse StartCart(RemotePosOperatorSession session)
    {
        RemotePosCart cart = new(Guid.NewGuid());
        _carts[session.SessionId] = cart;
        return BuildCartResponse(session, cart, "New remote cart started.");
    }

    public RemotePosCartResponse ViewCart(RemotePosOperatorSession session)
    {
        RemotePosCart cart = _carts.GetOrAdd(session.SessionId, _ => new RemotePosCart(Guid.NewGuid()));
        return BuildCartResponse(session, cart, "Current remote cart.");
    }

    public async Task<RemotePosCartOperationResult> AddLineAsync(
        RemotePosOperatorSession session,
        Guid productId,
        long quantityX1000,
        CancellationToken cancellationToken = default)
    {
        if (quantityX1000 <= 0)
        {
            return Fail(RemotePosCheckoutStatus.ValidationFailed, "RemotePos.Cart.InvalidQuantity",
                "Cart line quantity must be greater than zero.");
        }

        Result<ProductLookupReadModel?> productResult =
            await _productReadRepository.FindByIdAsync(productId, cancellationToken).ConfigureAwait(false);

        if (productResult.IsFailure)
        {
            return Fail(RemotePosCheckoutStatus.RetryableUnknown, "RemotePos.Cart.ProductLookupFailed",
                "Product lookup failed on the host.");
        }

        ProductLookupReadModel? product = productResult.Value;

        if (product is null || !product.IsActive)
        {
            return Fail(RemotePosCheckoutStatus.ProductUnavailable, "RemotePos.Cart.ProductUnavailable",
                "Product was not found or is inactive on the host.");
        }

        RemotePosCart cart = _carts.GetOrAdd(session.SessionId, _ => new RemotePosCart(Guid.NewGuid()));

        lock (cart.Sync)
        {
            RemotePosCartLine? existing = cart.Lines.FirstOrDefault(line => line.ProductId == product.Id);
            if (existing is null)
            {
                cart.Lines.Add(new RemotePosCartLine(
                    product.Id, product.Name, product.UnitName, quantityX1000, product.SellingPrice.Cents));
            }
            else
            {
                existing.QuantityX1000 = checked(existing.QuantityX1000 + quantityX1000);
                existing.UnitPriceCents = product.SellingPrice.Cents;
            }
        }

        return Ok(BuildCartResponse(session, cart, "Cart line added."));
    }

    /// <summary>
    /// PRE-BETA-F: add cart line by keyboard-wedge barcode (exact barcode match on host).
    /// Unknown barcodes return ProductUnavailable without inventing products on the LAN terminal.
    /// </summary>
    public async Task<RemotePosCartOperationResult> AddLineByBarcodeAsync(
        RemotePosOperatorSession session,
        string barcode,
        long quantityX1000,
        CancellationToken cancellationToken = default)
    {
        if (quantityX1000 <= 0)
        {
            return Fail(RemotePosCheckoutStatus.ValidationFailed, "RemotePos.Cart.InvalidQuantity",
                "Cart line quantity must be greater than zero.");
        }

        Result<Barcode> barcodeResult = Barcode.Create(barcode);
        if (barcodeResult.IsFailure)
        {
            return Fail(RemotePosCheckoutStatus.ValidationFailed, "RemotePos.Cart.BarcodeRequired",
                barcodeResult.ErrorDetail.Message);
        }

        Result<ProductLookupReadModel?> productResult =
            await _productReadRepository.FindByBarcodeAsync(barcodeResult.Value, cancellationToken).ConfigureAwait(false);

        if (productResult.IsFailure)
        {
            return Fail(RemotePosCheckoutStatus.RetryableUnknown, "RemotePos.Cart.ProductLookupFailed",
                "Product lookup failed on the host.");
        }

        ProductLookupReadModel? product = productResult.Value;
        if (product is null || !product.IsActive)
        {
            return Fail(RemotePosCheckoutStatus.ProductUnavailable, "RemotePos.Cart.UnknownBarcode",
                "Unknown barcode. No product was added. Create or activate the product on the Host, then scan again.");
        }

        return await AddLineAsync(session, product.Id, quantityX1000, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RemotePosCartOperationResult> UpdateLineAsync(
        RemotePosOperatorSession session,
        Guid productId,
        long quantityX1000,
        CancellationToken cancellationToken = default)
    {
        if (quantityX1000 <= 0)
        {
            return Fail(RemotePosCheckoutStatus.ValidationFailed, "RemotePos.Cart.InvalidQuantity",
                "Cart line quantity must be greater than zero.");
        }

        Result<ProductLookupReadModel?> productResult =
            await _productReadRepository.FindByIdAsync(productId, cancellationToken).ConfigureAwait(false);

        if (productResult.IsFailure || productResult.Value is null || !productResult.Value.IsActive)
        {
            return Fail(RemotePosCheckoutStatus.ProductUnavailable, "RemotePos.Cart.ProductUnavailable",
                "Product was not found or is inactive on the host.");
        }

        if (!_carts.TryGetValue(session.SessionId, out RemotePosCart? cart))
        {
            return Fail(RemotePosCheckoutStatus.ValidationFailed, "RemotePos.Cart.NotFound", "No active cart.");
        }

        bool found;
        lock (cart.Sync)
        {
            RemotePosCartLine? existing = cart.Lines.FirstOrDefault(line => line.ProductId == productId);
            found = existing is not null;
            if (existing is not null)
            {
                existing.QuantityX1000 = quantityX1000;
                existing.UnitPriceCents = productResult.Value.SellingPrice.Cents;
            }
        }

        if (!found)
        {
            return Fail(RemotePosCheckoutStatus.ValidationFailed, "RemotePos.Cart.LineNotFound",
                "Cart line was not found.");
        }

        return Ok(BuildCartResponse(session, cart, "Cart line updated."));
    }

    public RemotePosCartOperationResult RemoveLine(RemotePosOperatorSession session, Guid productId)
    {
        if (!_carts.TryGetValue(session.SessionId, out RemotePosCart? cart))
        {
            return Fail(RemotePosCheckoutStatus.ValidationFailed, "RemotePos.Cart.NotFound", "No active cart.");
        }

        lock (cart.Sync)
        {
            cart.Lines.RemoveAll(line => line.ProductId == productId);
        }

        return Ok(BuildCartResponse(session, cart, "Cart line removed."));
    }

    public RemotePosCartOperationResult ClearCart(RemotePosOperatorSession session)
    {
        RemotePosCart cart = _carts.GetOrAdd(session.SessionId, _ => new RemotePosCart(Guid.NewGuid()));
        lock (cart.Sync)
        {
            cart.Lines.Clear();
        }

        return Ok(BuildCartResponse(session, cart, "Cart cleared."));
    }

    // ---- Shift (cashier session) ----------------------------------------------------------

    public async Task<RemotePosShiftOperationResult> OpenShiftAsync(
        RemotePosOperatorSession session,
        long openingCashCents,
        CancellationToken cancellationToken = default)
    {
        if (openingCashCents < 0)
        {
            return new RemotePosShiftOperationResult(false, "RemotePos.Shift.InvalidCash",
                "Opening cash cannot be negative.", null);
        }

        Result<CashierSessionReadModel?> openResult = await _cashierSessionService
            .FindOpenSessionByUserIdAsync(session.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (openResult.IsSuccess && openResult.Value is not null)
        {
            return new RemotePosShiftOperationResult(true, "RemotePos.Shift.AlreadyOpen",
                "A cashier shift is already open.",
                BuildShiftResponse(openResult.Value));
        }

        Result<CashierSessionReadModel> opened = await _cashierSessionService
            .OpenAsync(new OpenCashierSessionRequest(session.UserId, Money.FromCents(openingCashCents), "Remote POS shift"), cancellationToken)
            .ConfigureAwait(false);

        if (opened.IsFailure)
        {
            return new RemotePosShiftOperationResult(false, "RemotePos.Shift.OpenFailed",
                "Cashier shift could not be opened on the host.", null);
        }

        return new RemotePosShiftOperationResult(true, "RemotePos.Shift.Opened",
            "Cashier shift opened.", BuildShiftResponse(opened.Value));
    }

    public async Task<RemotePosShiftOperationResult> ReadShiftAsync(
        RemotePosOperatorSession session,
        CancellationToken cancellationToken = default)
    {
        Result<CashierSessionReadModel?> openResult = await _cashierSessionService
            .FindOpenSessionByUserIdAsync(session.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (openResult.IsFailure)
        {
            return new RemotePosShiftOperationResult(false, "RemotePos.Shift.ReadFailed",
                "Cashier shift could not be read on the host.", null);
        }

        if (openResult.Value is null)
        {
            return new RemotePosShiftOperationResult(true, "RemotePos.Shift.None",
                "No open cashier shift.",
                new RemotePosShiftResponse("None", null, null, "No open cashier shift for this operator."));
        }

        return new RemotePosShiftOperationResult(true, "RemotePos.Shift.Open",
            "Open cashier shift.", BuildShiftResponse(openResult.Value));
    }

    public async Task<RemotePosShiftOperationResult> CloseShiftAsync(
        RemotePosOperatorSession session,
        CancellationToken cancellationToken = default)
    {
        Result<CashierSessionReadModel?> openResult = await _cashierSessionService
            .FindOpenSessionByUserIdAsync(session.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (openResult.IsFailure || openResult.Value is null)
        {
            return new RemotePosShiftOperationResult(false, "RemotePos.Shift.NoneToClose",
                "No open cashier shift to close.", null);
        }

        return new RemotePosShiftOperationResult(false,"RemotePos.Shift.PhysicalCountRequired",
            "Remote close is disabled. Complete the physical drawer count on the Host console.",null);
    }

    // ---- Checkout submit (idempotent host write) ------------------------------------------

    public async Task<RemotePosCheckoutResultResponse?> FindCheckoutResultAsync(
        RemotePosOperatorSession session,
        string clientRequestId,
        CancellationToken cancellationToken = default)
    {
        RemotePosCheckoutRequestRecord? record = await _requestStore
            .FindAsync(session.StoreId, session.TerminalId, clientRequestId, cancellationToken)
            .ConfigureAwait(false);
        return record is null ? null : await ReconcileRecordAsync(record, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RemotePosCheckoutResultResponse> SubmitAsync(
        RemotePosOperatorSession session,
        RemotePosCheckoutSubmitRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ClientRequestId) || request.ClientRequestId.Length > 128)
        {
            return Result(RemotePosCheckoutStatus.ValidationFailed, request.ClientRequestId ?? string.Empty,
                "A valid client request id (idempotency key) is required.", duplicate: false);
        }

        // Kirch: why - serialize submits sharing an idempotency key so a LAN drop + retry cannot
        // race two host writes. The DB sales.receipt_no UNIQUE constraint is the final backstop.
        string key = BuildIdempotencyKey(session, request.ClientRequestId);
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
            RemotePosCheckoutRequestRecord? durable = await _requestStore
                .FindAsync(session.StoreId, session.TerminalId, request.ClientRequestId, cancellationToken)
                .ConfigureAwait(false);
            if (durable is not null)
            {
                RemotePosCheckoutResultResponse reconciled =
                    await ReconcileRecordAsync(durable, cancellationToken).ConfigureAwait(false);
                if (reconciled.Status == RemotePosCheckoutStatus.Completed)
                {
                    return reconciled with { Duplicate = true, Status = RemotePosCheckoutStatus.DuplicateRequest };
                }
            }

            RemotePosCheckoutResultResponse result =
                await ExecuteSubmitAsync(session, request, durable, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            System.Diagnostics.Trace.TraceWarning(
                "RemotePosCheckoutService.SubmitAsync fault for ClientRequestId={0}: {1}",
                request.ClientRequestId,
                ex.GetType().Name);
            RemotePosCheckoutRequestRecord? durable = await _requestStore
                .FindAsync(session.StoreId, session.TerminalId, request.ClientRequestId, CancellationToken.None)
                .ConfigureAwait(false);
            if (durable is not null)
            {
                return await ReconcileRecordAsync(durable, CancellationToken.None).ConfigureAwait(false);
            }

            return Result(RemotePosCheckoutStatus.RetryableUnknown, request.ClientRequestId,
                "Remote checkout failed before a durable request was recorded.", duplicate: false);
        }
        finally
        {
            gate.Semaphore.Release();
            ReturnIdempotencyGate(key, gate);
        }
    }

    private async Task<RemotePosCheckoutResultResponse> ExecuteSubmitAsync(
        RemotePosOperatorSession session,
        RemotePosCheckoutSubmitRequest request,
        RemotePosCheckoutRequestRecord? durableRequest,
        CancellationToken cancellationToken)
    {
        if (request.Payments is null || request.Payments.Count == 0)
        {
            return Result(RemotePosCheckoutStatus.ValidationFailed, request.ClientRequestId,
                "At least one payment is required.", duplicate: false);
        }

        List<PosCheckoutPaymentInput> payments = new();
        foreach (RemotePosCheckoutPaymentRequest payment in request.Payments)
        {
            if (!string.Equals(payment.Method, "Cash", StringComparison.OrdinalIgnoreCase))
            {
                return Result(RemotePosCheckoutStatus.ValidationFailed, request.ClientRequestId,
                    "Only Cash tender is supported over remote POS in this build.", duplicate: false);
            }

            if (payment.AmountCents <= 0)
            {
                return Result(RemotePosCheckoutStatus.ValidationFailed, request.ClientRequestId,
                    "Payment amount must be greater than zero.", duplicate: false);
            }

            payments.Add(new PosCheckoutPaymentInput(PaymentMethod.Cash, payment.AmountCents));
        }

        if (!_carts.TryGetValue(session.SessionId, out RemotePosCart? cart))
        {
            return Result(RemotePosCheckoutStatus.ValidationFailed, request.ClientRequestId,
                "No active cart to submit.", duplicate: false);
        }

        List<PosCheckoutLineInput> lines;
        lock (cart.Sync)
        {
            if (cart.Lines.Count == 0)
            {
                return Result(RemotePosCheckoutStatus.ValidationFailed, request.ClientRequestId,
                    "Cart is empty.", duplicate: false);
            }

            lines = cart.Lines
                .Select(line => new PosCheckoutLineInput(
                    line.ProductId,
                    line.ProductName,
                    null,
                    line.UnitName,
                    line.QuantityX1000,
                    line.UnitPriceCents,
                    null,
                    0,
                    false))
                .ToList();
        }

        // Host revalidates the open cashier shift at submit time (fail closed).
        Result<CashierSessionReadModel?> openSessionResult = await _cashierSessionService
            .FindOpenSessionByUserIdAsync(session.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (openSessionResult.IsFailure)
        {
            return Result(RemotePosCheckoutStatus.RetryableUnknown, request.ClientRequestId,
                "Cashier shift could not be validated on the host.", duplicate: false);
        }

        if (openSessionResult.Value is null)
        {
            return Result(RemotePosCheckoutStatus.OpenShiftRequired, request.ClientRequestId,
                "An open cashier shift is required on the host before checkout.", duplicate: false);
        }

        string requestHash = BuildRequestHash(cart.CartId, lines, payments);
        RemotePosCheckoutRequestRecord ledger = durableRequest
            ?? await _requestStore.GetOrCreatePendingAsync(
                    session.StoreId,
                    session.TerminalId,
                    request.ClientRequestId,
                    requestHash,
                    _receiptAllocator.AllocateNext(),
                    cancellationToken)
                .ConfigureAwait(false);

        if (!string.Equals(ledger.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return Result(RemotePosCheckoutStatus.ValidationFailed, request.ClientRequestId,
                "This checkout request id was already used for different cart or payment details.", duplicate: false);
        }

        // Host owns the stable receipt number recorded before posting; the client never supplies it.
        string receiptNumber = ledger.ReceiptNumber;

        PosCheckoutApplicationRequest applicationRequest = new(
            session.UserId,
            openSessionResult.Value.Id,
            receiptNumber,
            lines,
            payments,
            SaleDiscountCents: 0,
            TaxCents: 0,
            ReceiptSnapshot: null);

        // Host executes the write: server-trusted re-pricing, atomic sale + inventory decrement.
        Result completion = await _checkoutApplicationService
            .CompleteSaleAsync(applicationRequest, cancellationToken)
            .ConfigureAwait(false);

        if (completion.IsFailure)
        {
            // No sale committed: safe to map to a conflict/validation/retry code with no memoization.
            return MapFailure(completion.ErrorDetail, request.ClientRequestId);
        }

        // ---- The host sale is now COMMITTED. Everything below must be fail-safe. ----------------

        // A read-back failure cannot undo the host commit, but it also cannot
        // authorize a fabricated Completed result with missing financial totals.
        Result<SaleDetailReadModel?> saleResult = await _saleReadRepository
            .FindSaleByReceiptNoAsync(receiptNumber, cancellationToken)
            .ConfigureAwait(false);

        SaleDetailReadModel? sale = saleResult.IsSuccess ? saleResult.Value : null;
        if (sale is null)
        {
            // The stable pending receipt survives a lost acknowledgement or read failure.
            // Reconciliation on retry will confirm or recover the committed sale.
            return Result(RemotePosCheckoutStatus.PendingConfirmation, request.ClientRequestId,
                "Host sale confirmation is unavailable. Retry with the same checkout request id.",
                duplicate: false);
        }

        RemotePosCheckoutResultResponse completed = new(
            RemotePosCheckoutStatus.Completed,
            request.ClientRequestId,
            receiptNumber,
            sale?.Id.ToString(),
            sale?.Total.Cents,
            sale?.Change.Cents,
            Duplicate: false,
            Message: "Remote sale completed on host.");

        // Kirch: why - memoize the committed success IMMEDIATELY before invoking any non-atomic side effects
        // Persist the committed result before any best-effort side effect. If this update itself is
        // interrupted, reconciliation uses the stable receipt recorded before posting and repairs
        // the ledger from the authoritative sale row.
        await _requestStore.MarkCompletedAsync(
                ledger,
                completed.SaleId,
                completed.TotalCents,
                completed.ChangeCents,
                completed.Message,
                cancellationToken)
            .ConfigureAwait(false);

        // Clear the cart deterministically after the committed success. This is an in-memory list
        // clear under the cart lock and cannot fail the sale or create retry ambiguity.
        lock (cart.Sync)
        {
            cart.Lines.Clear();
        }

        // Audit is strictly best-effort AFTER the sale is durable and memoized. WriteAuditAsync
        // swallows its own failures, so a failed audit can never flip the checkout to failed/unknown.
        await WriteAuditAsync(session, receiptNumber, sale?.Id, request.ClientRequestId, "Completed", cancellationToken)
            .ConfigureAwait(false);

        return completed;
    }

    private RemotePosCheckoutResultResponse MapFailure(Error error, string clientRequestId)
    {
        string code = error.Code ?? string.Empty;

        string status = code switch
        {
            _ when code.Contains("NegativeStock", StringComparison.OrdinalIgnoreCase) => RemotePosCheckoutStatus.StockChanged,
            _ when code.Contains("ProductNotFoundOrInactive", StringComparison.OrdinalIgnoreCase) => RemotePosCheckoutStatus.ProductUnavailable,
            _ when code.Contains("OpenCashierSessionRequired", StringComparison.OrdinalIgnoreCase) => RemotePosCheckoutStatus.OpenShiftRequired,
            _ when code.Contains("InsufficientPayment", StringComparison.OrdinalIgnoreCase) => RemotePosCheckoutStatus.PaymentInsufficient,
            _ when code.Contains("ReceiptNumber", StringComparison.OrdinalIgnoreCase) => RemotePosCheckoutStatus.RetryableUnknown,
            _ when code.StartsWith("CheckoutBoundary.", StringComparison.Ordinal) => RemotePosCheckoutStatus.ValidationFailed,
            _ when code.StartsWith("Checkout.", StringComparison.Ordinal) => RemotePosCheckoutStatus.ValidationFailed,
            _ => RemotePosCheckoutStatus.RetryableUnknown,
        };

        // Kirch: why - never surface raw internal error details to a remote client.
        string message = status switch
        {
            RemotePosCheckoutStatus.StockChanged => "Stock changed on the host; the sale was not completed.",
            RemotePosCheckoutStatus.ProductUnavailable => "A product became unavailable on the host.",
            RemotePosCheckoutStatus.OpenShiftRequired => "An open cashier shift is required on the host before checkout.",
            RemotePosCheckoutStatus.PaymentInsufficient => "Payment is less than the sale total.",
            RemotePosCheckoutStatus.ValidationFailed => "Checkout request failed host validation.",
            _ => "Remote checkout could not be completed. Retry is safe.",
        };

        return Result(status, clientRequestId, message, duplicate: false);
    }

    private async Task WriteAuditAsync(
        RemotePosOperatorSession session,
        string receiptNumber,
        Guid? saleId,
        string clientRequestId,
        string result,
        CancellationToken cancellationToken)
    {
        string reason =
            $"host={session.HostId};store={session.StoreId};terminal={session.TerminalId};role={session.RoleName};" +
            $"operation=RemotePos.CheckoutSubmit;result={result};clientRequestId={clientRequestId};receipt={receiptNumber}";

        AuditLog auditLog = new()
        {
            UserId = session.UserId,
            Action = "RemotePos.CheckoutSubmit",
            EntityType = "RemotePosSale",
            EntityId = saleId?.ToString() ?? receiptNumber,
            Reason = reason,
        };

        // Kirch: why - audit runs AFTER the sale committed and the success was memoized. It is
        // strictly best-effort: any failure (repository error, a raised exception, or a failed
        // Result) is swallowed here so it can never fail a committed sale, change the checkout
        // outcome, or create retry ambiguity. No exception detail is surfaced to the remote client.
        try
        {
            Result auditResult = await _auditWriteRepository.AddAsync(auditLog, cancellationToken).ConfigureAwait(false);
            _ = auditResult; // A failed audit Result is intentionally ignored (best-effort).
        }
        catch (Exception)
        {
            // Best-effort audit only. Intentionally swallowed; the sale is already durable.
        }
    }

    private RemotePosCartResponse BuildCartResponse(RemotePosOperatorSession session, RemotePosCart cart, string message)
    {
        List<RemotePosCartLineResponse> lines;
        long subtotal = 0;

        lock (cart.Sync)
        {
            lines = new List<RemotePosCartLineResponse>(cart.Lines.Count);
            foreach (RemotePosCartLine line in cart.Lines)
            {
                long lineTotal = checked((long)Math.Round(line.UnitPriceCents * (line.QuantityX1000 / 1000m), 0, MidpointRounding.AwayFromZero));
                subtotal = checked(subtotal + lineTotal);
                lines.Add(new RemotePosCartLineResponse(
                    line.ProductId.ToString(),
                    line.ProductName,
                    line.UnitName,
                    line.QuantityX1000,
                    line.UnitPriceCents,
                    lineTotal));
            }
        }

        return new RemotePosCartResponse(cart.CartId.ToString(), session.SessionId.ToString(), lines, subtotal, message);
    }

    private static RemotePosShiftResponse BuildShiftResponse(CashierSessionReadModel session) =>
        new("Open", session.Id.ToString(), session.OpenedAt.ToString("O"), "Open cashier shift.");

    private static string BuildIdempotencyKey(RemotePosOperatorSession session, string clientRequestId) =>
        $"{session.StoreId:N}:{session.TerminalId:N}:{clientRequestId}";

    private async Task<RemotePosCheckoutResultResponse> ReconcileRecordAsync(
        RemotePosCheckoutRequestRecord record,
        CancellationToken cancellationToken)
    {
        if (record.State == RemotePosCheckoutRequestStore.Completed &&
            !string.IsNullOrWhiteSpace(record.SaleId) &&
            record.TotalCents.HasValue &&
            record.ChangeCents.HasValue)
        {
            return new RemotePosCheckoutResultResponse(
                RemotePosCheckoutStatus.Completed,
                record.ClientRequestId,
                record.ReceiptNumber,
                record.SaleId,
                record.TotalCents,
                record.ChangeCents,
                false,
                record.Message);
        }

        Result<SaleDetailReadModel?> saleResult = await _saleReadRepository
            .FindSaleByReceiptNoAsync(record.ReceiptNumber, cancellationToken)
            .ConfigureAwait(false);
        SaleDetailReadModel? sale = saleResult.IsSuccess ? saleResult.Value : null;
        if (sale is null)
        {
            return new RemotePosCheckoutResultResponse(
                RemotePosCheckoutStatus.PendingConfirmation,
                record.ClientRequestId,
                record.ReceiptNumber,
                null,
                null,
                null,
                false,
                "Checkout is not committed; the original request may be retried with the same request id.");
        }

        const string message = "Remote sale completed on host.";
        await _requestStore.MarkCompletedAsync(
                record,
                sale.Id.ToString(),
                sale.Total.Cents,
                sale.Change.Cents,
                message,
                cancellationToken)
            .ConfigureAwait(false);
        return new RemotePosCheckoutResultResponse(
            RemotePosCheckoutStatus.Completed,
            record.ClientRequestId,
            record.ReceiptNumber,
            sale.Id.ToString(),
            sale.Total.Cents,
            sale.Change.Cents,
            false,
            message);
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
            if (gate.Renters == 0
                && _idempotencyGates.TryGetValue(key, out IdempotencyGate? current)
                && ReferenceEquals(current, gate))
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

    private static string BuildRequestHash(
        Guid cartId,
        IReadOnlyList<PosCheckoutLineInput> lines,
        IReadOnlyList<PosCheckoutPaymentInput> payments)
    {
        StringBuilder canonical = new();
        canonical.Append(cartId.ToString("N"));
        foreach (PosCheckoutLineInput line in lines.OrderBy(line => line.ProductId))
        {
            canonical.Append('|').Append(line.ProductId.ToString("N")).Append(':').Append(line.QuantityX1000);
        }
        foreach (PosCheckoutPaymentInput payment in payments)
        {
            canonical.Append('|').Append(payment.Method).Append(':').Append(payment.AmountCents);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static RemotePosCartOperationResult Ok(RemotePosCartResponse cart) =>
        new(true, "RemotePos.Cart.Ok", cart.Message, cart);

    private static RemotePosCartOperationResult Fail(string status, string code, string message) =>
        new(false, code, message, null);

    private static RemotePosCheckoutResultResponse Result(string status, string clientRequestId, string message, bool duplicate) =>
        new(status, clientRequestId, null, null, null, null, duplicate, message);

    private sealed class IdempotencyGate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int Renters { get; set; }
    }

    private sealed class RemotePosCart
    {
        public RemotePosCart(Guid cartId)
        {
            CartId = cartId;
        }

        public Guid CartId { get; }

        public object Sync { get; } = new();

        public List<RemotePosCartLine> Lines { get; } = new();
    }

    private sealed class RemotePosCartLine
    {
        public RemotePosCartLine(Guid productId, string productName, string unitName, long quantityX1000, long unitPriceCents)
        {
            ProductId = productId;
            ProductName = productName;
            UnitName = unitName;
            QuantityX1000 = quantityX1000;
            UnitPriceCents = unitPriceCents;
        }

        public Guid ProductId { get; }

        public string ProductName { get; }

        public string UnitName { get; }

        public long QuantityX1000 { get; set; }

        public long UnitPriceCents { get; set; }
    }
}

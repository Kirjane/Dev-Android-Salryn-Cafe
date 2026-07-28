using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;

namespace Salryn.Core.Services;

public sealed record CompleteCheckoutSaleRequest(
    Sale Sale,
    IReadOnlyList<SaleItem> Items,
    IReadOnlyList<Payment> Payments,
    IReadOnlyList<StockMovement> StockMovements);

public interface IPosCheckoutService
{
    Task<Result> CompleteSaleAsync(
        CompleteCheckoutSaleRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class PosCheckoutService : IPosCheckoutService
{
    private readonly ISaleWriteRepository _saleWriteRepository;
    private readonly ICashierSessionService _cashierSessionService;

    public PosCheckoutService(
        ISaleWriteRepository saleWriteRepository,
        ICashierSessionService cashierSessionService)
    {
        _saleWriteRepository = saleWriteRepository ?? throw new ArgumentNullException(nameof(saleWriteRepository));
        _cashierSessionService = cashierSessionService ?? throw new ArgumentNullException(nameof(cashierSessionService));
    }

    public async Task<Result> CompleteSaleAsync(
        CompleteCheckoutSaleRequest request,
        CancellationToken cancellationToken = default)
    {
        Result validation = ValidateRequest(request);

        if (validation.IsFailure)
        {
            return validation;
        }

        Sale sale = request.Sale;

        Result<CashierSessionReadModel?> openSessionResult =
            await _cashierSessionService.FindOpenSessionByUserIdAsync(
                sale.CashierUserId,
                cancellationToken);

        if (openSessionResult.IsFailure)
        {
            return Result.Fail(openSessionResult.ErrorDetail);
        }

        CashierSessionReadModel? openSession = openSessionResult.Value;

        if (openSession is null)
        {
            return Result.Fail(Error.Conflict(
                "Checkout.OpenCashierSessionRequired",
                "An open cashier session is required before completing a sale."));
        }

        if (openSession.UserId != sale.CashierUserId)
        {
            return Result.Fail(Error.Unexpected(
                "Checkout.OpenSessionUserMismatch",
                "Open cashier session user does not match the sale cashier."));
        }

        if (sale.CashierSessionId.HasValue &&
            sale.CashierSessionId.Value != openSession.Id)
        {
            return Result.Fail(Error.Validation(
                "Checkout.CashierSessionMismatch",
                "Sale cashier session must match the currently open cashier session."));
        }

        Sale enforcedSale = sale.CashierSessionId == openSession.Id
            ? sale
            : CloneSaleWithCashierSession(sale, openSession.Id);

        return await _saleWriteRepository.AddCompletedSaleAsync(
            enforcedSale,
            request.Items,
            request.Payments,
            request.StockMovements,
            cancellationToken);
    }

    private static Result ValidateRequest(CompleteCheckoutSaleRequest request)
    {
        if (request.Sale is null)
        {
            return Result.Fail(Error.Validation(
                "Checkout.SaleRequired",
                "Sale is required."));
        }

        if (request.Items is null)
        {
            return Result.Fail(Error.Validation(
                "Checkout.SaleItemsRequired",
                "Sale items are required."));
        }

        if (request.Payments is null)
        {
            return Result.Fail(Error.Validation(
                "Checkout.PaymentsRequired",
                "Sale payments are required."));
        }

        if (request.StockMovements is null)
        {
            return Result.Fail(Error.Validation(
                "Checkout.StockMovementsRequired",
                "Sale stock movements are required."));
        }

        if (request.Sale.CashierUserId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "Checkout.InvalidCashierUserId",
                "Cashier user ID is required before completing a sale."));
        }

        return Result.Success();
    }

    private static Sale CloneSaleWithCashierSession(Sale sale, Guid cashierSessionId)
    {
        return new Sale
        {
            Id = sale.Id,
            ReceiptNumber = sale.ReceiptNumber,
            SaleDate = sale.SaleDate,
            CashierUserId = sale.CashierUserId,
            CashierSessionId = cashierSessionId,
            Subtotal = sale.Subtotal,
            Discount = sale.Discount,
            Tax = sale.Tax,
            Total = sale.Total,
            AmountPaid = sale.AmountPaid,
            Change = sale.Change,
            CustomerId = sale.CustomerId,
            PaymentStatus = sale.PaymentStatus,
            BalanceDue = sale.BalanceDue,
            ReceivableDueDate = sale.ReceivableDueDate,
            ReceivableNote = sale.ReceivableNote,
            CustomerFullNameSnapshot = sale.CustomerFullNameSnapshot,
            CustomerPhoneSnapshot = sale.CustomerPhoneSnapshot,
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
}

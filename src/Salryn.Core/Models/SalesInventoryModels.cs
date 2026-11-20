using Salryn.Core.ValueObjects;

namespace Salryn.Core.Models;

public sealed class Cart
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid CashierUserId { get; init; }

    public List<CartLine> Lines { get; init; } = new();

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class CartLine
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public ProductSnapshot Product { get; init; } = default!;

    public Quantity Quantity { get; init; }

    public Money UnitPrice { get; init; }

    public Money Discount { get; init; } = Money.Zero;

    public Money LineTotal { get; init; }
}

public sealed class Sale
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string ReceiptNumber { get; init; }

    public DateTimeOffset SaleDate { get; init; } = DateTimeOffset.UtcNow;

    public Guid CashierUserId { get; init; }

    public Guid? CashierSessionId { get; init; }

    public Money Subtotal { get; init; }

    public Money Discount { get; init; } = Money.Zero;

    public Money Tax { get; init; } = Money.Zero;

    public Money FeeTotal { get; init; } = Money.Zero;

    public Money Total { get; init; }

    public Money AmountPaid { get; init; }

    public Money Change { get; init; }

    public Guid? CustomerId { get; init; }

    public SalePaymentStatus PaymentStatus { get; init; } = SalePaymentStatus.Paid;

    public Money BalanceDue { get; init; } = Money.Zero;

    public DateOnly? ReceivableDueDate { get; init; }

    public string? ReceivableNote { get; init; }

    public string? CustomerFullNameSnapshot { get; init; }

    public string? CustomerPhoneSnapshot { get; init; }

    public SaleStatus Status { get; init; } = SaleStatus.Completed;

    public Guid? VoidedByUserId { get; init; }

    public string? VoidReason { get; init; }

    public DateTimeOffset? VoidedAt { get; init; }

    public string? ReceiptSnapshot { get; init; }

    public List<SaleItem> Items { get; init; } = new();

    public List<Payment> Payments { get; init; } = new();

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class SaleItem
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid SaleId { get; init; }

    public Guid ProductId { get; init; }

    public required string ProductNameSnapshot { get; init; }

    public string? BarcodeSnapshot { get; init; }

    public required string UnitNameSnapshot { get; init; }

    public Quantity Quantity { get; init; }

    public Money UnitPrice { get; init; }

    public Money? CostPrice { get; init; }

    public Money Discount { get; init; } = Money.Zero;

    public Money LineTotal { get; init; }
}

public sealed class Payment
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid SaleId { get; init; }

    public PaymentMethod Method { get; init; } = PaymentMethod.Cash;

    public Money Amount { get; init; }

    public DateTimeOffset PaidAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class StockMovement
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid ProductId { get; init; }

    public StockMovementType MovementType { get; init; }

    public Quantity QuantityChange { get; init; }

    public Money? CostPrice { get; init; }

    public string? ReferenceType { get; init; }

    public Guid? ReferenceId { get; init; }

    public string? Reason { get; init; }

    public Guid UserId { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class CashierSession
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid UserId { get; init; }

    public DateTimeOffset OpenedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ClosedAt { get; init; }

    public Money OpeningCash { get; init; } = Money.Zero;

    public Money ExpectedCash { get; init; } = Money.Zero;

    public Money? CountedCash { get; init; }

    public Money Variance { get; init; } = Money.Zero;

    public CashierSessionStatus Status { get; init; } = CashierSessionStatus.Open;

    public string? Notes { get; init; }
}

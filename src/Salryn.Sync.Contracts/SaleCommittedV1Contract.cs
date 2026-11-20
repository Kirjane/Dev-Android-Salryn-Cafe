namespace Salryn.Sync.Contracts;

public static class SaleCommittedV1Contract
{
    public const string OperationType = "SaleCommittedV1";
    public const int SchemaVersion = 1;

    public const string PaymentCash = "Cash";
    public const string PaymentAccountsReceivable = "AccountsReceivable";

    public const string PaymentStatusPaid = "Paid";
    public const string PaymentStatusCredit = "Credit";
    public const string PaymentStatusPartial = "Partial";

    public const string StockMovementSale = "Sale";
    public const string StockReferenceSale = "SALE";

    public const string Applied = "Applied";
    public const string Rejected = "Rejected";
    public const string NeedsReview = "NeedsReview";

    public static bool IsFinalResolution(string? value) =>
        string.Equals(value, Applied, StringComparison.Ordinal) ||
        string.Equals(value, Rejected, StringComparison.Ordinal) ||
        string.Equals(value, NeedsReview, StringComparison.Ordinal);
}

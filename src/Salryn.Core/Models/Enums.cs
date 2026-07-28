namespace Salryn.Core.Models;

public enum UserStatus
{
    Active = 1,
    Inactive = 2,
    Locked = 3
}

public enum SaleStatus
{
    Draft = 1,
    Completed = 2,
    Voided = 3
}

public enum PaymentMethod
{
    Cash = 1,
    AccountsReceivable = 2
}

public enum StockMovementType
{
    Sale = 1,
    VoidSale = 2,
    StockIn = 3,
    AdjustmentAdd = 4,
    AdjustmentRemove = 5,
    Damage = 6,
    Expired = 7,
    CountCorrection = 8,
    /// <summary>M4-RC2: sellable restock from customer return (not a void of original sale).</summary>
    ReturnRestock = 9
}

public enum PrintJobStatus
{
    Pending = 1,
    Printed = 2,
    Failed = 3
}

public enum CashierSessionStatus
{
    Open = 1,
    Closed = 2,
    ForceClosed = 3
}

public enum ImportExportJobType
{
    ProductImport = 1,
    ProductExport = 2,
    SalesExport = 3
}

public enum JobStatus
{
    Pending = 1,
    Completed = 2,
    Failed = 3
}

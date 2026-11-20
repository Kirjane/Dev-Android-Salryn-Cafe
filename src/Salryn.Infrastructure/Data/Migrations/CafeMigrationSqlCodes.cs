namespace Salryn.Infrastructure.Data.Migrations;

/// <summary>
/// Stable integer and string SQL constants used by ported Yza v25-v39 migrations so
/// schema DDL remains self-contained and free of cross-lane type collisions.
/// Values are frozen to archive/yza/main (4241ea1ec0a8cebf552d93f25a7ac134d7d4d6f3).
/// </summary>
internal static class CafeMigrationSqlCodes
{
    internal static class CashMovementType
    {
        public const int SaleCashReceipt = 1;
        public const int ReceivableCashCollection = 2;
        public const int CashIn = 3;
        public const int InventoryPurchase = 4;
        public const int OperationalExpense = 5;
        public const int CashRefund = 6;
        public const int CashOut = 7;
        public const int Reversal = 8;
    }

    internal static class CafePrinterStationKind
    {
        public const int CustomerReceipt = 1;
        public const int Kitchen = 2;
        public const int BarDrinks = 3;
        public const int OtherPreparation = 4;
    }

    internal static class CafePrintDocumentKind
    {
        public const int CustomerReceipt = 1;
        public const int KitchenTicket = 2;
    }

    internal static class CafePrintCopyKind
    {
        public const int Original = 1;
        public const int Reprint = 2;
        public const int VoidedReprint = 3;
    }

    internal static class CafePrintAttemptStatus
    {
        public const int Succeeded = 1;
        public const int Failed = 2;
        public const int DisabledUnavailable = 3;
    }

    internal static class CafeBookingCashReferenceTypes
    {
        public const string DepositReceipt = "BOOKING_DEPOSIT_RECEIPT";
        public const string DepositRefund = "BOOKING_DEPOSIT_REFUND";
    }
}

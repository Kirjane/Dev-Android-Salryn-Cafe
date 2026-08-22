namespace Salryn.Core.Services;

/// <summary>
/// Stable searchable operator audit action names for PRE-I audit trail coverage.
/// </summary>
public static class OperatorAuditActions
{
    public const string SaleCompleted = "SALE_COMPLETED";
    public const string ProductCreated = "PRODUCT_CREATED";
    public const string ProductUpdated = "PRODUCT_UPDATED";
    public const string ProductDeactivated = "PRODUCT_DEACTIVATED";
    public const string StockInRecorded = "STOCK_IN_RECORDED";
    public const string StockAdjustmentRecorded = "STOCK_ADJUSTMENT_RECORDED";
    public const string TerminalPaired = "TERMINAL_PAIRED";
    public const string TerminalDisabled = "TERMINAL_DISABLED";
    public const string TerminalEnabled = "TERMINAL_ENABLED";
    public const string TerminalRemoved = "TERMINAL_REMOVED";
    public const string TerminalSessionsCleared = "TERMINAL_SESSIONS_CLEARED";
    public const string BackupCreated = "BACKUP_CREATED";
    public const string BackupRestored = "BACKUP_RESTORED";
    public const string LicenseInstalled = "LICENSE_INSTALLED";
    public const string LicenseReplaced = "LICENSE_REPLACED";
    public const string StoreSettingsUpdated = "STORE_SETTINGS_UPDATED";
    public const string UserLogin = "USER_LOGIN";
    public const string UserLogout = "USER_LOGOUT";
}

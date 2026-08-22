namespace Salryn.Api.Local.Contracts;

public sealed record StoreSettingsResponse(
    string Id, string StoreName, string? BusinessName, string? Address,
    string? ContactNumber, string? Tin, string CurrencyLabel, int ReceiptWidthMm,
    string? ReceiptHeader, string? ReceiptFooter, string PrinterMode, string? WindowsPrinterName,
    bool HasLogo = false, string? LogoContentType = null);

public sealed record StoreSettingsUpdateApiRequest(
    string StoreName, string? BusinessName, string? Address, string? ContactNumber,
    string? Tin, string CurrencyLabel, int ReceiptWidthMm,
    string? ReceiptHeader, string? ReceiptFooter, string PrinterMode, string? WindowsPrinterName);

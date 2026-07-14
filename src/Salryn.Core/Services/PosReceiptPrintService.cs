using Salryn.Core.Common;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;

namespace Salryn.Core.Services;

public static class PosReceiptPrintStatuses
{
    public const string NotAttempted = "NotAttempted";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string DisabledUnavailable = "Disabled/Unavailable";
}

public static class PosReceiptPrintSources
{
    public const string PersistedReceiptSnapshot = "PersistedReceiptSnapshot";
    public const string TestPrintSample = "TestPrintSample";
    public const string None = "None";
}

public static class ReceiptPrinterModes
{
    public const string Disabled = "Disabled";
    public const string WindowsPrinter = "WindowsPrinter";
}

public sealed record ReceiptPrinterAdapterResult(
    bool Succeeded,
    string Status,
    string Message,
    string? ErrorMessage = null)
{
    public static ReceiptPrinterAdapterResult Success(string message) =>
        new(true, PosReceiptPrintStatuses.Succeeded, message, null);

    public static ReceiptPrinterAdapterResult Failed(string message, string? errorMessage = null) =>
        new(false, PosReceiptPrintStatuses.Failed, message, errorMessage);

    public static ReceiptPrinterAdapterResult Disabled(string message) =>
        new(false, PosReceiptPrintStatuses.DisabledUnavailable, message, null);
}

public sealed record PosReceiptPrintResult(
    bool Succeeded,
    string Status,
    Guid? SaleId,
    string? ReceiptNumber,
    string Source,
    string AdapterName,
    string Message,
    string? ErrorMessage,
    DateTimeOffset? PrintedAtUtc)
{
    public static PosReceiptPrintResult NotAttempted(
        Guid? saleId,
        string? receiptNumber,
        string source,
        string adapterName,
        string message) =>
        new(false, PosReceiptPrintStatuses.NotAttempted, saleId, receiptNumber, source, adapterName, message, null, null);

    public static PosReceiptPrintResult Disabled(
        Guid? saleId,
        string? receiptNumber,
        string source,
        string adapterName,
        string message) =>
        new(false, PosReceiptPrintStatuses.DisabledUnavailable, saleId, receiptNumber, source, adapterName, message, null, null);

    public static PosReceiptPrintResult Failed(
        Guid? saleId,
        string? receiptNumber,
        string source,
        string adapterName,
        string message,
        string? errorMessage) =>
        new(false, PosReceiptPrintStatuses.Failed, saleId, receiptNumber, source, adapterName, message, errorMessage, null);

    public static PosReceiptPrintResult Printed(
        Guid? saleId,
        string? receiptNumber,
        string source,
        string adapterName,
        string message,
        DateTimeOffset printedAtUtc) =>
        new(true, PosReceiptPrintStatuses.Succeeded, saleId, receiptNumber, source, adapterName, message, null, printedAtUtc);
}

public interface IReceiptPrinterAdapter
{
    string AdapterName { get; }

    bool IsEnabled { get; }

    Task<ReceiptPrinterAdapterResult> PrintAsync(
        string jobName,
        string receiptText,
        CancellationToken cancellationToken = default);
}

public sealed class DisabledReceiptPrinterAdapter : IReceiptPrinterAdapter
{
    private readonly string _message;

    public DisabledReceiptPrinterAdapter(
        string? message = null,
        string? adapterName = null)
    {
        _message = string.IsNullOrWhiteSpace(message)
            ? "Receipt printing is disabled or unavailable on this workstation."
            : message.Trim();
        AdapterName = string.IsNullOrWhiteSpace(adapterName)
            ? "DisabledReceiptPrinterAdapter"
            : adapterName.Trim();
    }

    public string AdapterName { get; }

    public bool IsEnabled => false;

    public Task<ReceiptPrinterAdapterResult> PrintAsync(
        string jobName,
        string receiptText,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReceiptPrinterAdapterResult.Disabled(_message));
    }
}

public sealed class NoOpReceiptPrinterAdapter : IReceiptPrinterAdapter
{
    public NoOpReceiptPrinterAdapter(string adapterName = "NoOpReceiptPrinterAdapter")
    {
        AdapterName = string.IsNullOrWhiteSpace(adapterName) ? "NoOpReceiptPrinterAdapter" : adapterName.Trim();
    }

    public string AdapterName { get; }

    public bool IsEnabled => true;

    public string? LastJobName { get; private set; }

    public string? LastReceiptText { get; private set; }

    public Task<ReceiptPrinterAdapterResult> PrintAsync(
        string jobName,
        string receiptText,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastJobName = jobName;
        LastReceiptText = receiptText;
        return Task.FromResult(ReceiptPrinterAdapterResult.Success(
            "Receipt print accepted by no-op adapter."));
    }
}

public sealed class PosReceiptPrintService
{
    private readonly ISaleReadRepository _saleReadRepository;
    private readonly Func<IReceiptPrinterAdapter> _printerAdapterFactory;

    public PosReceiptPrintService(
        ISaleReadRepository saleReadRepository,
        IReceiptPrinterAdapter printerAdapter)
        : this(saleReadRepository, () => printerAdapter)
    {
    }

    public PosReceiptPrintService(
        ISaleReadRepository saleReadRepository,
        Func<IReceiptPrinterAdapter> printerAdapterFactory)
    {
        _saleReadRepository = saleReadRepository ?? throw new ArgumentNullException(nameof(saleReadRepository));
        _printerAdapterFactory = printerAdapterFactory ?? throw new ArgumentNullException(nameof(printerAdapterFactory));
    }

    public async Task<PosReceiptPrintResult> PrintSaleReceiptAsync(
        Guid saleId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReceiptPrinterAdapter printerAdapter = CreatePrinterAdapter();

        if (saleId == Guid.Empty)
        {
            return PosReceiptPrintResult.NotAttempted(
                null,
                null,
                PosReceiptPrintSources.None,
                printerAdapter.AdapterName,
                "Sale id is required before printing can be attempted.");
        }

        Result<SaleDetailReadModel?> saleResult = await _saleReadRepository
            .FindSaleByIdAsync(saleId, cancellationToken)
            .ConfigureAwait(false);

        if (saleResult.IsFailure)
        {
            return PosReceiptPrintResult.Failed(
                saleId,
                null,
                PosReceiptPrintSources.None,
                printerAdapter.AdapterName,
                "Sale was not modified. Receipt print lookup failed.",
                saleResult.ErrorDetail.Message);
        }

        SaleDetailReadModel? sale = saleResult.Value;
        if (sale is null)
        {
            return PosReceiptPrintResult.Failed(
                saleId,
                null,
                PosReceiptPrintSources.None,
                printerAdapter.AdapterName,
                "Sale was not modified. Sale was not found for receipt printing.",
                "Sale not found.");
        }

        string? receiptSnapshot = sale.ReceiptSnapshot;
        if (string.IsNullOrWhiteSpace(receiptSnapshot))
        {
            return PosReceiptPrintResult.NotAttempted(
                sale.Id,
                sale.ReceiptNo,
                PosReceiptPrintSources.None,
                printerAdapter.AdapterName,
                "Sale was not modified. No persisted receipt snapshot exists for this sale.");
        }

        return await PrintPersistedTextAsync(
                printerAdapter,
                sale.Id,
                sale.ReceiptNo,
                PosReceiptPrintSources.PersistedReceiptSnapshot,
                "POS receipt " + sale.ReceiptNo,
                receiptSnapshot,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<PosReceiptPrintResult> TestPrintAsync(CancellationToken cancellationToken = default)
    {
        IReceiptPrinterAdapter printerAdapter = CreatePrinterAdapter();
        string text = string.Join(Environment.NewLine, new[]
        {
            "SALRYN POS TEST PRINT",
            "Printer adapter boundary check only.",
            "This is not an official receipt.",
            "No sale, payment, or stock movement is created.",
            "Generated UTC: " + DateTimeOffset.UtcNow.ToString("O")
        });

        return PrintPersistedTextAsync(
            printerAdapter,
            null,
            null,
            PosReceiptPrintSources.TestPrintSample,
            "Salryn test print",
            text,
            cancellationToken);
    }

    private IReceiptPrinterAdapter CreatePrinterAdapter()
    {
        try
        {
            return _printerAdapterFactory() ?? new DisabledReceiptPrinterAdapter(
                "Receipt printing is unavailable because no printer adapter was created.",
                "ReceiptPrinterAdapterFactory.Empty");
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return new DisabledReceiptPrinterAdapter(
                "Receipt printing is unavailable because printer settings could not be resolved: " + ex.Message,
                "ReceiptPrinterAdapterFactory.Error");
        }
    }

    private async Task<PosReceiptPrintResult> PrintPersistedTextAsync(
        IReceiptPrinterAdapter printerAdapter,
        Guid? saleId,
        string? receiptNumber,
        string source,
        string jobName,
        string receiptText,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(receiptText))
        {
            return PosReceiptPrintResult.NotAttempted(
                saleId,
                receiptNumber,
                PosReceiptPrintSources.None,
                printerAdapter.AdapterName,
                "No receipt text was available for printing.");
        }

        if (!printerAdapter.IsEnabled)
        {
            try
            {
                ReceiptPrinterAdapterResult unavailable = await printerAdapter
                    .PrintAsync(jobName, receiptText, cancellationToken)
                    .ConfigureAwait(false);

                return PosReceiptPrintResult.Disabled(
                    saleId,
                    receiptNumber,
                    source,
                    printerAdapter.AdapterName,
                    unavailable.Message);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
            {
                return PosReceiptPrintResult.Disabled(
                    saleId,
                    receiptNumber,
                    source,
                    printerAdapter.AdapterName,
                    "Receipt printing is disabled or unavailable on this workstation. Sale state is unchanged. " + ex.Message);
            }
        }

        try
        {
            ReceiptPrinterAdapterResult adapterResult = await printerAdapter
                .PrintAsync(jobName, receiptText, cancellationToken)
                .ConfigureAwait(false);

            if (adapterResult.Succeeded)
            {
                return PosReceiptPrintResult.Printed(
                    saleId,
                    receiptNumber,
                    source,
                    printerAdapter.AdapterName,
                    adapterResult.Message,
                    DateTimeOffset.UtcNow);
            }

            if (string.Equals(adapterResult.Status, PosReceiptPrintStatuses.DisabledUnavailable, StringComparison.Ordinal))
            {
                return PosReceiptPrintResult.Disabled(
                    saleId,
                    receiptNumber,
                    source,
                    printerAdapter.AdapterName,
                    adapterResult.Message);
            }

            return PosReceiptPrintResult.Failed(
                saleId,
                receiptNumber,
                source,
                printerAdapter.AdapterName,
                adapterResult.Message,
                adapterResult.ErrorMessage);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return PosReceiptPrintResult.Failed(
                saleId,
                receiptNumber,
                source,
                printerAdapter.AdapterName,
                "Receipt print failed. Sale state is unchanged.",
                ex.GetType().Name + ": " + ex.Message);
        }
    }
}
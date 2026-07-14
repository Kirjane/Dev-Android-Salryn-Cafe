using Salryn.Core.ReadModels;

namespace Salryn.Core.Services;

public static class ReceiptPrinterAdapterFactory
{
    public static IReceiptPrinterAdapter Create(StoreSettingsReadModel? settings) =>
        Create(settings, printerName => new WindowsQueueReceiptPrinterAdapter(printerName));

    public static IReceiptPrinterAdapter Create(
        StoreSettingsReadModel? settings,
        Func<string, IReceiptPrinterAdapter> windowsPrinterFactory)
    {
        ArgumentNullException.ThrowIfNull(windowsPrinterFactory);

        if (settings is null)
        {
            return new DisabledReceiptPrinterAdapter(
                "Receipt printing is unavailable because store printer settings were not found.",
                "ReceiptPrinterSettings.NotFound");
        }

        string mode = string.IsNullOrWhiteSpace(settings.PrinterMode)
            ? ReceiptPrinterModes.Disabled
            : settings.PrinterMode.Trim();

        if (string.Equals(mode, ReceiptPrinterModes.Disabled, StringComparison.Ordinal))
        {
            return new DisabledReceiptPrinterAdapter(
                "Receipt printing is disabled in Store Settings. Sale state is unchanged.");
        }

        if (string.Equals(mode, ReceiptPrinterModes.WindowsPrinter, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(settings.WindowsPrinterName))
            {
                return new DisabledReceiptPrinterAdapter(
                    "Windows printer mode is selected, but no Windows printer name is configured in Store Settings.",
                    "WindowsQueueReceiptPrinterAdapter.Unconfigured");
            }

            return windowsPrinterFactory(settings.WindowsPrinterName.Trim());
        }

        return new DisabledReceiptPrinterAdapter(
            "Receipt printing is unavailable because Store Settings contains an unsupported printer mode.",
            "ReceiptPrinterSettings.InvalidMode");
    }
}

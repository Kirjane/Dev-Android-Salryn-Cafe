using System.Diagnostics;
using System.Text;

namespace Salryn.Core.Services;

public sealed class WindowsQueueReceiptPrinterAdapter : IReceiptPrinterAdapter
{
    private static readonly TimeSpan PrintSubmitTimeout = TimeSpan.FromSeconds(30);
    private readonly string? _printerName;

    public WindowsQueueReceiptPrinterAdapter(string? printerName)
    {
        _printerName = string.IsNullOrWhiteSpace(printerName) ? null : printerName.Trim();
    }

    public string AdapterName => string.IsNullOrWhiteSpace(_printerName)
        ? "WindowsQueueReceiptPrinterAdapter.Unconfigured"
        : "WindowsQueueReceiptPrinterAdapter";

    public bool IsEnabled => OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(_printerName);

    public async Task<ReceiptPrinterAdapterResult> PrintAsync(
        string jobName,
        string receiptText,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return ReceiptPrinterAdapterResult.Disabled(
                "Windows printer mode requires a Windows host. Sale state is unchanged.");
        }

        if (string.IsNullOrWhiteSpace(_printerName))
        {
            return ReceiptPrinterAdapterResult.Disabled(
                "Windows printer mode is selected, but no Windows printer name is configured in Store Settings.");
        }

        if (string.IsNullOrWhiteSpace(receiptText))
        {
            return ReceiptPrinterAdapterResult.Disabled(
                "No receipt text was available for Windows printer submission.");
        }

        string job = string.IsNullOrWhiteSpace(jobName) ? "Salryn receipt" : jobName.Trim();
        string spoolRoot = Path.Combine(Path.GetTempPath(), "Salryn", "WindowsPrintQueue");
        Directory.CreateDirectory(spoolRoot);
        string receiptPath = Path.Combine(spoolRoot, SanitizeFileName(job) + "-" + Guid.NewGuid().ToString("N") + ".txt");

        try
        {
            await File.WriteAllTextAsync(receiptPath, receiptText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken)
                .ConfigureAwait(false);

            using Process process = StartNotepadPrintTo(receiptPath, _printerName);
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(PrintSubmitTimeout);

            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                return ReceiptPrinterAdapterResult.Failed(
                    "Windows printer submission timed out before the print handler returned. Sale state is unchanged.",
                    "Print submission timeout after " + PrintSubmitTimeout.TotalSeconds.ToString("0") + " seconds.");
            }

            if (process.ExitCode == 0)
            {
                return ReceiptPrinterAdapterResult.Success(
                    "Receipt submitted to the Windows print queue for printer '" + _printerName + "'. This does not prove paper output.");
            }

            return ReceiptPrinterAdapterResult.Failed(
                "Windows print handler returned a failure. Sale state is unchanged.",
                "notepad.exe exit code " + process.ExitCode.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return ReceiptPrinterAdapterResult.Failed(
                "Windows printer submission failed. Sale state is unchanged.",
                ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            TryDelete(receiptPath);
        }
    }

    private static Process StartNotepadPrintTo(string receiptPath, string printerName)
    {
        ProcessStartInfo start = new()
        {
            FileName = "notepad.exe",
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        start.ArgumentList.Add("/pt");
        start.ArgumentList.Add(receiptPath);
        start.ArgumentList.Add(printerName);

        return Process.Start(start)
            ?? throw new InvalidOperationException("Windows print handler did not start.");
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        StringBuilder builder = new(value.Length);
        foreach (char c in value)
        {
            builder.Append(invalid.Contains(c) ? '_' : c);
        }
        return builder.Length == 0 ? "SalrynReceipt" : builder.ToString();
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

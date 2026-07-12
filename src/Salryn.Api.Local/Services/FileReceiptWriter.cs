using System.Text;
using Salryn.Core.ReadModels;

namespace Salryn.Api.Local.Services;

public interface IFileReceiptWriter
{
    string ReceiptDirectory { get; }

    Task<FileReceiptWriteResult> WriteAsync(
        SaleDetailReadModel sale,
        CancellationToken cancellationToken = default);
}

public sealed record FileReceiptWriteResult(
    string Status,
    bool Succeeded,
    string? FilePath,
    string? ErrorMessage)
{
    public static FileReceiptWriteResult Written(string filePath) =>
        new("Written", true, filePath, null);

    public static FileReceiptWriteResult NotAttempted(string reason) =>
        new("NotAttempted", false, null, reason);

    public static FileReceiptWriteResult Failed(string? filePath, string errorMessage) =>
        new("Failed", false, filePath, errorMessage);
}

public sealed class FileReceiptWriter : IFileReceiptWriter
{
    public FileReceiptWriter(string receiptDirectory)
    {
        if (string.IsNullOrWhiteSpace(receiptDirectory))
        {
            throw new ArgumentException("Receipt directory is required.", nameof(receiptDirectory));
        }

        ReceiptDirectory = receiptDirectory;
    }

    public string ReceiptDirectory { get; }

    public async Task<FileReceiptWriteResult> WriteAsync(
        SaleDetailReadModel sale,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sale);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(sale.ReceiptSnapshot))
        {
            return FileReceiptWriteResult.NotAttempted("Sale has no receipt snapshot to write.");
        }

        string? filePath = null;

        try
        {
            Directory.CreateDirectory(ReceiptDirectory);

            string fileName = BuildFileName(sale.ReceiptNo, sale.Id);
            filePath = Path.Combine(ReceiptDirectory, fileName);

            await File.WriteAllTextAsync(
                    filePath,
                    sale.ReceiptSnapshot,
                    Encoding.UTF8,
                    cancellationToken)
                .ConfigureAwait(false);

            return FileReceiptWriteResult.Written(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return FileReceiptWriteResult.Failed(
                filePath,
                ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static string BuildFileName(string receiptNo, Guid saleId)
    {
        string safeReceiptNo = Sanitize(receiptNo);
        return safeReceiptNo + "-" + saleId.ToString("N") + ".txt";
    }

    private static string Sanitize(string value)
    {
        string trimmed = string.IsNullOrWhiteSpace(value) ? "receipt" : value.Trim();
        char[] invalid = Path.GetInvalidFileNameChars();
        var chars = trimmed
            .Select(ch => invalid.Contains(ch) ? '_' : ch)
            .ToArray();

        string sanitized = new(chars);
        return string.IsNullOrWhiteSpace(sanitized) ? "receipt" : sanitized;
    }
}

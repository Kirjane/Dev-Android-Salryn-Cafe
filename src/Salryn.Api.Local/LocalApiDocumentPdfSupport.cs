using Salryn.Composition.Local;
using Salryn.Core.Models;
using Salryn.Infrastructure.Data.Repositories;

namespace Salryn.Api.Local;

internal static class LocalApiDocumentPdfSupport
{
    public static async Task<StoreSettings?> TryLoadStoreSettingsAsync(
        SalrynLocalComposition composition,
        CancellationToken cancellationToken)
    {
        SqliteRepositoryFactory repositories = new(composition.ConnectionFactory);
        var result = await repositories
            .CreateStoreSettingsReadRepository()
            .GetCurrentAsync(cancellationToken)
            .ConfigureAwait(false);
        if (result.IsFailure || result.Value is null)
        {
            return null;
        }

        var s = result.Value;
        return new StoreSettings
        {
            Id = s.Id,
            StoreName = s.StoreName,
            BusinessName = s.BusinessName,
            Address = s.Address,
            ContactNumber = s.ContactNumber,
            Tin = s.Tin,
            CurrencyLabel = s.CurrencyLabel,
            ReceiptWidthMm = s.ReceiptWidthMm,
            ReceiptHeader = s.ReceiptHeader,
            ReceiptFooter = s.ReceiptFooter,
            TaxModeEnabled = s.TaxModeEnabled,
            ShiftModeEnabled = s.ShiftModeEnabled,
        };
    }

    public static IResult PdfFile(byte[] bytes, string fileName) =>
        Results.File(bytes, "application/pdf", fileName);
}

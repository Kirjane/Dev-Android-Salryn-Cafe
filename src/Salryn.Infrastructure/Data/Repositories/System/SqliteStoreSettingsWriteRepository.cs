using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Repositories;
using Salryn.Core.Services;

namespace Salryn.Infrastructure.Data.Repositories.System;

public sealed class SqliteStoreSettingsWriteRepository :
    SqliteRepositoryBase,
    IStoreSettingsWriteRepository
{
    public SqliteStoreSettingsWriteRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public Task<Result> UpdateAsync(
        StoreSettingsUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result validation = Validate(request);

        if (validation.IsFailure)
        {
            return Task.FromResult(validation);
        }

        try
        {
            using var connection = CreateOpenConnection();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText = """
                UPDATE store_settings
                SET
                    store_name = $storeName,
                    business_name = $businessName,
                    address = $address,
                    contact_number = $contactNumber,
                    tin = $tin,
                    currency_label = $currencyLabel,
                    receipt_width_mm = $receiptWidthMm,
                    tax_mode_enabled = $taxModeEnabled,
                    shift_mode_enabled = $shiftModeEnabled,
                    receipt_header = $receiptHeader,
                    receipt_footer = $receiptFooter,
                    printer_mode = $printerMode,
                    windows_printer_name = $windowsPrinterName,
                    updated_at = $updatedAt
                WHERE id = $id;
                """;

            command.Parameters.AddWithValue("$id", request.StoreSettingsId.ToString("D"));
            command.Parameters.AddWithValue("$storeName", request.StoreName.Trim());
            command.Parameters.AddWithValue("$businessName", DbNullableText(request.BusinessName));
            command.Parameters.AddWithValue("$address", DbNullableText(request.Address));
            command.Parameters.AddWithValue("$contactNumber", DbNullableText(request.ContactNumber));
            command.Parameters.AddWithValue("$tin", DbNullableText(request.Tin));
            command.Parameters.AddWithValue("$currencyLabel", request.CurrencyLabel.Trim());
            command.Parameters.AddWithValue("$receiptWidthMm", request.ReceiptWidthMm);
            command.Parameters.AddWithValue("$taxModeEnabled", request.TaxModeEnabled ? 1 : 0);
            command.Parameters.AddWithValue("$shiftModeEnabled", request.ShiftModeEnabled ? 1 : 0);
            command.Parameters.AddWithValue("$receiptHeader", DbNullableText(request.ReceiptHeader));
            command.Parameters.AddWithValue("$receiptFooter", DbNullableText(request.ReceiptFooter));
            command.Parameters.AddWithValue("$printerMode", request.PrinterMode.Trim());
            command.Parameters.AddWithValue("$windowsPrinterName", DbNullableText(request.WindowsPrinterName));
            command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));

            int affectedRows = command.ExecuteNonQuery();

            if (affectedRows == 0)
            {
                transaction.Rollback();

                return Task.FromResult(Result.Fail(Error.NotFound(
                    "StoreSettings.NotFound",
                    "Store settings were not found.")));
            }

            transaction.Commit();

            return Task.FromResult(Result.Success());
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Task.FromResult(Unexpected("StoreSettings.UpdateFailed", ex));
        }
    }

    public Task<Result> SetLogoAsync(
        Guid storeSettingsId,
        string contentType,
        byte[] bytes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (storeSettingsId == Guid.Empty)
        {
            return Task.FromResult(Result.Fail(Error.Validation("StoreSettings.InvalidId", "Store settings ID is required.")));
        }

        if (string.IsNullOrWhiteSpace(contentType) || bytes is null || bytes.Length == 0)
        {
            return Task.FromResult(Result.Fail(Error.Validation("StoreSettings.LogoRequired", "Logo content is required.")));
        }

        try
        {
            using var connection = CreateOpenConnection();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE store_settings
                SET logo_content_type = $contentType,
                    logo_bytes = $bytes,
                    updated_at = $updatedAt
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$id", storeSettingsId.ToString("D"));
            command.Parameters.AddWithValue("$contentType", contentType.Trim());
            command.Parameters.AddWithValue("$bytes", bytes);
            command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
            int affected = command.ExecuteNonQuery();
            if (affected == 0)
            {
                transaction.Rollback();
                return Task.FromResult(Result.Fail(Error.NotFound("StoreSettings.NotFound", "Store settings were not found.")));
            }

            transaction.Commit();
            return Task.FromResult(Result.Success());
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Task.FromResult(Unexpected("StoreSettings.SetLogoFailed", ex));
        }
    }

    public Task<Result> ClearLogoAsync(
        Guid storeSettingsId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (storeSettingsId == Guid.Empty)
        {
            return Task.FromResult(Result.Fail(Error.Validation("StoreSettings.InvalidId", "Store settings ID is required.")));
        }

        try
        {
            using var connection = CreateOpenConnection();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE store_settings
                SET logo_content_type = NULL,
                    logo_bytes = NULL,
                    updated_at = $updatedAt
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$id", storeSettingsId.ToString("D"));
            command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
            int affected = command.ExecuteNonQuery();
            if (affected == 0)
            {
                transaction.Rollback();
                return Task.FromResult(Result.Fail(Error.NotFound("StoreSettings.NotFound", "Store settings were not found.")));
            }

            transaction.Commit();
            return Task.FromResult(Result.Success());
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Task.FromResult(Unexpected("StoreSettings.ClearLogoFailed", ex));
        }
    }

    private static Result Validate(StoreSettingsUpdateRequest request)
    {
        if (request.StoreSettingsId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "StoreSettings.InvalidId",
                "Store settings ID is required."));
        }

        if (string.IsNullOrWhiteSpace(request.StoreName))
        {
            return Result.Fail(Error.Validation(
                "StoreSettings.StoreNameRequired",
                "Store name is required."));
        }

        if (string.IsNullOrWhiteSpace(request.CurrencyLabel))
        {
            return Result.Fail(Error.Validation(
                "StoreSettings.CurrencyRequired",
                "Currency label is required."));
        }

        if (request.ReceiptWidthMm is not 58 and not 80)
        {
            return Result.Fail(Error.Validation(
                "StoreSettings.InvalidReceiptWidth",
                "Receipt width must be 58 or 80 millimeters."));
        }

        if (request.PrinterMode is not ReceiptPrinterModes.Disabled and not ReceiptPrinterModes.WindowsPrinter)
        {
            return Result.Fail(Error.Validation(
                "StoreSettings.InvalidPrinterMode",
                "Printer mode must be Disabled or WindowsPrinter."));
        }

        if (string.Equals(request.PrinterMode, ReceiptPrinterModes.WindowsPrinter, StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(request.WindowsPrinterName))
        {
            return Result.Fail(Error.Validation(
                "StoreSettings.WindowsPrinterNameRequired",
                "Windows printer name is required when Windows Printer mode is selected."));
        }

        return Result.Success();
    }

    private static object DbNullableText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? DBNull.Value
            : value.Trim();
    }
}

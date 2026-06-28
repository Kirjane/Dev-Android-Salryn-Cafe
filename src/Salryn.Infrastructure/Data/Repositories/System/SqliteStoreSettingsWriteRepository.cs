using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Repositories;

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

        return Result.Success();
    }

    private static object DbNullableText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? DBNull.Value
            : value.Trim();
    }
}

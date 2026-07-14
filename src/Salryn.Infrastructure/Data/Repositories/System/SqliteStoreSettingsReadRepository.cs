using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;

namespace Salryn.Infrastructure.Data.Repositories.System;

public sealed class SqliteStoreSettingsReadRepository :
    SqliteRepositoryBase,
    IStoreSettingsReadRepository
{
    public SqliteStoreSettingsReadRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public Task<Result<StoreSettingsReadModel?>> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var connection = CreateOpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText = """
                SELECT
                    id,
                    store_name,
                    business_name,
                    address,
                    contact_number,
                    tin,
                    currency_label,
                    receipt_width_mm,
                    tax_mode_enabled,
                    shift_mode_enabled,
                    allow_negative_stock,
                    receipt_header,
                    receipt_footer,
                    printer_mode
                FROM store_settings
                ORDER BY created_at DESC
                LIMIT 1;
                """;

            using var reader = command.ExecuteReader();

            StoreSettingsReadModel? settings = reader.Read()
                ? new StoreSettingsReadModel(
                    reader.GetGuidValue("id"),
                    reader.GetStringValue("store_name"),
                    reader.GetNullableString("business_name"),
                    reader.GetNullableString("address"),
                    reader.GetNullableString("contact_number"),
                    reader.GetNullableString("tin"),
                    reader.GetStringValue("currency_label"),
                    reader.GetInt32Value("receipt_width_mm"),
                    reader.GetBoolValue("tax_mode_enabled"),
                    reader.GetBoolValue("shift_mode_enabled"),
                    reader.GetBoolValue("allow_negative_stock"),
                    reader.GetNullableString("receipt_header"),
                    reader.GetNullableString("receipt_footer"),
                    reader.GetStringValue("printer_mode"))
                : null;

            return Task.FromResult(Result<StoreSettingsReadModel?>.Success(settings));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Task.FromResult(Unexpected<StoreSettingsReadModel?>(
                "StoreSettings.GetCurrentFailed",
                ex));
        }
    }
}

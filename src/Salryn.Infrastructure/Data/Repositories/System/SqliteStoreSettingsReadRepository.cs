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
                    printer_mode,
                    windows_printer_name,
                    logo_content_type,
                    CASE WHEN logo_bytes IS NOT NULL AND length(logo_bytes) > 0 THEN 1 ELSE 0 END AS has_logo
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
                    reader.GetStringValue("printer_mode"),
                    reader.GetNullableString("windows_printer_name"),
                    reader.GetInt32Value("has_logo") == 1,
                    reader.GetNullableString("logo_content_type"))
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

    public Task<Result<StoreLogoReadModel?>> GetLogoAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var connection = CreateOpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT logo_content_type, logo_bytes
                FROM store_settings
                ORDER BY created_at DESC
                LIMIT 1;
                """;

            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return Task.FromResult(Result<StoreLogoReadModel?>.Success(null));
            }

            string? contentType = reader.IsDBNull(0) ? null : reader.GetString(0);
            if (reader.IsDBNull(1) || string.IsNullOrWhiteSpace(contentType))
            {
                return Task.FromResult(Result<StoreLogoReadModel?>.Success(null));
            }

            long length = reader.GetBytes(1, 0, null, 0, 0);
            if (length <= 0)
            {
                return Task.FromResult(Result<StoreLogoReadModel?>.Success(null));
            }

            byte[] bytes = new byte[length];
            reader.GetBytes(1, 0, bytes, 0, (int)length);
            return Task.FromResult(Result<StoreLogoReadModel?>.Success(new StoreLogoReadModel(contentType.Trim(), bytes)));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Task.FromResult(Unexpected<StoreLogoReadModel?>("StoreSettings.GetLogoFailed", ex));
        }
    }
}

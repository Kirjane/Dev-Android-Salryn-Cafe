using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;

namespace Salryn.Infrastructure.Data.Repositories.System;

public sealed class SqliteCashierSessionReadRepository :
    SqliteRepositoryBase,
    ICashierSessionReadRepository
{
    public SqliteCashierSessionReadRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public Task<Result<CashierSessionReadModel?>> FindOpenSessionByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var connection = CreateOpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText = CashierSessionSelectSql + Environment.NewLine + """
                WHERE cs.user_id = $userId
                  AND cs.status = $openStatus
                ORDER BY cs.opened_at DESC
                LIMIT 1;
                """;

            command.Parameters.AddWithValue("$userId", userId.ToString("D"));
            command.Parameters.AddWithValue("$openStatus", Convert.ToInt32(CashierSessionStatus.Open));

            using var reader = command.ExecuteReader();

            CashierSessionReadModel? session = reader.Read()
                ? MapSession(reader)
                : null;

            return Task.FromResult(Result<CashierSessionReadModel?>.Success(session));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Task.FromResult(Unexpected<CashierSessionReadModel?>(
                "CashierSessions.FindOpenByUserFailed",
                ex));
        }
    }

    public Task<Result<CashierSessionReadModel?>> FindByIdAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var connection = CreateOpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText = CashierSessionSelectSql + Environment.NewLine + """
                WHERE cs.id = $sessionId
                LIMIT 1;
                """;

            command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));

            using var reader = command.ExecuteReader();

            CashierSessionReadModel? session = reader.Read()
                ? MapSession(reader)
                : null;

            return Task.FromResult(Result<CashierSessionReadModel?>.Success(session));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Task.FromResult(Unexpected<CashierSessionReadModel?>(
                "CashierSessions.FindByIdFailed",
                ex));
        }
    }

    private static CashierSessionReadModel MapSession(SqliteDataReader reader)
    {
        return new CashierSessionReadModel(
            reader.GetGuidValue("id"),
            reader.GetGuidValue("user_id"),
            reader.GetStringValue("user_display_name"),
            reader.GetDateTimeOffsetValue("opened_at"),
            reader.GetNullableDateTimeOffsetValue("closed_at"),
            reader.GetMoneyValue("opening_cash_cents"),
            reader.GetMoneyValue("expected_cash_cents"),
            reader.GetNullableMoneyValue("counted_cash_cents"),
            reader.GetMoneyValue("variance_cents"),
            reader.GetEnumValue<CashierSessionStatus>("status"),
            reader.GetNullableString("notes"));
    }

    private const string CashierSessionSelectSql = """
        SELECT
            cs.id,
            cs.user_id,
            u.display_name AS user_display_name,
            cs.opened_at,
            cs.closed_at,
            cs.opening_cash_cents,
            cs.expected_cash_cents,
            cs.counted_cash_cents,
            cs.variance_cents,
            cs.status,
            cs.notes
        FROM cashier_sessions cs
        INNER JOIN users u ON u.id = cs.user_id
        """;
}


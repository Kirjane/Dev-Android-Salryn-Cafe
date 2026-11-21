using Microsoft.Data.Sqlite;
using Salryn.Infrastructure.Data;

namespace Salryn.Api.Local.Services;

public sealed record HostCheckoutRequestRecord(
    Guid UserId,
    string ClientRequestId,
    string RequestHash,
    string State,
    string ReceiptNumber,
    string? SaleId,
    long? TotalCents,
    long? ChangeCents,
    string Message);

public sealed class HostCheckoutRequestStore
{
    public const string Pending = "Pending";
    public const string Completed = "Completed";

    private readonly SqliteConnectionFactory _connectionFactory;

    public HostCheckoutRequestStore(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<HostCheckoutRequestRecord?> FindAsync(
        Guid userId,
        string clientRequestId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT request_hash, state, receipt_no, sale_id, total_cents, change_cents, message
            FROM host_checkout_requests
            WHERE user_id = $user AND client_request_id = $request;
            """;
        command.Parameters.AddWithValue("$user", userId.ToString("D"));
        command.Parameters.AddWithValue("$request", clientRequestId);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new HostCheckoutRequestRecord(
            userId,
            clientRequestId,
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetInt64(4),
            reader.IsDBNull(5) ? null : reader.GetInt64(5),
            reader.GetString(6));
    }

    public async Task<HostCheckoutRequestRecord> GetOrCreatePendingAsync(
        Guid userId,
        string clientRequestId,
        string requestHash,
        string receiptNumber,
        CancellationToken cancellationToken = default)
    {
        string now = DateTimeOffset.UtcNow.ToString("O");
        await using SqliteConnection connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT OR IGNORE INTO host_checkout_requests
                (user_id, client_request_id, request_hash, state, receipt_no,
                 sale_id, total_cents, change_cents, message, created_at_utc, updated_at_utc)
            VALUES
                ($user, $request, $hash, 'Pending', $receipt,
                 NULL, NULL, NULL, 'Checkout is being confirmed by the host.', $now, $now);
            """;
        command.Parameters.AddWithValue("$user", userId.ToString("D"));
        command.Parameters.AddWithValue("$request", clientRequestId);
        command.Parameters.AddWithValue("$hash", requestHash);
        command.Parameters.AddWithValue("$receipt", receiptNumber);
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return await FindAsync(userId, clientRequestId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Host checkout request ledger row was not readable after insert.");
    }

    public async Task MarkCompletedAsync(
        HostCheckoutRequestRecord record,
        string? saleId,
        long? totalCents,
        long? changeCents,
        string message,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(saleId, out Guid completedSaleId) ||
            completedSaleId == Guid.Empty ||
            totalCents is null or < 0 ||
            changeCents is null or < 0)
        {
            throw new InvalidOperationException(
                "Completed checkout requires an authoritative sale identity and nonnegative confirmed totals.");
        }

        await using SqliteConnection connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE host_checkout_requests
            SET state = 'Completed', sale_id = $sale, total_cents = $total,
                change_cents = $change, message = $message, updated_at_utc = $now
            WHERE user_id = $user AND client_request_id = $request
              AND request_hash = $hash AND receipt_no = $receipt;
            """;
        command.Parameters.AddWithValue("$sale", saleId);
        command.Parameters.AddWithValue("$total", (object?)totalCents ?? DBNull.Value);
        command.Parameters.AddWithValue("$change", (object?)changeCents ?? DBNull.Value);
        command.Parameters.AddWithValue("$message", message);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$user", record.UserId.ToString("D"));
        command.Parameters.AddWithValue("$request", record.ClientRequestId);
        command.Parameters.AddWithValue("$hash", record.RequestHash);
        command.Parameters.AddWithValue("$receipt", record.ReceiptNumber);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException(
                "Checkout completion could not match the immutable pending request authority.");
        }
    }
}

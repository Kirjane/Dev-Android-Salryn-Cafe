using Microsoft.Data.Sqlite;
using Salryn.Infrastructure.Data;

namespace Salryn.Api.Local.RemotePos;

public sealed record RemotePosCheckoutRequestRecord(
    Guid StoreId,
    Guid TerminalId,
    string ClientRequestId,
    string RequestHash,
    string State,
    string ReceiptNumber,
    string? SaleId,
    long? TotalCents,
    long? ChangeCents,
    string Message);

public sealed class RemotePosCheckoutRequestStore
{
    public const string Pending = "Pending";
    public const string Completed = "Completed";

    private readonly SqliteConnectionFactory _connectionFactory;

    public RemotePosCheckoutRequestStore(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<RemotePosCheckoutRequestRecord?> FindAsync(
        Guid storeId,
        Guid terminalId,
        string clientRequestId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT request_hash, state, receipt_no, sale_id, total_cents, change_cents, message
            FROM remote_pos_checkout_requests
            WHERE store_id = $store AND terminal_id = $terminal AND client_request_id = $request;
            """;
        command.Parameters.AddWithValue("$store", storeId.ToString("D"));
        command.Parameters.AddWithValue("$terminal", terminalId.ToString("D"));
        command.Parameters.AddWithValue("$request", clientRequestId);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new RemotePosCheckoutRequestRecord(
            storeId,
            terminalId,
            clientRequestId,
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetInt64(4),
            reader.IsDBNull(5) ? null : reader.GetInt64(5),
            reader.GetString(6));
    }

    public async Task<RemotePosCheckoutRequestRecord> GetOrCreatePendingAsync(
        Guid storeId,
        Guid terminalId,
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
            INSERT OR IGNORE INTO remote_pos_checkout_requests
                (store_id, terminal_id, client_request_id, request_hash, state, receipt_no,
                 sale_id, total_cents, change_cents, message, created_at_utc, updated_at_utc)
            VALUES
                ($store, $terminal, $request, $hash, 'Pending', $receipt,
                 NULL, NULL, NULL, 'Checkout is being confirmed by the host.', $now, $now);
            """;
        command.Parameters.AddWithValue("$store", storeId.ToString("D"));
        command.Parameters.AddWithValue("$terminal", terminalId.ToString("D"));
        command.Parameters.AddWithValue("$request", clientRequestId);
        command.Parameters.AddWithValue("$hash", requestHash);
        command.Parameters.AddWithValue("$receipt", receiptNumber);
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return await FindAsync(storeId, terminalId, clientRequestId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Remote POS checkout request ledger row was not readable after insert.");
    }

    public async Task MarkCompletedAsync(
        RemotePosCheckoutRequestRecord record,
        string? saleId,
        long? totalCents,
        long? changeCents,
        string message,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE remote_pos_checkout_requests
            SET state = 'Completed', sale_id = $sale, total_cents = $total,
                change_cents = $change, message = $message, updated_at_utc = $now
            WHERE store_id = $store AND terminal_id = $terminal AND client_request_id = $request;
            """;
        command.Parameters.AddWithValue("$sale", (object?)saleId ?? DBNull.Value);
        command.Parameters.AddWithValue("$total", (object?)totalCents ?? DBNull.Value);
        command.Parameters.AddWithValue("$change", (object?)changeCents ?? DBNull.Value);
        command.Parameters.AddWithValue("$message", message);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$store", record.StoreId.ToString("D"));
        command.Parameters.AddWithValue("$terminal", record.TerminalId.ToString("D"));
        command.Parameters.AddWithValue("$request", record.ClientRequestId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;

namespace Salryn.Infrastructure.Data.Services;

public sealed class SqliteAccountsReceivableService : IAccountsReceivableService
{
    private readonly SqliteConnectionFactory _connections;

    public SqliteAccountsReceivableService(SqliteConnectionFactory connections) =>
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));

    public async Task<Result<IReadOnlyList<ReceivableCustomerBalance>>> SearchCustomersAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT c.id, c.full_name, c.phone, c.address, c.note, c.credit_limit_cents,
                   c.is_active, c.created_by_user_id, c.created_at, c.updated_at,
                   COALESCE((SELECT e.balance_after_cents
                             FROM accounts_receivable_entries e
                             WHERE e.customer_id = c.id
                             ORDER BY e.created_at DESC, e.id DESC LIMIT 1), 0),
                   (SELECT MIN(e.due_date) FROM accounts_receivable_entries e
                    WHERE e.customer_id = c.id AND e.debit_cents > 0
                      AND e.balance_after_cents > 0),
                   (SELECT MAX(e.created_at) FROM accounts_receivable_entries e
                    WHERE e.customer_id = c.id)
            FROM customers c
            WHERE $search = '' OR c.full_name LIKE $pattern OR COALESCE(c.phone, '') LIKE $pattern
            ORDER BY c.full_name COLLATE NOCASE;
            """;
        string value = search?.Trim() ?? string.Empty;
        command.Parameters.AddWithValue("$search", value);
        command.Parameters.AddWithValue("$pattern", $"%{value}%");

        List<ReceivableCustomerBalance> rows = new();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ReceivableCustomerBalance(
                ReadCustomer(reader),
                reader.GetInt64(10),
                reader.IsDBNull(11) ? null : DateOnly.Parse(reader.GetString(11)),
                reader.IsDBNull(12) ? null : DateTimeOffset.Parse(reader.GetString(12))));
        }
        return Result<IReadOnlyList<ReceivableCustomerBalance>>.Success(rows);
    }

    public async Task<Result<ReceivableCustomer>> CreateCustomerAsync(
        CreateReceivableCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.FullName) || request.CreatedByUserId == Guid.Empty)
            return Result<ReceivableCustomer>.Fail(Error.Validation("Receivables.InvalidCustomer", "Customer full name and recording user are required."));
        if (request.CreditLimitCents < 0)
            return Result<ReceivableCustomer>.Fail(Error.Validation("Receivables.InvalidCreditLimit", "Credit limit cannot be negative."));

        DateTimeOffset now = DateTimeOffset.UtcNow;
        ReceivableCustomer customer = new(
            Guid.NewGuid(), request.FullName.Trim(), Clean(request.Phone), Clean(request.Address),
            Clean(request.Note), request.CreditLimitCents, true, request.CreatedByUserId, now, now);
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO customers
            (id, full_name, phone, address, note, credit_limit_cents, is_active,
             created_by_user_id, created_at, updated_at)
            VALUES ($id, $name, $phone, $address, $note, $limit, 1, $user, $created, $updated);
            """;
        BindCustomer(command, customer);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return Result<ReceivableCustomer>.Success(customer);
    }

    public async Task<Result<ReceivableCustomer>> UpdateCustomerAsync(
        Guid customerId,
        UpdateReceivableCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        if (customerId == Guid.Empty || string.IsNullOrWhiteSpace(request.FullName) || request.CreditLimitCents < 0)
            return Result<ReceivableCustomer>.Fail(Error.Validation("Receivables.InvalidCustomer", "Valid customer data is required."));
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        ReceivableCustomer? existing = await FindCustomerAsync(connection, customerId, cancellationToken);
        if (existing is null)
            return Result<ReceivableCustomer>.Fail(Error.NotFound("Receivables.CustomerNotFound", "Customer was not found."));
        ReceivableCustomer updated = existing with
        {
            FullName = request.FullName.Trim(), Phone = Clean(request.Phone), Address = Clean(request.Address),
            Note = Clean(request.Note), CreditLimitCents = request.CreditLimitCents,
            IsActive = request.IsActive, UpdatedAt = DateTimeOffset.UtcNow
        };
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE customers SET full_name=$name, phone=$phone, address=$address, note=$note,
                credit_limit_cents=$limit, is_active=$active, updated_at=$updated WHERE id=$id;
            """;
        BindCustomer(command, updated);
        command.Parameters.AddWithValue("$active", updated.IsActive ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return Result<ReceivableCustomer>.Success(updated);
    }

    public async Task<Result<IReadOnlyList<AccountsReceivableEntry>>> GetLedgerAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, customer_id, sale_id, collection_id, entry_type, debit_cents, credit_cents,
                   balance_after_cents, due_date, note, created_by_user_id, created_at
            FROM accounts_receivable_entries WHERE customer_id=$customer
            ORDER BY created_at, id;
            """;
        command.Parameters.AddWithValue("$customer", customerId.ToString("D"));
        List<AccountsReceivableEntry> entries = new();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            entries.Add(ReadEntry(reader));
        return Result<IReadOnlyList<AccountsReceivableEntry>>.Success(entries);
    }

    public async Task<Result<AccountsReceivableSummary>> GetSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        long total = await ScalarAsync(connection, "SELECT COALESCE(SUM(debit_cents-credit_cents),0) FROM accounts_receivable_entries;", cancellationToken);
        string today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        long overdue = await ScalarAsync(connection,
            "SELECT COALESCE(SUM(debit_cents),0) FROM accounts_receivable_entries WHERE due_date < $today AND debit_cents > 0;", cancellationToken, today);
        long dueToday = await ScalarAsync(connection,
            "SELECT COALESCE(SUM(debit_cents),0) FROM accounts_receivable_entries WHERE due_date = $today AND debit_cents > 0;", cancellationToken, today);
        long collected = await ScalarAsync(connection,
            "SELECT COALESCE(SUM(amount_cents),0) FROM accounts_receivable_collections WHERE substr(collected_at,1,10)=$today AND voided_at IS NULL;", cancellationToken, today);
        return Result<AccountsReceivableSummary>.Success(new(total, dueToday, overdue, collected));
    }

    public async Task<Result<AccountsReceivableCollection>> CollectAsync(
        RecordReceivableCollectionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.CustomerId == Guid.Empty || request.CollectedByUserId == Guid.Empty || request.AmountCents <= 0)
            return Result<AccountsReceivableCollection>.Fail(Error.Validation("Receivables.InvalidCollection", "Customer, collector, and a positive amount are required."));
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        long balance = await GetBalanceAsync(connection, transaction, request.CustomerId, cancellationToken);
        if (request.AmountCents > balance)
            return Result<AccountsReceivableCollection>.Fail(Error.Validation("Receivables.CollectionExceedsBalance", "Collection cannot exceed the outstanding balance."));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        AccountsReceivableCollection collection = new(Guid.NewGuid(), request.CustomerId, request.AmountCents,
            string.IsNullOrWhiteSpace(request.Method) ? "Cash" : request.Method.Trim(), Clean(request.Note),
            request.CollectedByUserId, now, null, null, null);
        await InsertCollectionAsync(connection, transaction, collection, cancellationToken);
        await InsertEntryAsync(connection, transaction, new AccountsReceivableEntry(
            Guid.NewGuid(), request.CustomerId, null, collection.Id, AccountsReceivableEntryType.Collection,
            0, request.AmountCents, balance - request.AmountCents, null, collection.Note,
            request.CollectedByUserId, now), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result<AccountsReceivableCollection>.Success(collection);
    }

    public async Task<Result<AccountsReceivableEntry>> AdjustAsync(
        RecordReceivableAdjustmentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.CustomerId == Guid.Empty || request.CreatedByUserId == Guid.Empty ||
            request.AmountCents <= 0 || string.IsNullOrWhiteSpace(request.Note))
            return Result<AccountsReceivableEntry>.Fail(Error.Validation("Receivables.InvalidAdjustment", "Customer, user, positive amount, and note are required."));
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        long balance = await GetBalanceAsync(connection, transaction, request.CustomerId, cancellationToken);
        if (!request.IsDebit && request.AmountCents > balance)
            return Result<AccountsReceivableEntry>.Fail(Error.Validation("Receivables.AdjustmentExceedsBalance", "Credit adjustment cannot exceed the balance."));
        AccountsReceivableEntry entry = new(Guid.NewGuid(), request.CustomerId, null, null,
            request.IsDebit ? AccountsReceivableEntryType.AdjustmentDebit : AccountsReceivableEntryType.AdjustmentCredit,
            request.IsDebit ? request.AmountCents : 0, request.IsDebit ? 0 : request.AmountCents,
            request.IsDebit ? balance + request.AmountCents : balance - request.AmountCents,
            null, request.Note.Trim(), request.CreatedByUserId, DateTimeOffset.UtcNow);
        await InsertEntryAsync(connection, transaction, entry, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result<AccountsReceivableEntry>.Success(entry);
    }

    public async Task<Result> VoidCollectionAsync(Guid collectionId, Guid voidedByUserId, string reason, CancellationToken cancellationToken = default)
    {
        if (collectionId == Guid.Empty || voidedByUserId == Guid.Empty || string.IsNullOrWhiteSpace(reason))
            return Result.Fail(Error.Validation("Receivables.InvalidCollectionVoid", "Collection, user, and reason are required."));
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using SqliteCommand read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT customer_id, amount_cents FROM accounts_receivable_collections WHERE id=$id AND voided_at IS NULL;";
        read.Parameters.AddWithValue("$id", collectionId.ToString("D"));
        await using SqliteDataReader reader = await read.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return Result.Fail(Error.NotFound("Receivables.CollectionNotFound", "Active collection was not found."));
        Guid customerId = Guid.Parse(reader.GetString(0));
        long amount = reader.GetInt64(1);
        await reader.DisposeAsync();
        long balance = await GetBalanceAsync(connection, transaction, customerId, cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using SqliteCommand update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE accounts_receivable_collections SET voided_at=$at, voided_by_user_id=$user, void_reason=$reason WHERE id=$id;";
        update.Parameters.AddWithValue("$at", now.ToString("O"));
        update.Parameters.AddWithValue("$user", voidedByUserId.ToString("D"));
        update.Parameters.AddWithValue("$reason", reason.Trim());
        update.Parameters.AddWithValue("$id", collectionId.ToString("D"));
        await update.ExecuteNonQueryAsync(cancellationToken);
        await InsertEntryAsync(connection, transaction, new AccountsReceivableEntry(Guid.NewGuid(), customerId, null,
            collectionId, AccountsReceivableEntryType.CollectionReversal, amount, 0, balance + amount, null,
            reason.Trim(), voidedByUserId, now), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }

    private static async Task<long> GetBalanceAsync(SqliteConnection connection, SqliteTransaction transaction, Guid customerId, CancellationToken ct)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT balance_after_cents FROM accounts_receivable_entries WHERE customer_id=$id ORDER BY created_at DESC, id DESC LIMIT 1;";
        command.Parameters.AddWithValue("$id", customerId.ToString("D"));
        object? value = await command.ExecuteScalarAsync(ct);
        return value is null or DBNull ? 0 : Convert.ToInt64(value);
    }

    private static async Task InsertCollectionAsync(SqliteConnection c, SqliteTransaction t, AccountsReceivableCollection x, CancellationToken ct)
    {
        await using SqliteCommand command = c.CreateCommand();
        command.Transaction = t;
        command.CommandText = "INSERT INTO accounts_receivable_collections (id,customer_id,amount_cents,method,note,collected_by_user_id,collected_at) VALUES ($id,$customer,$amount,$method,$note,$user,$at);";
        command.Parameters.AddWithValue("$id", x.Id.ToString("D"));
        command.Parameters.AddWithValue("$customer", x.CustomerId.ToString("D"));
        command.Parameters.AddWithValue("$amount", x.AmountCents);
        command.Parameters.AddWithValue("$method", x.Method);
        command.Parameters.AddWithValue("$note", (object?)x.Note ?? DBNull.Value);
        command.Parameters.AddWithValue("$user", x.CollectedByUserId.ToString("D"));
        command.Parameters.AddWithValue("$at", x.CollectedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(ct);
    }

    internal static async Task InsertEntryAsync(SqliteConnection c, SqliteTransaction t, AccountsReceivableEntry x, CancellationToken ct)
    {
        await using SqliteCommand command = c.CreateCommand();
        command.Transaction = t;
        command.CommandText = "INSERT INTO accounts_receivable_entries (id,customer_id,sale_id,collection_id,entry_type,debit_cents,credit_cents,balance_after_cents,due_date,note,created_by_user_id,created_at) VALUES ($id,$customer,$sale,$collection,$type,$debit,$credit,$balance,$due,$note,$user,$at);";
        command.Parameters.AddWithValue("$id", x.Id.ToString("D"));
        command.Parameters.AddWithValue("$customer", x.CustomerId.ToString("D"));
        command.Parameters.AddWithValue("$sale", x.SaleId?.ToString("D") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$collection", x.CollectionId?.ToString("D") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$type", x.EntryType.ToString());
        command.Parameters.AddWithValue("$debit", x.DebitCents);
        command.Parameters.AddWithValue("$credit", x.CreditCents);
        command.Parameters.AddWithValue("$balance", x.BalanceAfterCents);
        command.Parameters.AddWithValue("$due", x.DueDate?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$note", x.Note ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$user", x.CreatedByUserId.ToString("D"));
        command.Parameters.AddWithValue("$at", x.CreatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(ct);
    }

    private static ReceivableCustomer ReadCustomer(SqliteDataReader r) => new(
        Guid.Parse(r.GetString(0)), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
        r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
        r.IsDBNull(5) ? null : r.GetInt64(5), r.GetInt64(6) != 0, Guid.Parse(r.GetString(7)),
        DateTimeOffset.Parse(r.GetString(8)), DateTimeOffset.Parse(r.GetString(9)));

    private static AccountsReceivableEntry ReadEntry(SqliteDataReader r) => new(
        Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), r.IsDBNull(2) ? null : Guid.Parse(r.GetString(2)),
        r.IsDBNull(3) ? null : Guid.Parse(r.GetString(3)), Enum.Parse<AccountsReceivableEntryType>(r.GetString(4)),
        r.GetInt64(5), r.GetInt64(6), r.GetInt64(7), r.IsDBNull(8) ? null : DateOnly.Parse(r.GetString(8)),
        r.IsDBNull(9) ? null : r.GetString(9), Guid.Parse(r.GetString(10)), DateTimeOffset.Parse(r.GetString(11)));

    private static async Task<ReceivableCustomer?> FindCustomerAsync(SqliteConnection c, Guid id, CancellationToken ct)
    {
        await using SqliteCommand command = c.CreateCommand();
        command.CommandText = "SELECT id,full_name,phone,address,note,credit_limit_cents,is_active,created_by_user_id,created_at,updated_at FROM customers WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadCustomer(reader) : null;
    }

    private static void BindCustomer(SqliteCommand command, ReceivableCustomer c)
    {
        command.Parameters.AddWithValue("$id", c.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", c.FullName);
        command.Parameters.AddWithValue("$phone", c.Phone ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$address", c.Address ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$note", c.Note ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$limit", c.CreditLimitCents ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$user", c.CreatedByUserId.ToString("D"));
        command.Parameters.AddWithValue("$created", c.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updated", c.UpdatedAt.ToString("O"));
    }

    private static async Task<long> ScalarAsync(SqliteConnection c, string sql, CancellationToken ct, string? today = null)
    {
        await using SqliteCommand command = c.CreateCommand();
        command.CommandText = sql;
        if (today is not null) command.Parameters.AddWithValue("$today", today);
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

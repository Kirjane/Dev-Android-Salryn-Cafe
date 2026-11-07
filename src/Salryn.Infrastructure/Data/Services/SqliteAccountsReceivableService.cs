using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;

namespace Salryn.Infrastructure.Data.Services;

public sealed class SqliteAccountsReceivableService : IAccountsReceivableService
{
    public const int CustomerQueryLimit = 100;
    public const int LedgerQueryLimit = 500;

    private readonly SqliteConnectionFactory _connections;
    private readonly TimeProvider _timeProvider;
    private static readonly TimeZoneInfo StoreTimeZone = TimeZoneInfo.CreateCustomTimeZone(
        "Asia/Manila",
        TimeSpan.FromHours(8),
        "Philippine Standard Time",
        "Philippine Standard Time");

    public SqliteAccountsReceivableService(
        SqliteConnectionFactory connections,
        TimeProvider? timeProvider = null)
    {
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

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
                   COALESCE((SELECT SUM(e.debit_cents - e.credit_cents)
                             FROM accounts_receivable_entries e
                             WHERE e.customer_id = c.id), 0),
                   (WITH allocation AS (
                        SELECT e.due_date, e.debit_cents,
                               COALESCE(SUM(CASE WHEN e.due_date IS NOT NULL THEN e.debit_cents ELSE 0 END) OVER (
                                   PARTITION BY e.customer_id ORDER BY e.created_at, e.id
                                   ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0) AS prior_debits,
                               SUM(CASE WHEN e.entry_type = 'CollectionReversal'
                                        THEN -e.debit_cents ELSE e.credit_cents END)
                                   OVER (PARTITION BY e.customer_id) AS total_credits
                        FROM accounts_receivable_entries e
                        WHERE e.customer_id = c.id
                    )
                    SELECT MIN(due_date) FROM allocation
                    WHERE due_date IS NOT NULL
                      AND MAX(0, debit_cents - MAX(0, total_credits - prior_debits)) > 0),
                   (SELECT MAX(e.created_at) FROM accounts_receivable_entries e
                    WHERE e.customer_id = c.id)
            FROM customers c
            WHERE $search = '' OR c.full_name LIKE $pattern OR COALESCE(c.phone, '') LIKE $pattern
            ORDER BY c.full_name COLLATE NOCASE
            LIMIT $limit;
            """;
        string value = search?.Trim() ?? string.Empty;
        command.Parameters.AddWithValue("$search", value);
        command.Parameters.AddWithValue("$pattern", $"%{value}%");
        command.Parameters.AddWithValue("$limit", CustomerQueryLimit);

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

    public async Task<Result<ReceivablePage<ReceivableCustomerBalance>>> SearchCustomersPageAsync(
        string? search,int offset,int limit,CancellationToken cancellationToken=default)
    {
        offset=Math.Max(0,offset);limit=Math.Clamp(limit,1,100);
        string value=search?.Trim()??string.Empty;
        await using SqliteConnection connection=_connections.CreateConnection();await connection.OpenAsync(cancellationToken);
        await using SqliteCommand count=connection.CreateCommand();
        count.CommandText="SELECT COUNT(*) FROM customers c WHERE $search='' OR c.full_name LIKE $pattern OR COALESCE(c.phone,'') LIKE $pattern;";
        count.Parameters.AddWithValue("$search",value);count.Parameters.AddWithValue("$pattern",$"%{value}%");
        int total=Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken));
        await using SqliteCommand command=connection.CreateCommand();
        command.CommandText="""
            SELECT c.id,c.full_name,c.phone,c.address,c.note,c.credit_limit_cents,c.is_active,c.created_by_user_id,c.created_at,c.updated_at,
              COALESCE((SELECT SUM(e.debit_cents-e.credit_cents) FROM accounts_receivable_entries e WHERE e.customer_id=c.id),0),
              (WITH allocation AS (SELECT e.due_date,e.debit_cents,COALESCE(SUM(CASE WHEN e.due_date IS NOT NULL THEN e.debit_cents ELSE 0 END) OVER (PARTITION BY e.customer_id ORDER BY e.created_at,e.id ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING),0) prior_debits,SUM(CASE WHEN e.entry_type='CollectionReversal' THEN -e.debit_cents ELSE e.credit_cents END) OVER (PARTITION BY e.customer_id) total_credits FROM accounts_receivable_entries e WHERE e.customer_id=c.id) SELECT MIN(due_date) FROM allocation WHERE due_date IS NOT NULL AND MAX(0,debit_cents-MAX(0,total_credits-prior_debits))>0),
              (SELECT MAX(e.created_at) FROM accounts_receivable_entries e WHERE e.customer_id=c.id)
            FROM customers c WHERE $search='' OR c.full_name LIKE $pattern OR COALESCE(c.phone,'') LIKE $pattern
            ORDER BY c.full_name COLLATE NOCASE,c.id LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$search",value);command.Parameters.AddWithValue("$pattern",$"%{value}%");command.Parameters.AddWithValue("$limit",limit);command.Parameters.AddWithValue("$offset",offset);
        List<ReceivableCustomerBalance> items=new();await using SqliteDataReader reader=await command.ExecuteReaderAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken))items.Add(new(ReadCustomer(reader),reader.GetInt64(10),reader.IsDBNull(11)?null:DateOnly.Parse(reader.GetString(11)),reader.IsDBNull(12)?null:DateTimeOffset.Parse(reader.GetString(12))));
        return Result<ReceivablePage<ReceivableCustomerBalance>>.Success(new(items,offset,limit,total,offset+items.Count<total,_timeProvider.GetUtcNow(),"Asia/Manila"));
    }

    public async Task<Result<ReceivableCustomer>> CreateCustomerAsync(
        CreateReceivableCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.FullName) || request.CreatedByUserId == Guid.Empty)
            return Result<ReceivableCustomer>.Fail(Error.Validation("Receivables.InvalidCustomer", "Customer full name and recording user are required."));
        if (request.CreditLimitCents < 0)
            return Result<ReceivableCustomer>.Fail(Error.Validation("Receivables.InvalidCreditLimit", "Credit limit cannot be negative."));

        DateTimeOffset now = _timeProvider.GetUtcNow();
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
            IsActive = request.IsActive, UpdatedAt = _timeProvider.GetUtcNow()
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

    public async Task<Result<ReceivableCustomer?>> FindCustomerAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        if (customerId == Guid.Empty)
            return Result<ReceivableCustomer?>.Fail(
                Error.Validation("Receivables.InvalidCustomer", "A valid customer id is required."));
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return Result<ReceivableCustomer?>.Success(
            await FindCustomerAsync(connection, customerId, cancellationToken));
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
                   balance_after_cents, due_date, note, created_by_user_id, created_at, payment_status
            FROM (
                SELECT e.id, e.customer_id, e.sale_id, e.collection_id, e.entry_type,
                       e.debit_cents, e.credit_cents, e.balance_after_cents, e.due_date,
                       e.note, e.created_by_user_id, e.created_at, s.payment_status
                FROM accounts_receivable_entries e
                LEFT JOIN sales s ON s.id = e.sale_id
                WHERE e.customer_id=$customer
                ORDER BY e.created_at DESC, e.id DESC
                LIMIT $limit
            )
            ORDER BY created_at, id;
            """;
        command.Parameters.AddWithValue("$customer", customerId.ToString("D"));
        command.Parameters.AddWithValue("$limit", LedgerQueryLimit);
        List<AccountsReceivableEntry> entries = new();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            entries.Add(ReadEntry(reader));
        return Result<IReadOnlyList<AccountsReceivableEntry>>.Success(entries);
    }

    public async Task<Result<ReceivablePage<AccountsReceivableEntry>>> GetLedgerPageAsync(Guid customerId,int offset,int limit,CancellationToken cancellationToken=default)
    {
        offset=Math.Max(0,offset);limit=Math.Clamp(limit,1,200);
        await using SqliteConnection connection=_connections.CreateConnection();await connection.OpenAsync(cancellationToken);
        await using SqliteCommand count=connection.CreateCommand();count.CommandText="SELECT COUNT(*) FROM accounts_receivable_entries WHERE customer_id=$customer;";count.Parameters.AddWithValue("$customer",customerId.ToString("D"));
        int total=Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken));
        await using SqliteCommand command=connection.CreateCommand();command.CommandText="""
            SELECT e.id,e.customer_id,e.sale_id,e.collection_id,e.entry_type,e.debit_cents,e.credit_cents,e.balance_after_cents,e.due_date,e.note,e.created_by_user_id,e.created_at,s.payment_status
            FROM accounts_receivable_entries e LEFT JOIN sales s ON s.id=e.sale_id
            WHERE e.customer_id=$customer ORDER BY e.created_at DESC,e.id DESC LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$customer",customerId.ToString("D"));command.Parameters.AddWithValue("$limit",limit);command.Parameters.AddWithValue("$offset",offset);
        List<AccountsReceivableEntry> items=new();await using SqliteDataReader reader=await command.ExecuteReaderAsync(cancellationToken);while(await reader.ReadAsync(cancellationToken))items.Add(ReadEntry(reader));
        return Result<ReceivablePage<AccountsReceivableEntry>>.Success(new(items,offset,limit,total,offset+items.Count<total,_timeProvider.GetUtcNow(),"Asia/Manila"));
    }

    public async Task<Result<AccountsReceivableSummary>> GetSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        long total = await ScalarAsync(connection, "SELECT COALESCE(SUM(debit_cents-credit_cents),0) FROM accounts_receivable_entries;", cancellationToken);
        DateTimeOffset storeNow = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), StoreTimeZone);
        DateTimeOffset todayStart = new(
            storeNow.Year, storeNow.Month, storeNow.Day, 0, 0, 0, StoreTimeZone.BaseUtcOffset);
        DateTimeOffset tomorrowStart = todayStart.AddDays(1);
        string today = DateOnly.FromDateTime(todayStart.DateTime).ToString("yyyy-MM-dd");
        const string exposureCte =
            """
            WITH allocation AS (
                SELECT due_date, debit_cents,
                       COALESCE(SUM(CASE WHEN due_date IS NOT NULL THEN debit_cents ELSE 0 END) OVER (
                           PARTITION BY customer_id ORDER BY created_at, id
                           ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0) AS prior_debits,
                       SUM(CASE WHEN entry_type = 'CollectionReversal'
                                THEN -debit_cents ELSE credit_cents END)
                           OVER (PARTITION BY customer_id) AS total_credits
                FROM accounts_receivable_entries
            ),
            exposure AS (
                SELECT due_date,
                       MAX(0, debit_cents - MAX(0, total_credits - prior_debits)) AS remaining_cents
                FROM allocation
                WHERE debit_cents > 0
            )
            """;
        long overdue = await ScalarAsync(connection,
            exposureCte + " SELECT COALESCE(SUM(remaining_cents),0) FROM exposure WHERE due_date < $today;",
            cancellationToken, today);
        long dueToday = await ScalarAsync(connection,
            exposureCte + " SELECT COALESCE(SUM(remaining_cents),0) FROM exposure WHERE due_date = $today;",
            cancellationToken, today);
        long collected = await ScalarRangeAsync(connection,
            "SELECT COALESCE(SUM(amount_cents),0) FROM accounts_receivable_collections " +
            "WHERE collected_at >= $start AND collected_at < $end AND voided_at IS NULL;",
            cancellationToken,
            todayStart.ToUniversalTime(),
            tomorrowStart.ToUniversalTime());
        return Result<AccountsReceivableSummary>.Success(new(total, dueToday, overdue, collected));
    }

    public async Task<Result<AccountsReceivableCollection>> CollectAsync(
        RecordReceivableCollectionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.CustomerId == Guid.Empty || request.CollectedByUserId == Guid.Empty ||
            request.AmountCents <= 0 || string.IsNullOrWhiteSpace(request.ClientRequestId) ||
            request.ClientRequestId.Length > 128)
            return Result<AccountsReceivableCollection>.Fail(Error.Validation(
                "Receivables.InvalidCollection",
                "Customer, collector, positive amount, and a valid request id are required."));
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        string requestHash = BuildCollectionRequestHash(request);
        (AccountsReceivableCollection Collection, string RequestHash)? existing =
            await FindCollectionByRequestAsync(
                    connection,
                    transaction,
                    request.CollectedByUserId,
                    request.ClientRequestId,
                    cancellationToken)
                .ConfigureAwait(false);
        if (existing is not null)
        {
            if (!string.Equals(existing.Value.RequestHash, requestHash, StringComparison.Ordinal))
                return Result<AccountsReceivableCollection>.Fail(Error.Conflict(
                    "Receivables.CollectionRequestIdReused",
                    "This collection request id was already used for different collection details."));
            return Result<AccountsReceivableCollection>.Success(existing.Value.Collection);
        }
        long balance = await GetBalanceAsync(connection, transaction, request.CustomerId, cancellationToken);
        if (request.AmountCents > balance)
            return Result<AccountsReceivableCollection>.Fail(Error.Validation("Receivables.CollectionExceedsBalance", "Collection cannot exceed the outstanding balance."));
        DateTimeOffset now = _timeProvider.GetUtcNow();
        AccountsReceivableCollection collection = new(Guid.NewGuid(), request.CustomerId, request.AmountCents,
            string.IsNullOrWhiteSpace(request.Method) ? "Cash" : request.Method.Trim(), Clean(request.Note),
            request.CollectedByUserId, now, null, null, null);
        await InsertCollectionAsync(
            connection, transaction, collection, request.ClientRequestId, requestHash, cancellationToken);
        await InsertEntryAsync(connection, transaction, new AccountsReceivableEntry(
            Guid.NewGuid(), request.CustomerId, null, collection.Id, AccountsReceivableEntryType.Collection,
            0, request.AmountCents, balance - request.AmountCents, null, collection.Note,
            request.CollectedByUserId, now), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result<AccountsReceivableCollection>.Success(collection);
    }

    public async Task<Result<CollectionPaymentAcknowledgment>> GetCollectionAcknowledgmentAsync(
        Guid collectionId,
        CancellationToken cancellationToken = default)
    {
        if (collectionId == Guid.Empty)
            return Result<CollectionPaymentAcknowledgment>.Fail(
                Error.Validation("Receivables.InvalidCollection", "A valid collection is required."));

        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT c.id, customer.full_name, c.amount_cents,
                   entry.balance_after_cents + c.amount_cents,
                   entry.balance_after_cents,
                   user.display_name, c.collected_at, c.note, c.method
            FROM accounts_receivable_collections c
            JOIN customers customer ON customer.id = c.customer_id
            JOIN accounts_receivable_entries entry
              ON entry.collection_id = c.id AND entry.entry_type = 'Collection'
            JOIN users user ON user.id = c.collected_by_user_id
            WHERE c.id = $id;
            """;
        command.Parameters.AddWithValue("$id", collectionId.ToString("D"));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return Result<CollectionPaymentAcknowledgment>.Fail(
                Error.NotFound("Receivables.CollectionNotFound", "Collection was not found."));

        return Result<CollectionPaymentAcknowledgment>.Success(new(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetString(5),
            DateTimeOffset.Parse(reader.GetString(6)),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetString(8)));
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
            null, request.Note.Trim(), request.CreatedByUserId, _timeProvider.GetUtcNow());
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
        read.CommandText = "SELECT customer_id, amount_cents, voided_at FROM accounts_receivable_collections WHERE id=$id;";
        read.Parameters.AddWithValue("$id", collectionId.ToString("D"));
        await using SqliteDataReader reader = await read.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return Result.Fail(Error.NotFound("Receivables.CollectionNotFound", "Collection was not found."));
        if (!reader.IsDBNull(2))
            return Result.Fail(Error.Conflict(
                "Receivables.CollectionAlreadyVoided",
                "This collection was already reversed."));
        Guid customerId = Guid.Parse(reader.GetString(0));
        long amount = reader.GetInt64(1);
        await reader.DisposeAsync();
        long balance = await GetBalanceAsync(connection, transaction, customerId, cancellationToken);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        await using SqliteCommand update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE accounts_receivable_collections SET voided_at=$at, voided_by_user_id=$user, void_reason=$reason WHERE id=$id AND voided_at IS NULL;";
        update.Parameters.AddWithValue("$at", now.ToString("O"));
        update.Parameters.AddWithValue("$user", voidedByUserId.ToString("D"));
        update.Parameters.AddWithValue("$reason", reason.Trim());
        update.Parameters.AddWithValue("$id", collectionId.ToString("D"));
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            return Result.Fail(Error.Conflict(
                "Receivables.CollectionAlreadyVoided",
                "This collection was already reversed."));
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

    private static async Task InsertCollectionAsync(
        SqliteConnection c,
        SqliteTransaction t,
        AccountsReceivableCollection x,
        string clientRequestId,
        string requestHash,
        CancellationToken ct)
    {
        await using SqliteCommand command = c.CreateCommand();
        command.Transaction = t;
        command.CommandText = "INSERT INTO accounts_receivable_collections (id,customer_id,amount_cents,method,note,collected_by_user_id,collected_at,client_request_id,request_hash) VALUES ($id,$customer,$amount,$method,$note,$user,$at,$request,$hash);";
        command.Parameters.AddWithValue("$id", x.Id.ToString("D"));
        command.Parameters.AddWithValue("$customer", x.CustomerId.ToString("D"));
        command.Parameters.AddWithValue("$amount", x.AmountCents);
        command.Parameters.AddWithValue("$method", x.Method);
        command.Parameters.AddWithValue("$note", (object?)x.Note ?? DBNull.Value);
        command.Parameters.AddWithValue("$user", x.CollectedByUserId.ToString("D"));
        command.Parameters.AddWithValue("$at", x.CollectedAt.ToString("O"));
        command.Parameters.AddWithValue("$request", clientRequestId);
        command.Parameters.AddWithValue("$hash", requestHash);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<(AccountsReceivableCollection Collection, string RequestHash)?>
        FindCollectionByRequestAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            Guid collectedByUserId,
            string clientRequestId,
            CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id, customer_id, amount_cents, method, note, collected_by_user_id, collected_at,
                   voided_at, voided_by_user_id, void_reason, request_hash
            FROM accounts_receivable_collections
            WHERE collected_by_user_id = $user AND client_request_id = $request;
            """;
        command.Parameters.AddWithValue("$user", collectedByUserId.ToString("D"));
        command.Parameters.AddWithValue("$request", clientRequestId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        AccountsReceivableCollection collection = new(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            reader.GetInt64(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            Guid.Parse(reader.GetString(5)),
            DateTimeOffset.Parse(reader.GetString(6)),
            reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7)),
            reader.IsDBNull(8) ? null : Guid.Parse(reader.GetString(8)),
            reader.IsDBNull(9) ? null : reader.GetString(9));
        return (collection, reader.GetString(10));
    }

    private static string BuildCollectionRequestHash(RecordReceivableCollectionRequest request)
    {
        string canonical =
            $"{request.CustomerId:N}|{request.AmountCents}|{request.Method.Trim().ToUpperInvariant()}|{request.Note?.Trim() ?? string.Empty}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
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
        r.IsDBNull(9) ? null : r.GetString(9), Guid.Parse(r.GetString(10)), DateTimeOffset.Parse(r.GetString(11)),
        r.IsDBNull(12) ? null : Enum.Parse<SalePaymentStatus>(r.GetString(12)));

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

    private static async Task<long> ScalarRangeAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$start", start.ToString("O"));
        command.Parameters.AddWithValue("$end", end.ToString("O"));
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

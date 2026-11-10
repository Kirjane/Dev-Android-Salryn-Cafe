using System.Text.Json;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Models.ProcureToPay;
using Salryn.Core.Services;

namespace Salryn.Infrastructure.Data.Services;

public sealed partial class SqliteProcureToPayService : IProcureToPayService
{
    public const string DefaultMainLocationId = "00000000-0000-0000-0000-000000000501";
    public const string DefaultReceivingLocationId = "00000000-0000-0000-0000-000000000502";
    public const string DefaultQuarantineLocationId = "00000000-0000-0000-0000-000000000503";
    public const string DefaultDamagedLocationId = "00000000-0000-0000-0000-000000000504";

    private readonly SqliteConnectionFactory _connections;
    private readonly TimeProvider _timeProvider;

    public SqliteProcureToPayService(
        SqliteConnectionFactory connections,
        TimeProvider? timeProvider = null)
    {
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<Result<LpiFoundationCatalog>> GetFoundationCatalogAsync(
        bool includeInactiveSuppliers = true,
        CancellationToken cancellationToken = default)
    {
        Result<IReadOnlyList<LpiSupplier>> suppliers = await GetSuppliersAsync(null, includeInactiveSuppliers, cancellationToken);
        if (suppliers.IsFailure) return Result<LpiFoundationCatalog>.Fail(suppliers.ErrorDetail);

        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        List<InventoryLocationModel> locations = new();
        await using (SqliteCommand locCmd = connection.CreateCommand())
        {
            locCmd.CommandText = """
                SELECT id, code, name, location_type, is_active, created_at
                FROM inventory_locations
                ORDER BY code;
                """;
            await using SqliteDataReader reader = await locCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                locations.Add(new InventoryLocationModel(
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt64(4) == 1,
                    DateTimeOffset.Parse(reader.GetString(5))));
            }
        }

        Dictionary<string, long> sequences = new(StringComparer.OrdinalIgnoreCase);
        await using (SqliteCommand seqCmd = connection.CreateCommand())
        {
            seqCmd.CommandText = "SELECT prefix, current_number FROM lpi_document_sequences ORDER BY prefix;";
            await using SqliteDataReader reader = await seqCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                sequences[reader.GetString(0)] = reader.GetInt64(1);
            }
        }

        List<LpiFoundationUnit> units = new();
        await using (SqliteCommand unitCmd = connection.CreateCommand())
        {
            unitCmd.CommandText = """
                SELECT id, COALESCE(NULLIF(TRIM(abbreviation), ''), name), name, allows_decimal_quantity
                FROM units
                WHERE is_active = 1
                ORDER BY name;
                """;
            await using SqliteDataReader reader = await unitCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                units.Add(new LpiFoundationUnit(
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt64(3) == 1));
            }
        }

        List<LpiFoundationProduct> products = new();
        await using (SqliteCommand productCmd = connection.CreateCommand())
        {
            productCmd.CommandText = """
                SELECT p.id, p.name, p.barcode, p.unit_id, u.name,
                       COALESCE(p.cost_price_cents, 0), p.selling_price_cents, p.stock_on_hand_x1000
                FROM products p
                INNER JOIN units u ON u.id = p.unit_id
                WHERE p.is_active = 1
                ORDER BY p.name;
                """;
            await using SqliteDataReader reader = await productCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                products.Add(new LpiFoundationProduct(
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    Guid.Parse(reader.GetString(3)),
                    reader.GetString(4),
                    reader.GetInt64(5),
                    reader.GetInt64(6),
                    reader.GetInt64(7)));
            }
        }

        return Result<LpiFoundationCatalog>.Success(new LpiFoundationCatalog(
            suppliers.Value,
            locations,
            PurchaseClassifications.All,
            sequences,
            units,
            products));
    }

    public async Task<Result<IReadOnlyList<LpiSupplier>>> GetSuppliersAsync(
        string? search = null,
        bool includeInactive = true,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using SqliteCommand cmd = connection.CreateCommand();
        string term = search?.Trim() ?? string.Empty;
        cmd.CommandText = """
            SELECT s.id, s.code, s.name, s.contact_person, s.contact_number, s.email, s.address, s.tin,
                   COALESCE(s.payment_terms_days, 30), COALESCE(s.lead_time_days, 3), COALESCE(s.default_currency, 'PHP'),
                   s.notes, s.is_active, s.created_at, s.updated_at,
                   COALESCE((SELECT SUM(i.remaining_balance_cents)
                             FROM supplier_invoices i
                             WHERE i.supplier_id = s.id AND i.status IN ('PostedOpen', 'PartiallyPaid')), 0),
                   COALESCE((SELECT COUNT(*)
                             FROM purchase_orders po
                             WHERE po.supplier_id = s.id AND po.status NOT IN ('Cancelled', 'Closed')), 0)
            FROM suppliers s
            WHERE ($includeInactive = 1 OR s.is_active = 1)
              AND ($search = '' OR s.name LIKE $pattern OR COALESCE(s.code, '') LIKE $pattern OR COALESCE(s.contact_person, '') LIKE $pattern)
            ORDER BY s.is_active DESC, s.name COLLATE NOCASE;
            """;
        cmd.Parameters.AddWithValue("$includeInactive", includeInactive ? 1 : 0);
        cmd.Parameters.AddWithValue("$search", term);
        cmd.Parameters.AddWithValue("$pattern", $"%{term}%");

        List<LpiSupplier> items = new();
        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadSupplier(reader));
        }

        return Result<IReadOnlyList<LpiSupplier>>.Success(items);
    }

    public async Task<Result<LpiSupplier>> CreateSupplierAsync(
        CreateLpiSupplierRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result<LpiSupplier>.Fail(Error.Validation("Lpi.Supplier.NameRequired", "Supplier name is required."));
        }
        if (request.PaymentTermsDays < 0 || request.LeadTimeDays < 0)
        {
            return Result<LpiSupplier>.Fail(Error.Validation("Lpi.Supplier.InvalidTerms", "Payment terms and lead time cannot be negative."));
        }

        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        SqlitePragma.Apply(connection);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        string trimmedName = request.Name.Trim();
        string? trimmedCode = Clean(request.Code)?.ToUpperInvariant();

        if (await SupplierNameExistsAsync(connection, transaction, trimmedName, null, cancellationToken))
        {
            return Result<LpiSupplier>.Fail(Error.Conflict("Lpi.Supplier.DuplicateName", $"Supplier '{trimmedName}' already exists."));
        }

        if (!string.IsNullOrWhiteSpace(trimmedCode) &&
            await SupplierCodeExistsAsync(connection, transaction, trimmedCode, null, cancellationToken))
        {
            return Result<LpiSupplier>.Fail(Error.Conflict("Lpi.Supplier.DuplicateCode", $"Supplier code '{trimmedCode}' already exists."));
        }

        if (string.IsNullOrWhiteSpace(trimmedCode))
        {
            trimmedCode = $"SUP-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        }

        Guid id = Guid.NewGuid();
        DateTimeOffset now = _timeProvider.GetUtcNow();

        await using (SqliteCommand cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO suppliers (
                    id, code, name, contact_person, contact_number, email, address, tin,
                    payment_terms_days, lead_time_days, default_currency, notes, is_active, created_at, updated_at
                )
                VALUES (
                    $id, $code, $name, $contactPerson, $contactNumber, $email, $address, $tin,
                    $paymentTerms, $leadTime, 'PHP', $notes, 1, $now, $now
                );
                """;
            cmd.Parameters.AddWithValue("$id", id.ToString("D"));
            cmd.Parameters.AddWithValue("$code", (object?)trimmedCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$name", trimmedName);
            cmd.Parameters.AddWithValue("$contactPerson", (object?)Clean(request.ContactPerson) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$contactNumber", (object?)Clean(request.ContactNumber) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$email", (object?)Clean(request.Email) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$address", (object?)Clean(request.Address) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$tin", (object?)Clean(request.Tin) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$paymentTerms", request.PaymentTermsDays);
            cmd.Parameters.AddWithValue("$leadTime", request.LeadTimeDays);
            cmd.Parameters.AddWithValue("$notes", (object?)Clean(request.Notes) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$now", now.ToString("O"));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            request.ActorUserId,
            "SUPPLIER_CREATED",
            "Supplier",
            id.ToString("D"),
            new { id, code = trimmedCode, name = trimmedName, request.PaymentTermsDays, request.LeadTimeDays },
            null,
            "Created supplier record.",
            now,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        LpiSupplier? created = await FindSupplierByIdAsync(connection, null, id, cancellationToken);
        return Result<LpiSupplier>.Success(created!);
    }

    public async Task<Result<LpiSupplier>> UpdateSupplierAsync(
        Guid supplierId,
        UpdateLpiSupplierRequest request,
        CancellationToken cancellationToken = default)
    {
        if (supplierId == Guid.Empty || string.IsNullOrWhiteSpace(request.Name))
        {
            return Result<LpiSupplier>.Fail(Error.Validation("Lpi.Supplier.InvalidUpdate", "Supplier ID and name are required."));
        }
        if (request.PaymentTermsDays < 0 || request.LeadTimeDays < 0)
        {
            return Result<LpiSupplier>.Fail(Error.Validation("Lpi.Supplier.InvalidTerms", "Payment terms and lead time cannot be negative."));
        }

        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        SqlitePragma.Apply(connection);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        LpiSupplier? existing = await FindSupplierByIdAsync(connection, transaction, supplierId, cancellationToken);
        if (existing is null)
        {
            return Result<LpiSupplier>.Fail(Error.NotFound("Lpi.Supplier.NotFound", "Supplier was not found."));
        }

        string trimmedName = request.Name.Trim();
        string? trimmedCode = Clean(request.Code)?.ToUpperInvariant() ?? existing.Code;

        if (await SupplierNameExistsAsync(connection, transaction, trimmedName, supplierId, cancellationToken))
        {
            return Result<LpiSupplier>.Fail(Error.Conflict("Lpi.Supplier.DuplicateName", $"Supplier '{trimmedName}' already exists."));
        }

        if (!string.IsNullOrWhiteSpace(trimmedCode) &&
            await SupplierCodeExistsAsync(connection, transaction, trimmedCode, supplierId, cancellationToken))
        {
            return Result<LpiSupplier>.Fail(Error.Conflict("Lpi.Supplier.DuplicateCode", $"Supplier code '{trimmedCode}' already exists."));
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();

        await using (SqliteCommand cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = """
                UPDATE suppliers
                SET code = $code,
                    name = $name,
                    contact_person = $contactPerson,
                    contact_number = $contactNumber,
                    email = $email,
                    address = $address,
                    tin = $tin,
                    payment_terms_days = $paymentTerms,
                    lead_time_days = $leadTime,
                    notes = $notes,
                    is_active = $isActive,
                    updated_at = $now
                WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", supplierId.ToString("D"));
            cmd.Parameters.AddWithValue("$code", (object?)trimmedCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$name", trimmedName);
            cmd.Parameters.AddWithValue("$contactPerson", (object?)Clean(request.ContactPerson) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$contactNumber", (object?)Clean(request.ContactNumber) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$email", (object?)Clean(request.Email) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$address", (object?)Clean(request.Address) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$tin", (object?)Clean(request.Tin) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$paymentTerms", request.PaymentTermsDays);
            cmd.Parameters.AddWithValue("$leadTime", request.LeadTimeDays);
            cmd.Parameters.AddWithValue("$notes", (object?)Clean(request.Notes) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$isActive", request.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("$now", now.ToString("O"));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            request.ActorUserId,
            request.IsActive ? "SUPPLIER_UPDATED" : "SUPPLIER_DEACTIVATED",
            "Supplier",
            supplierId.ToString("D"),
            new { code = trimmedCode, name = trimmedName, request.IsActive, request.PaymentTermsDays },
            new { existing.Code, existing.Name, existing.IsActive, existing.PaymentTermsDays },
            request.IsActive ? "Updated supplier record." : "Deactivated supplier record.",
            now,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        LpiSupplier? updated = await FindSupplierByIdAsync(connection, null, supplierId, cancellationToken);
        return Result<LpiSupplier>.Success(updated!);
    }

    public async Task<Result<string>> AllocateNextDocumentNumberAsync(
        string prefix,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return Result<string>.Fail(Error.Validation("Lpi.Sequence.InvalidPrefix", "Document sequence prefix is required."));
        }

        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        SqlitePragma.Apply(connection);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        string allocated = await AllocateSequenceInTransactionAsync(connection, transaction, prefix.Trim().ToUpperInvariant(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result<string>.Success(allocated);
    }

    private async Task<string> AllocateSequenceInTransactionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string normalizedPrefix,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        await using (SqliteCommand ensure = connection.CreateCommand())
        {
            ensure.Transaction = transaction;
            ensure.CommandText = """
                INSERT OR IGNORE INTO lpi_document_sequences (prefix, current_number, updated_at)
                VALUES ($prefix, 0, $now);
                """;
            ensure.Parameters.AddWithValue("$prefix", normalizedPrefix);
            ensure.Parameters.AddWithValue("$now", now.ToString("O"));
            await ensure.ExecuteNonQueryAsync(cancellationToken);
        }

        await using SqliteCommand update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE lpi_document_sequences
            SET current_number = current_number + 1,
                updated_at = $now
            WHERE prefix = $prefix;
            SELECT current_number FROM lpi_document_sequences WHERE prefix = $prefix;
            """;
        update.Parameters.AddWithValue("$prefix", normalizedPrefix);
        update.Parameters.AddWithValue("$now", now.ToString("O"));
        long next = Convert.ToInt64(await update.ExecuteScalarAsync(cancellationToken));
        return $"{normalizedPrefix}-{now.Year:D4}-{next:D5}";
    }

    private static async Task<LpiSupplier?> FindSupplierByIdAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid supplierId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            SELECT s.id, s.code, s.name, s.contact_person, s.contact_number, s.email, s.address, s.tin,
                   COALESCE(s.payment_terms_days, 30), COALESCE(s.lead_time_days, 3), COALESCE(s.default_currency, 'PHP'),
                   s.notes, s.is_active, s.created_at, s.updated_at,
                   COALESCE((SELECT SUM(i.remaining_balance_cents)
                             FROM supplier_invoices i
                             WHERE i.supplier_id = s.id AND i.status IN ('PostedOpen', 'PartiallyPaid')), 0),
                   COALESCE((SELECT COUNT(*)
                             FROM purchase_orders po
                             WHERE po.supplier_id = s.id AND po.status NOT IN ('Cancelled', 'Closed')), 0)
            FROM suppliers s
            WHERE s.id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", supplierId.ToString("D"));
        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSupplier(reader) : null;
    }

    private static LpiSupplier ReadSupplier(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetInt32(8),
            reader.GetInt32(9),
            reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.GetInt64(12) == 1,
            reader.GetInt64(15),
            reader.GetInt32(16),
            reader.IsDBNull(13) ? null : DateTimeOffset.Parse(reader.GetString(13)),
            reader.IsDBNull(14) ? null : DateTimeOffset.Parse(reader.GetString(14)));

    private static async Task<bool> SupplierNameExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string name,
        Guid? excludeId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            SELECT COUNT(*) FROM suppliers
            WHERE LOWER(TRIM(name)) = LOWER(TRIM($name))
              AND ($excludeId IS NULL OR id <> $excludeId);
            """;
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$excludeId", excludeId?.ToString("D") ?? (object)DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<bool> SupplierCodeExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string code,
        Guid? excludeId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            SELECT COUNT(*) FROM suppliers
            WHERE code IS NOT NULL AND LOWER(TRIM(code)) = LOWER(TRIM($code))
              AND ($excludeId IS NULL OR id <> $excludeId);
            """;
        cmd.Parameters.AddWithValue("$code", code);
        cmd.Parameters.AddWithValue("$excludeId", excludeId?.ToString("D") ?? (object)DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task WriteAuditAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid? actorUserId,
        string action,
        string entityType,
        string entityId,
        object? newValue,
        object? oldValue,
        string? reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO audit_logs (
                id, user_id, action, entity_type, entity_id, old_value_json, new_value_json, reason, created_at
            )
            VALUES (
                $id, $userId, $action, $entityType, $entityId, $oldJson, $newJson, $reason, $createdAt
            );
            """;
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        cmd.Parameters.AddWithValue("$userId", actorUserId is { } uid && uid != Guid.Empty ? uid.ToString("D") : DBNull.Value);
        cmd.Parameters.AddWithValue("$action", action);
        cmd.Parameters.AddWithValue("$entityType", entityType);
        cmd.Parameters.AddWithValue("$entityId", entityId);
        cmd.Parameters.AddWithValue("$oldJson", oldValue is null ? DBNull.Value : JsonSerializer.Serialize(oldValue));
        cmd.Parameters.AddWithValue("$newJson", newValue is null ? DBNull.Value : JsonSerializer.Serialize(newValue));
        cmd.Parameters.AddWithValue("$reason", (object?)Clean(reason) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$createdAt", now.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

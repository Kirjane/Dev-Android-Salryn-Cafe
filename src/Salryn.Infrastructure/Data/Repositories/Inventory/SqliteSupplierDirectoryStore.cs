using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Infrastructure.Data.Repositories.Inventory;

public sealed class SqliteSupplierDirectoryStore : SqliteRepositoryBase
{
    private static readonly JsonSerializerOptions CanonicalJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public SqliteSupplierDirectoryStore(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public Result<IReadOnlyList<SupplierConfig>> GetSuppliers()
    {
        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            return Result<IReadOnlyList<SupplierConfig>>.Success(ReadSuppliers(connection, null));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Result<IReadOnlyList<SupplierConfig>>.Fail(
                Error.Unexpected("Suppliers.ReadFailed", ex.Message));
        }
    }

    public Result<SupplierConfig> SaveSupplier(SupplierMutation mutation)
    {
        if (string.IsNullOrWhiteSpace(mutation.Name))
        {
            return Result<SupplierConfig>.Fail(
                Error.Validation("Suppliers.NameRequired", "Supplier name is required."));
        }

        Guid supplierId = mutation.Id.GetValueOrDefault();
        if (supplierId == Guid.Empty)
        {
            supplierId = Guid.NewGuid();
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            if (mutation.Id.HasValue &&
                !Exists(connection, transaction, "suppliers", "id", supplierId.ToString("D")))
            {
                transaction.Rollback();
                return Result<SupplierConfig>.Fail(
                    Error.NotFound("Suppliers.NotFound", "Supplier does not exist."));
            }

            if (mutation.Id.HasValue)
            {
                using SqliteCommand update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE suppliers
                    SET name=$name,
                        contact_number=$contact,
                        notes=$notes,
                        is_active=$active
                    WHERE id=$id;
                    """;
                BindSupplier(update, supplierId, mutation);
                if (update.ExecuteNonQuery() != 1)
                {
                    throw new InvalidOperationException("Supplier update did not affect exactly one row.");
                }
            }
            else
            {
                using SqliteCommand insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO suppliers(id,name,contact_number,notes,is_active)
                    VALUES($id,$name,$contact,$notes,$active);
                    """;
                BindSupplier(insert, supplierId, mutation);
                insert.ExecuteNonQuery();
            }

            SupplierConfig saved = ReadSupplier(connection, transaction, supplierId)
                ?? throw new InvalidOperationException("Saved supplier could not be read.");
            transaction.Commit();
            return Result<SupplierConfig>.Success(saved);
        }
        catch (SqliteException ex) when (IsUniqueViolation(ex))
        {
            return Result<SupplierConfig>.Fail(
                Error.Conflict("Suppliers.Conflict", ex.Message));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Result<SupplierConfig>.Fail(
                Error.Unexpected("Suppliers.SaveFailed", ex.Message));
        }
    }

    public Result<SupplierDirectoryPublication> PublishCurrent(
        Guid storeId,
        DateTimeOffset? publishedAtUtc = null)
    {
        if (storeId == Guid.Empty)
        {
            return Result<SupplierDirectoryPublication>.Fail(
                Error.Validation("Suppliers.StoreRequired", "Store identity is required."));
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Guid localStore = ReadStoreId(connection, transaction);
            if (localStore != storeId)
            {
                transaction.Rollback();
                return Result<SupplierDirectoryPublication>.Fail(
                    Error.Conflict("Suppliers.StoreMismatch", "Supplier publication store does not match deployment authority."));
            }

            IReadOnlyList<SupplierConfig> suppliers = ReadSuppliers(connection, transaction);
            SupplierSnapshot[] snapshots = suppliers
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Id)
                .Select((x, index) => new SupplierSnapshot(
                    x.Id,
                    x.Name,
                    x.ContactNumber,
                    x.Notes,
                    x.IsActive,
                    index))
                .ToArray();

            string contentHash = ComputeContentHash(snapshots);
            SupplierDirectoryPublication? current = ReadCurrent(connection, transaction, storeId);
            if (current is not null &&
                string.Equals(current.Summary.ContentHash, contentHash, StringComparison.Ordinal))
            {
                transaction.Commit();
                return Result<SupplierDirectoryPublication>.Success(current);
            }

            int revisionNumber = (current?.Summary.RevisionNumber ?? 0) + 1;
            DateTimeOffset at = (publishedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
            Guid revisionId = DeriveRevisionId(storeId, revisionNumber, contentHash);
            string publicationHash = ComputePublicationHash(
                revisionId,
                storeId,
                revisionNumber,
                contentHash,
                at);

            using (SqliteCommand revision = connection.CreateCommand())
            {
                revision.Transaction = transaction;
                revision.CommandText = """
                    INSERT INTO supplier_directory_revisions(
                        revision_id,store_id,revision_number,content_hash,publication_hash,published_at_utc)
                    VALUES($revision,$store,$number,$content,$publication,$at);
                    """;
                revision.Parameters.AddWithValue("$revision", revisionId.ToString("D"));
                revision.Parameters.AddWithValue("$store", storeId.ToString("D"));
                revision.Parameters.AddWithValue("$number", revisionNumber);
                revision.Parameters.AddWithValue("$content", contentHash);
                revision.Parameters.AddWithValue("$publication", publicationHash);
                revision.Parameters.AddWithValue("$at", at.ToString("O"));
                revision.ExecuteNonQuery();
            }

            foreach (SupplierSnapshot supplier in snapshots)
            {
                InsertSnapshot(connection, transaction, revisionId, supplier);
            }

            using (SqliteCommand currentPointer = connection.CreateCommand())
            {
                currentPointer.Transaction = transaction;
                currentPointer.CommandText = """
                    INSERT INTO supplier_directory_current_publication(
                        store_id,revision_id,content_hash,publication_hash,applied_at_utc)
                    VALUES($store,$revision,$content,$publication,$at)
                    ON CONFLICT(store_id) DO UPDATE SET
                        revision_id=excluded.revision_id,
                        content_hash=excluded.content_hash,
                        publication_hash=excluded.publication_hash,
                        applied_at_utc=excluded.applied_at_utc;
                    """;
                currentPointer.Parameters.AddWithValue("$store", storeId.ToString("D"));
                currentPointer.Parameters.AddWithValue("$revision", revisionId.ToString("D"));
                currentPointer.Parameters.AddWithValue("$content", contentHash);
                currentPointer.Parameters.AddWithValue("$publication", publicationHash);
                currentPointer.Parameters.AddWithValue("$at", at.ToString("O"));
                currentPointer.ExecuteNonQuery();
            }

            SupplierDirectoryPublication publication = new(
                new SupplierPublicationSummary(
                    revisionId,
                    storeId,
                    revisionNumber,
                    contentHash,
                    publicationHash,
                    at),
                snapshots);

            transaction.Commit();
            return Result<SupplierDirectoryPublication>.Success(publication);
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or JsonException)
        {
            return Result<SupplierDirectoryPublication>.Fail(
                Error.Unexpected("Suppliers.PublishFailed", ex.Message));
        }
    }

    public Result<SupplierDirectoryPublication?> GetCurrentPublication(Guid storeId)
    {
        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            SupplierDirectoryPublication? publication = ReadCurrent(connection, transaction, storeId);
            transaction.Commit();
            return Result<SupplierDirectoryPublication?>.Success(publication);
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Result<SupplierDirectoryPublication?>.Fail(
                Error.Unexpected("Suppliers.ReadPublicationFailed", ex.Message));
        }
    }

    public Result<SupplierPublicationApplyResult> ApplyPublication(
        SupplierDirectoryPublication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);

        string expectedContent = ComputeContentHash(publication.Suppliers);
        string expectedPublication = ComputePublicationHash(
            publication.Summary.RevisionId,
            publication.Summary.StoreId,
            publication.Summary.RevisionNumber,
            publication.Summary.ContentHash,
            publication.Summary.PublishedAtUtc);

        if (!string.Equals(expectedContent, publication.Summary.ContentHash, StringComparison.Ordinal) ||
            !string.Equals(expectedPublication, publication.Summary.PublicationHash, StringComparison.Ordinal))
        {
            return Result<SupplierPublicationApplyResult>.Fail(
                Error.Conflict("Suppliers.PublicationIntegrity", "Supplier publication hash does not match canonical content."));
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Guid localStore = ReadStoreId(connection, transaction);
            if (localStore != publication.Summary.StoreId)
            {
                transaction.Rollback();
                return Result<SupplierPublicationApplyResult>.Fail(
                    Error.Conflict("Suppliers.PublicationStoreMismatch", "Supplier publication belongs to another store."));
            }

            SupplierDirectoryPublication? current = ReadCurrent(
                connection,
                transaction,
                publication.Summary.StoreId);

            if (current is not null)
            {
                if (current.Summary.RevisionId == publication.Summary.RevisionId)
                {
                    bool exact =
                        current.Summary.RevisionNumber == publication.Summary.RevisionNumber &&
                        string.Equals(current.Summary.ContentHash, publication.Summary.ContentHash, StringComparison.Ordinal) &&
                        string.Equals(current.Summary.PublicationHash, publication.Summary.PublicationHash, StringComparison.Ordinal);
                    transaction.Commit();
                    if (exact)
                    {
                        return Result<SupplierPublicationApplyResult>.Success(
                            new SupplierPublicationApplyResult(false, true, current.Summary));
                    }

                    return Result<SupplierPublicationApplyResult>.Fail(
                        Error.Conflict("Suppliers.PublicationIdentityConflict", "Supplier revision identity already exists with different content."));
                }

                if (publication.Summary.RevisionNumber < current.Summary.RevisionNumber)
                {
                    transaction.Rollback();
                    return Result<SupplierPublicationApplyResult>.Fail(
                        Error.Conflict("Suppliers.PublicationStale", "Stale supplier publication cannot replace current authority."));
                }

                if (publication.Summary.RevisionNumber == current.Summary.RevisionNumber)
                {
                    transaction.Rollback();
                    return Result<SupplierPublicationApplyResult>.Fail(
                        Error.Conflict("Suppliers.PublicationRevisionConflict", "Different supplier publication claims the current revision number."));
                }
            }

            using (SqliteCommand revision = connection.CreateCommand())
            {
                revision.Transaction = transaction;
                revision.CommandText = """
                    INSERT INTO supplier_directory_revisions(
                        revision_id,store_id,revision_number,content_hash,publication_hash,published_at_utc)
                    VALUES($revision,$store,$number,$content,$publication,$at);
                    """;
                revision.Parameters.AddWithValue("$revision", publication.Summary.RevisionId.ToString("D"));
                revision.Parameters.AddWithValue("$store", publication.Summary.StoreId.ToString("D"));
                revision.Parameters.AddWithValue("$number", publication.Summary.RevisionNumber);
                revision.Parameters.AddWithValue("$content", publication.Summary.ContentHash);
                revision.Parameters.AddWithValue("$publication", publication.Summary.PublicationHash);
                revision.Parameters.AddWithValue("$at", publication.Summary.PublishedAtUtc.ToUniversalTime().ToString("O"));
                revision.ExecuteNonQuery();
            }

            foreach (SupplierSnapshot supplier in publication.Suppliers.OrderBy(x => x.DisplayOrder).ThenBy(x => x.SupplierId))
            {
                InsertSnapshot(connection, transaction, publication.Summary.RevisionId, supplier);

                using SqliteCommand upsertSupplier = connection.CreateCommand();
                upsertSupplier.Transaction = transaction;
                upsertSupplier.CommandText = """
                    INSERT INTO suppliers(id,name,contact_number,notes,is_active)
                    VALUES($id,$name,$contact,$notes,$active)
                    ON CONFLICT(id) DO UPDATE SET
                        name=excluded.name,
                        contact_number=excluded.contact_number,
                        notes=excluded.notes,
                        is_active=excluded.is_active;
                    """;
                upsertSupplier.Parameters.AddWithValue("$id", supplier.SupplierId.ToString("D"));
                upsertSupplier.Parameters.AddWithValue("$name", supplier.Name);
                upsertSupplier.Parameters.AddWithValue("$contact", (object?)supplier.ContactNumber ?? DBNull.Value);
                upsertSupplier.Parameters.AddWithValue("$notes", (object?)supplier.Notes ?? DBNull.Value);
                upsertSupplier.Parameters.AddWithValue("$active", supplier.IsActive ? 1 : 0);
                upsertSupplier.ExecuteNonQuery();
            }

            using (SqliteCommand pointer = connection.CreateCommand())
            {
                pointer.Transaction = transaction;
                pointer.CommandText = """
                    INSERT INTO supplier_directory_current_publication(
                        store_id,revision_id,content_hash,publication_hash,applied_at_utc)
                    VALUES($store,$revision,$content,$publication,$at)
                    ON CONFLICT(store_id) DO UPDATE SET
                        revision_id=excluded.revision_id,
                        content_hash=excluded.content_hash,
                        publication_hash=excluded.publication_hash,
                        applied_at_utc=excluded.applied_at_utc;
                    """;
                pointer.Parameters.AddWithValue("$store", publication.Summary.StoreId.ToString("D"));
                pointer.Parameters.AddWithValue("$revision", publication.Summary.RevisionId.ToString("D"));
                pointer.Parameters.AddWithValue("$content", publication.Summary.ContentHash);
                pointer.Parameters.AddWithValue("$publication", publication.Summary.PublicationHash);
                pointer.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
                pointer.ExecuteNonQuery();
            }

            transaction.Commit();
            return Result<SupplierPublicationApplyResult>.Success(
                new SupplierPublicationApplyResult(true, false, publication.Summary));
        }
        catch (SqliteException ex) when (IsUniqueViolation(ex))
        {
            return Result<SupplierPublicationApplyResult>.Fail(
                Error.Conflict("Suppliers.PublicationConflict", ex.Message));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Result<SupplierPublicationApplyResult>.Fail(
                Error.Unexpected("Suppliers.ApplyPublicationFailed", ex.Message));
        }
    }

    internal static SupplierDirectoryPublication? ReadCurrent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId)
    {
        string? revisionText;
        using (SqliteCommand current = connection.CreateCommand())
        {
            current.Transaction = transaction;
            current.CommandText = """
                SELECT revision_id
                FROM supplier_directory_current_publication
                WHERE store_id=$store
                LIMIT 1;
                """;
            current.Parameters.AddWithValue("$store", storeId.ToString("D"));
            revisionText = current.ExecuteScalar() as string;
        }

        if (revisionText is null || !Guid.TryParse(revisionText, out Guid revisionId))
        {
            return null;
        }

        SupplierPublicationSummary summary;
        using (SqliteCommand revision = connection.CreateCommand())
        {
            revision.Transaction = transaction;
            revision.CommandText = """
                SELECT store_id,revision_number,content_hash,publication_hash,published_at_utc
                FROM supplier_directory_revisions
                WHERE revision_id=$revision;
                """;
            revision.Parameters.AddWithValue("$revision", revisionId.ToString("D"));
            using SqliteDataReader reader = revision.ExecuteReader();
            if (!reader.Read())
            {
                throw new InvalidOperationException("Supplier current publication points to a missing revision.");
            }

            summary = new SupplierPublicationSummary(
                revisionId,
                Guid.Parse(reader.GetString(0)),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4)));
        }

        List<SupplierSnapshot> suppliers = new();
        using (SqliteCommand items = connection.CreateCommand())
        {
            items.Transaction = transaction;
            items.CommandText = """
                SELECT supplier_id,name,contact_number,notes,is_active,display_order
                FROM supplier_directory_revision_items
                WHERE revision_id=$revision
                ORDER BY display_order,supplier_id;
                """;
            items.Parameters.AddWithValue("$revision", revisionId.ToString("D"));
            using SqliteDataReader reader = items.ExecuteReader();
            while (reader.Read())
            {
                suppliers.Add(new SupplierSnapshot(
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetInt32(4) == 1,
                    reader.GetInt32(5)));
            }
        }

        return new SupplierDirectoryPublication(summary, suppliers);
    }

    internal static string ComputeContentHash(IReadOnlyList<SupplierSnapshot> suppliers)
    {
        var canonical = suppliers
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.SupplierId)
            .Select(x => new
            {
                supplierId = x.SupplierId.ToString("D"),
                name = x.Name,
                contactNumber = x.ContactNumber,
                notes = x.Notes,
                isActive = x.IsActive,
                displayOrder = x.DisplayOrder
            })
            .ToArray();

        return Sha(JsonSerializer.Serialize(canonical, CanonicalJson));
    }

    internal static string ComputePublicationHash(
        Guid revisionId,
        Guid storeId,
        int revisionNumber,
        string contentHash,
        DateTimeOffset publishedAtUtc) =>
        Sha(JsonSerializer.Serialize(new
        {
            revisionId = revisionId.ToString("D"),
            storeId = storeId.ToString("D"),
            revisionNumber,
            contentHash,
            publishedAtUtc = publishedAtUtc.ToUniversalTime().ToString("O")
        }, CanonicalJson));

    private static Guid DeriveRevisionId(Guid storeId, int revisionNumber, string contentHash)
    {
        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                storeId.ToString("D") + "|" + revisionNumber + "|" + contentHash));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes);
    }

    private static string Sha(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static IReadOnlyList<SupplierConfig> ReadSuppliers(
        SqliteConnection connection,
        SqliteTransaction? transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id,name,contact_number,notes,is_active
            FROM suppliers
            ORDER BY name COLLATE NOCASE,id;
            """;

        using SqliteDataReader reader = command.ExecuteReader();
        List<SupplierConfig> suppliers = new();
        while (reader.Read())
        {
            suppliers.Add(new SupplierConfig(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt32(4) == 1));
        }
        return suppliers;
    }

    private static SupplierConfig? ReadSupplier(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid supplierId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id,name,contact_number,notes,is_active
            FROM suppliers WHERE id=$id LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", supplierId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new SupplierConfig(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt32(4) == 1)
            : null;
    }

    private static void InsertSnapshot(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid revisionId,
        SupplierSnapshot supplier)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO supplier_directory_revision_items(
                revision_id,supplier_id,name,contact_number,notes,is_active,display_order)
            VALUES($revision,$supplier,$name,$contact,$notes,$active,$order);
            """;
        command.Parameters.AddWithValue("$revision", revisionId.ToString("D"));
        command.Parameters.AddWithValue("$supplier", supplier.SupplierId.ToString("D"));
        command.Parameters.AddWithValue("$name", supplier.Name);
        command.Parameters.AddWithValue("$contact", (object?)supplier.ContactNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$notes", (object?)supplier.Notes ?? DBNull.Value);
        command.Parameters.AddWithValue("$active", supplier.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("$order", supplier.DisplayOrder);
        command.ExecuteNonQuery();
    }

    private static void BindSupplier(
        SqliteCommand command,
        Guid supplierId,
        SupplierMutation mutation)
    {
        command.Parameters.AddWithValue("$id", supplierId.ToString("D"));
        command.Parameters.AddWithValue("$name", mutation.Name.Trim());
        command.Parameters.AddWithValue("$contact", string.IsNullOrWhiteSpace(mutation.ContactNumber)
            ? DBNull.Value : mutation.ContactNumber.Trim());
        command.Parameters.AddWithValue("$notes", string.IsNullOrWhiteSpace(mutation.Notes)
            ? DBNull.Value : mutation.Notes.Trim());
        command.Parameters.AddWithValue("$active", mutation.IsActive ? 1 : 0);
    }

    private static Guid ReadStoreId(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT store_id FROM deployment_identity ORDER BY created_at LIMIT 1;";
        object? value = command.ExecuteScalar();
        return value is string text && Guid.TryParse(text, out Guid id)
            ? id
            : throw new InvalidOperationException("Deployment StoreId is missing.");
    }

    private static bool Exists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string column,
        string value)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM " + table + " WHERE " + column + "=$value LIMIT 1;";
        command.Parameters.AddWithValue("$value", value);
        return command.ExecuteScalar() is not null;
    }

    private static bool IsUniqueViolation(SqliteException ex) =>
        ex.SqliteExtendedErrorCode is 1555 or 2067 ||
        ex.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase);
}

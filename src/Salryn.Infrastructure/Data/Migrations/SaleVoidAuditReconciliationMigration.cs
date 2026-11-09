using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

/// <summary>
/// Reconstructs the operator audit row that older host void code failed to append.
/// The authoritative sale row supplies actor, reason, and timestamp; the marker in
/// new_value_json makes the reconciliation explicit instead of pretending it was
/// written at the original request boundary.
/// </summary>
public sealed class SaleVoidAuditReconciliationMigration : IDatabaseMigration
{
    public int Version => 27;
    public string Name => "Reconcile missing sale void audit rows";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        List<MissingVoidAudit> missing = new();
        using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = """
                SELECT s.id, s.voided_by_user_id, s.receipt_no, s.void_reason, s.voided_at
                FROM sales s
                WHERE s.status = 3
                  AND s.voided_at IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM audit_logs a
                      WHERE a.action = 'SALE_VOIDED'
                        AND a.entity_type = 'Sale'
                        AND a.entity_id = s.id
                  )
                ORDER BY s.voided_at, s.id;
                """;
            using SqliteDataReader reader = read.ExecuteReader();
            while (reader.Read())
            {
                missing.Add(new MissingVoidAudit(
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4)));
            }
        }

        foreach (MissingVoidAudit row in missing)
        {
            using SqliteCommand insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO audit_logs (
                    id, user_id, action, entity_type, entity_id,
                    old_value_json, new_value_json, reason, created_at)
                VALUES (
                    $id, $userId, 'SALE_VOIDED', 'Sale', $saleId,
                    $oldValue, $newValue, $reason, $createdAt);
                """;
            insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            insert.Parameters.AddWithValue("$userId", (object?)row.UserId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$saleId", row.SaleId);
            insert.Parameters.AddWithValue("$oldValue", JsonSerializer.Serialize(new { status = "Completed" }));
            insert.Parameters.AddWithValue("$newValue", JsonSerializer.Serialize(new
            {
                saleId = row.SaleId,
                receiptNo = row.ReceiptNo,
                status = "Voided",
                reconciledFromSaleRecord = true
            }));
            insert.Parameters.AddWithValue("$reason", (object?)row.Reason ?? DBNull.Value);
            insert.Parameters.AddWithValue("$createdAt", row.VoidedAt);
            insert.ExecuteNonQuery();
        }
    }

    private sealed record MissingVoidAudit(
        string SaleId,
        string? UserId,
        string ReceiptNo,
        string? Reason,
        string VoidedAt);
}

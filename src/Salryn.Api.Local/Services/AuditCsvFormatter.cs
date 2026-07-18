using System.Globalization;
using System.Text;
using Salryn.Core.ReadModels;
using Salryn.Infrastructure.Data.Repositories.System;

namespace Salryn.Api.Local.Services;

/// <summary>
/// Builds UTF-8 CSV for filtered audit export (RFC-style quoting).
/// </summary>
public static class AuditCsvFormatter
{
    public const int ExportMaxRows = SqliteAuditReadRepository.DefaultExportMaxRows;

    public static readonly string[] Headers =
    {
        "Date/Time",
        "User",
        "Action",
        "Entity Type",
        "Entity ID",
        "Reason",
        "Details"
    };

    public static string BuildFileName(DateTimeOffset timestamp) =>
        "salryn-audit-" + timestamp.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".csv";

    public static byte[] BuildCsv(IReadOnlyList<AuditLogExportRow> rows)
    {
        StringBuilder builder = new();
        builder.AppendLine(string.Join(",", Headers.Select(Escape)));

        foreach (AuditLogExportRow row in rows)
        {
            builder.Append(Escape(row.CreatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
            builder.Append(',');
            builder.Append(Escape(row.UserDisplayName));
            builder.Append(',');
            builder.Append(Escape(row.Action));
            builder.Append(',');
            builder.Append(Escape(row.EntityType));
            builder.Append(',');
            builder.Append(Escape(row.EntityId));
            builder.Append(',');
            builder.Append(Escape(row.Reason));
            builder.Append(',');
            builder.Append(Escape(SqliteAuditReadRepository.SanitizeAuditDetails(row.Details) ?? row.Details));
            builder.AppendLine();
        }

        // UTF-8 with BOM helps Excel open non-ASCII correctly.
        byte[] utf8 = Encoding.UTF8.GetBytes(builder.ToString());
        byte[] bom = Encoding.UTF8.GetPreamble();
        byte[] result = new byte[bom.Length + utf8.Length];
        Buffer.BlockCopy(bom, 0, result, 0, bom.Length);
        Buffer.BlockCopy(utf8, 0, result, bom.Length, utf8.Length);
        return result;
    }

    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        bool needsQuotes = value.Contains(',')
            || value.Contains('"')
            || value.Contains('\r')
            || value.Contains('\n');

        string escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        return needsQuotes ? "\"" + escaped + "\"" : escaped;
    }
}

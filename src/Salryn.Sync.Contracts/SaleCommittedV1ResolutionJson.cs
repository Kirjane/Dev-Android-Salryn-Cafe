using System.Text;
using System.Text.Json;

namespace Salryn.Sync.Contracts;

public static class SaleCommittedV1ResolutionJson
{
    public static string Create(
        Guid operationId,
        Guid saleId,
        string status,
        string resultCode,
        string? detail)
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("operation_id", operationId.ToString("D"));
            writer.WriteString("sale_id", saleId.ToString("D"));
            writer.WriteString("status", status);
            writer.WriteString("result_code", resultCode);
            if (detail is null)
            {
                writer.WriteNull("detail");
            }
            else
            {
                writer.WriteString("detail", detail);
            }
            writer.WriteEndObject();
            writer.Flush();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Salryn.Sync.Contracts;

public sealed record CafeInventoryCanonicalParseResult<T>(
    bool IsValid,
    T? Payload,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static CafeInventoryCanonicalParseResult<T> Success(T payload) =>
        new(true, payload, null, null);

    public static CafeInventoryCanonicalParseResult<T> Fail(string code, string message) =>
        new(false, default, code, message);
}

public static class CafeInventoryOperationCanonicalJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string Serialize(PurchaseReceiptCommittedV1 payload) =>
        JsonSerializer.Serialize(Normalize(payload), Options);

    public static string Serialize(InventoryWasteCommittedV1 payload) =>
        JsonSerializer.Serialize(Normalize(payload), Options);

    public static string Serialize(InventoryAdjustmentCommittedV1 payload) =>
        JsonSerializer.Serialize(payload, Options);

    public static string Serialize(InventoryCountFinalizedV1 payload) =>
        JsonSerializer.Serialize(Normalize(payload), Options);

    public static string ComputeHash(string canonicalJson) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)))
        .ToLowerInvariant();

    public static CafeInventoryCanonicalParseResult<PurchaseReceiptCommittedV1> ParsePurchase(
        string json) =>
        Parse<PurchaseReceiptCommittedV1>(
            json,
            Serialize,
            ValidatePurchase,
            "PurchaseReceipt");

    public static CafeInventoryCanonicalParseResult<InventoryWasteCommittedV1> ParseWaste(
        string json) =>
        Parse<InventoryWasteCommittedV1>(
            json,
            Serialize,
            ValidateWaste,
            "InventoryWaste");

    public static CafeInventoryCanonicalParseResult<InventoryAdjustmentCommittedV1> ParseAdjustment(
        string json) =>
        Parse<InventoryAdjustmentCommittedV1>(
            json,
            Serialize,
            ValidateAdjustment,
            "InventoryAdjustment");

    public static CafeInventoryCanonicalParseResult<InventoryCountFinalizedV1> ParseCount(
        string json) =>
        Parse<InventoryCountFinalizedV1>(
            json,
            Serialize,
            ValidateCount,
            "InventoryCount");

    private static CafeInventoryCanonicalParseResult<T> Parse<T>(
        string json,
        Func<T, string> serialize,
        Func<T, string?> validate,
        string code)
    {
        try
        {
            T? payload = JsonSerializer.Deserialize<T>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (payload is null)
            {
                return CafeInventoryCanonicalParseResult<T>.Fail(
                    code + ".PayloadMissing",
                    "Payload is missing.");
            }

            string? error = validate(payload);
            if (error is not null)
            {
                return CafeInventoryCanonicalParseResult<T>.Fail(
                    code + ".PayloadInvalid",
                    error);
            }

            string canonical = serialize(payload);
            if (!string.Equals(canonical, json, StringComparison.Ordinal))
            {
                return CafeInventoryCanonicalParseResult<T>.Fail(
                    code + ".PayloadNotCanonical",
                    "Payload is not canonical JSON.");
            }

            return CafeInventoryCanonicalParseResult<T>.Success(payload);
        }
        catch (JsonException ex)
        {
            return CafeInventoryCanonicalParseResult<T>.Fail(
                code + ".PayloadInvalidJson",
                ex.Message);
        }
    }

    private static PurchaseReceiptCommittedV1 Normalize(PurchaseReceiptCommittedV1 payload) =>
        payload with
        {
            Lines = payload.Lines
                .OrderBy(x => x.LineId)
                .ToArray()
        };

    private static InventoryWasteCommittedV1 Normalize(InventoryWasteCommittedV1 payload) =>
        payload with
        {
            Lines = payload.Lines
                .OrderBy(x => x.LineId)
                .ToArray()
        };

    private static InventoryCountFinalizedV1 Normalize(InventoryCountFinalizedV1 payload) =>
        payload with
        {
            Lines = payload.Lines
                .OrderBy(x => x.LineId)
                .ToArray()
        };

    private static string? ValidatePurchase(PurchaseReceiptCommittedV1 payload)
    {
        if (payload.PurchaseReceiptId == Guid.Empty ||
            payload.SupplierId == Guid.Empty ||
            payload.ActorUserId == Guid.Empty ||
            string.IsNullOrWhiteSpace(payload.SupplierNameSnapshot))
        {
            return "Purchase, supplier, actor and supplier snapshot are required.";
        }

        if (payload.Lines is null || payload.Lines.Count == 0)
        {
            return "Purchase lines are required.";
        }

        if (payload.Lines.Any(x =>
                x.QuantityDeltaX1000 == 0 ||
                !x.AcquisitionCostCents.HasValue))
        {
            return "Purchase lines require non-zero quantity and acquisition-cost evidence.";
        }

        if (payload.TotalAcquisitionCostCents !=
            payload.Lines.Sum(x => x.AcquisitionCostCents!.Value))
        {
            return "Purchase total must equal exact line acquisition-cost cents.";
        }

        return ValidateLines(payload.Lines, requireCountEvidence: false);
    }

    private static string? ValidateWaste(InventoryWasteCommittedV1 payload)
    {
        if (payload.WasteEventId == Guid.Empty ||
            payload.ActorUserId == Guid.Empty ||
            string.IsNullOrWhiteSpace(payload.Reason))
        {
            return "Waste identity, actor and reason are required.";
        }

        if (payload.Lines is null || payload.Lines.Count == 0)
        {
            return "Waste lines are required.";
        }

        if (payload.Lines.Any(x => x.QuantityDeltaX1000 == 0))
        {
            return "Waste lines require non-zero canonical delta.";
        }

        return ValidateLines(payload.Lines, requireCountEvidence: false);
    }

    private static string? ValidateAdjustment(InventoryAdjustmentCommittedV1 payload)
    {
        if (payload.AdjustmentEventId == Guid.Empty ||
            payload.StockTargetId == Guid.Empty ||
            payload.ActorUserId == Guid.Empty ||
            payload.QuantityDeltaX1000 == 0 ||
            string.IsNullOrWhiteSpace(payload.NameSnapshot) ||
            string.IsNullOrWhiteSpace(payload.StockUnitSnapshot) ||
            string.IsNullOrWhiteSpace(payload.Reason))
        {
            return "Adjustment identities, snapshots, reason and non-zero delta are required.";
        }

        return ValidateTargetType(payload.StockTargetType);
    }

    private static string? ValidateCount(InventoryCountFinalizedV1 payload)
    {
        if (payload.CountSessionId == Guid.Empty ||
            payload.FinalizedByUserId == Guid.Empty)
        {
            return "Count and finalizer identities are required.";
        }

        if (payload.Lines is null || payload.Lines.Count == 0)
        {
            return "Count lines are required.";
        }

        return ValidateLines(payload.Lines, requireCountEvidence: true);
    }

    private static string? ValidateLines(
        IReadOnlyList<CafeInventoryTargetDeltaV1> lines,
        bool requireCountEvidence)
    {
        HashSet<Guid> lineIds = new();
        HashSet<string> targets = new(StringComparer.Ordinal);

        foreach (CafeInventoryTargetDeltaV1 line in lines)
        {
            if (line.LineId == Guid.Empty ||
                line.StockTargetId == Guid.Empty ||
                string.IsNullOrWhiteSpace(line.NameSnapshot) ||
                string.IsNullOrWhiteSpace(line.StockUnitSnapshot))
            {
                return "Line identities and target snapshots are required.";
            }

            string? typeError = ValidateTargetType(line.StockTargetType);
            if (typeError is not null)
            {
                return typeError;
            }

            if (!lineIds.Add(line.LineId))
            {
                return "Line identities must be unique.";
            }

            string targetKey = line.StockTargetType + ":" + line.StockTargetId.ToString("D");
            if (!targets.Add(targetKey))
            {
                return "A stock target may appear only once in one operation.";
            }

            if (requireCountEvidence)
            {
                if (!line.ExpectedBeforeX1000.HasValue ||
                    !line.CountedX1000.HasValue ||
                    line.ExpectedBeforeX1000.Value < 0 ||
                    line.CountedX1000.Value < 0 ||
                    line.QuantityDeltaX1000 !=
                        line.CountedX1000.Value - line.ExpectedBeforeX1000.Value)
                {
                    return "Count expected/count/delta evidence is inconsistent.";
                }
            }
        }

        return null;
    }

    private static string? ValidateTargetType(string value) =>
        string.Equals(value, "Ingredient", StringComparison.Ordinal) ||
        string.Equals(value, "Product", StringComparison.Ordinal)
            ? null
            : "Stock target type must be Ingredient or Product.";
}

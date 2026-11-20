using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Salryn.Sync.Contracts;

public sealed record IngredientConsumptionLineV1(
    string LineId,
    Guid SaleItemId,
    Guid IngredientId,
    string RecipeId,
    string RecipeRevisionId,
    string SourceBindingType,
    string SourceBindingId,
    long QuantityConsumedX1000,
    string StockUnit);

public sealed record IngredientConsumptionCommittedV1(
    Guid SourceSaleOperationId,
    Guid SaleId,
    string RecipeBookRevisionId,
    string DecisionHash,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyList<IngredientConsumptionLineV1> Lines);

public static class IngredientConsumptionCommittedV1Contract
{
    public const string OperationType = "IngredientConsumptionCommittedV1";
    public const int SchemaVersion = 1;
    public const string Applied = "Applied";
    public const string Rejected = "Rejected";
    public const string NeedsReview = "NeedsReview";

    public static Guid DeriveOperationId(Guid sourceSaleOperationId)
    {
        if (sourceSaleOperationId == Guid.Empty)
        {
            throw new ArgumentException("Source sale operation ID is required.", nameof(sourceSaleOperationId));
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            "salryn-c2-ingredient-consumption-v1|" + sourceSaleOperationId.ToString("D")));
        byte[] bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}


public static class IngredientConsumptionDecisionHashV1
{
    public const string AppliedResult = "APPLIED";
    public const string NoRecipeAppliedResult = "NO_RECIPE_APPLIED";

    public static string Compute(
        Guid saleId,
        Guid? sourceSaleOperationId,
        string? recipeBookRevisionId,
        string result,
        IEnumerable<IngredientConsumptionLineV1> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        StringBuilder builder = new();
        Append(builder, saleId.ToString("D"));
        Append(builder, sourceSaleOperationId?.ToString("D"));
        Append(builder, recipeBookRevisionId);
        Append(builder, result);

        foreach (IngredientConsumptionLineV1 line in lines
            .OrderBy(x => x.SaleItemId)
            .ThenBy(x => x.IngredientId)
            .ThenBy(x => x.LineId, StringComparer.Ordinal))
        {
            Append(
                builder,
                line.LineId,
                line.SaleItemId.ToString("D"),
                line.IngredientId.ToString("D"),
                line.RecipeId,
                line.RecipeRevisionId,
                line.SourceBindingType,
                line.SourceBindingId,
                line.QuantityConsumedX1000.ToString(CultureInfo.InvariantCulture),
                line.StockUnit);
        }

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static void Append(StringBuilder builder, params string?[] values)
    {
        foreach (string? value in values)
        {
            builder.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty)));
            builder.Append('|');
        }

        builder.Append('\n');
    }
}

public sealed record IngredientConsumptionValidationResult(
    bool IsValid,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static IngredientConsumptionValidationResult Success() => new(true, null, null);
    public static IngredientConsumptionValidationResult Fail(string code, string message) =>
        new(false, code, message);
}

public static class IngredientConsumptionCommittedV1CanonicalJson
{
    private static readonly Regex LowerHexSha256 =
        new("^[0-9a-f]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Serialize(IngredientConsumptionCommittedV1 payload)
    {
        IngredientConsumptionValidationResult validation = Validate(payload);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                validation.ErrorCode + ": " + validation.ErrorMessage);
        }

        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("sourceSaleOperationId", payload.SourceSaleOperationId.ToString("D"));
            writer.WriteString("saleId", payload.SaleId.ToString("D"));
            writer.WriteString("recipeBookRevisionId", payload.RecipeBookRevisionId);
            writer.WriteString("decisionHash", payload.DecisionHash);
            writer.WriteString("actorUserId", payload.ActorUserId.ToString("D"));
            writer.WriteString("occurredAtUtc", payload.OccurredAtUtc.ToUniversalTime().ToString("O"));
            writer.WritePropertyName("lines");
            writer.WriteStartArray();
            foreach (IngredientConsumptionLineV1 line in payload.Lines
                .OrderBy(x => x.SaleItemId)
                .ThenBy(x => x.IngredientId)
                .ThenBy(x => x.LineId, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("lineId", line.LineId);
                writer.WriteString("saleItemId", line.SaleItemId.ToString("D"));
                writer.WriteString("ingredientId", line.IngredientId.ToString("D"));
                writer.WriteString("recipeId", line.RecipeId);
                writer.WriteString("recipeRevisionId", line.RecipeRevisionId);
                writer.WriteString("sourceBindingType", line.SourceBindingType);
                writer.WriteString("sourceBindingId", line.SourceBindingId);
                writer.WriteNumber("quantityConsumedX1000", line.QuantityConsumedX1000);
                writer.WriteString("stockUnit", line.StockUnit);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static IngredientConsumptionCanonicalParseResult Parse(string json)
    {
        try
        {
            IngredientConsumptionCommittedV1? payload =
                JsonSerializer.Deserialize<IngredientConsumptionCommittedV1>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (payload is null)
            {
                return IngredientConsumptionCanonicalParseResult.Fail(
                    "IngredientConsumption.PayloadMissing", "Ingredient consumption payload is missing.");
            }

            IngredientConsumptionValidationResult validation = Validate(payload);
            if (!validation.IsValid)
            {
                return IngredientConsumptionCanonicalParseResult.Fail(
                    validation.ErrorCode!, validation.ErrorMessage!);
            }

            string canonical = Serialize(payload);
            if (!string.Equals(canonical, json, StringComparison.Ordinal))
            {
                return IngredientConsumptionCanonicalParseResult.Fail(
                    "IngredientConsumption.PayloadNotCanonical",
                    "Ingredient consumption payload is not canonical JSON.");
            }

            return IngredientConsumptionCanonicalParseResult.Success(payload);
        }
        catch (JsonException ex)
        {
            return IngredientConsumptionCanonicalParseResult.Fail(
                "IngredientConsumption.PayloadInvalidJson", ex.Message);
        }
    }

    public static IngredientConsumptionValidationResult Validate(
        IngredientConsumptionCommittedV1 payload)
    {
        if (payload.SourceSaleOperationId == Guid.Empty ||
            payload.SaleId == Guid.Empty ||
            payload.ActorUserId == Guid.Empty ||
            string.IsNullOrWhiteSpace(payload.RecipeBookRevisionId))
        {
            return IngredientConsumptionValidationResult.Fail(
                "IngredientConsumption.IdentityRequired",
                "Source sale, sale, actor, and RecipeBook revision identities are required.");
        }

        if (!LowerHexSha256.IsMatch(payload.DecisionHash ?? string.Empty))
        {
            return IngredientConsumptionValidationResult.Fail(
                "IngredientConsumption.DecisionHashInvalid",
                "Decision hash must be lowercase SHA-256 hex.");
        }

        if (payload.Lines is null || payload.Lines.Count == 0)
        {
            return IngredientConsumptionValidationResult.Fail(
                "IngredientConsumption.LinesRequired",
                "Committed ingredient consumption requires at least one line.");
        }

        string expectedDecisionHash = IngredientConsumptionDecisionHashV1.Compute(
            payload.SaleId,
            payload.SourceSaleOperationId,
            payload.RecipeBookRevisionId,
            IngredientConsumptionDecisionHashV1.AppliedResult,
            payload.Lines);
        if (!string.Equals(expectedDecisionHash, payload.DecisionHash, StringComparison.Ordinal))
        {
            return IngredientConsumptionValidationResult.Fail(
                "IngredientConsumption.DecisionHashMismatch",
                "Ingredient consumption decision hash does not match immutable line evidence.");
        }

        HashSet<string> lineIds = new(StringComparer.Ordinal);
        foreach (IngredientConsumptionLineV1 line in payload.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.LineId) ||
                line.SaleItemId == Guid.Empty ||
                line.IngredientId == Guid.Empty ||
                string.IsNullOrWhiteSpace(line.RecipeId) ||
                string.IsNullOrWhiteSpace(line.RecipeRevisionId) ||
                string.IsNullOrWhiteSpace(line.SourceBindingId) ||
                line.QuantityConsumedX1000 <= 0)
            {
                return IngredientConsumptionValidationResult.Fail(
                    "IngredientConsumption.LineInvalid",
                    "Consumption line identities and positive quantity are required.");
            }

            if (!string.Equals(line.SourceBindingType, "Item", StringComparison.Ordinal) &&
                !string.Equals(line.SourceBindingType, "Variant", StringComparison.Ordinal) &&
                !string.Equals(line.SourceBindingType, "ModifierOption", StringComparison.Ordinal))
            {
                return IngredientConsumptionValidationResult.Fail(
                    "IngredientConsumption.BindingTypeInvalid",
                    "Consumption line binding type is invalid.");
            }

            if (!string.Equals(line.StockUnit, "Gram", StringComparison.Ordinal) &&
                !string.Equals(line.StockUnit, "Milliliter", StringComparison.Ordinal) &&
                !string.Equals(line.StockUnit, "Piece", StringComparison.Ordinal))
            {
                return IngredientConsumptionValidationResult.Fail(
                    "IngredientConsumption.StockUnitInvalid",
                    "Consumption stock unit must be Gram, Milliliter, or Piece.");
            }

            if (!lineIds.Add(line.LineId))
            {
                return IngredientConsumptionValidationResult.Fail(
                    "IngredientConsumption.DuplicateLine",
                    "Consumption line identities must be unique.");
            }
        }

        return IngredientConsumptionValidationResult.Success();
    }
}

public sealed record IngredientConsumptionCanonicalParseResult(
    bool IsValid,
    IngredientConsumptionCommittedV1? Payload,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static IngredientConsumptionCanonicalParseResult Success(
        IngredientConsumptionCommittedV1 payload) => new(true, payload, null, null);
    public static IngredientConsumptionCanonicalParseResult Fail(
        string code, string message) => new(false, null, code, message);
}

using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Infrastructure.Data.Repositories.Orders;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Repositories.Ingredients;

public static class SqliteOrderIngredientConsumptionParticipant
{
    public static Result<SaleIngredientConsumptionDecision> ApplyOrderCompletion(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid orderId,
        Guid saleId,
        Guid sourceSaleOperationId,
        Guid actorUserId,
        DateTimeOffset occurredAtUtc,
        IReadOnlyDictionary<Guid, Guid> saleItemIdByOrderLineId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(saleItemIdByOrderLineId);

        if (orderId == Guid.Empty ||
            saleId == Guid.Empty ||
            sourceSaleOperationId == Guid.Empty ||
            actorUserId == Guid.Empty)
        {
            return Result<SaleIngredientConsumptionDecision>.Fail(
                Error.Validation(
                    "CafeOrder.IngredientIdentityRequired",
                    "Order/sale/source operation/actor identities are required."));
        }

        try
        {
            SaleIngredientConsumptionDecision? existing =
                SqliteIngredientConsumptionParticipant.ReadDecision(
                    connection,
                    transaction,
                    saleId);
            if (existing is not null)
            {
                return existing.SourceSaleOperationId == sourceSaleOperationId
                    ? Result<SaleIngredientConsumptionDecision>.Success(existing)
                    : Result<SaleIngredientConsumptionDecision>.Fail(
                        Error.Conflict(
                            "CafeOrder.IngredientDecisionConflict",
                            "Sale already has ingredient-consumption evidence from different source authority."));
            }

            SqliteCafeOrderStore.OrderRow order =
                SqliteCafeOrderStore.ReadOrder(connection, transaction, orderId)
                ?? throw new InvalidOperationException("Order was not found.");

            Guid revisionId = order.SubmittedRevisionId
                ?? throw new OrderIngredientValidationException(
                    "CafeOrder.SubmittedRevisionMissing",
                    "Order completion requires the frozen submitted revision.");
            CafeOrderRevisionSnapshot revision =
                SqliteCafeOrderStore.ReadRevision(
                    connection,
                    transaction,
                    revisionId);

            if (saleItemIdByOrderLineId.Count != revision.Lines.Count ||
                revision.Lines.Any(x => !saleItemIdByOrderLineId.ContainsKey(x.OrderLineId)))
            {
                return Result<SaleIngredientConsumptionDecision>.Fail(
                    Error.Conflict(
                        "CafeOrder.SaleItemCorrelationIncomplete",
                        "Every submitted OrderLine must correlate to exactly one SaleItem."));
            }

            string? recipeBookRevisionId = order.SubmittedRecipeBookRevisionId;
            if (string.IsNullOrWhiteSpace(recipeBookRevisionId))
            {
                return InsertNoRecipeDecision(
                    connection,
                    transaction,
                    saleId,
                    sourceSaleOperationId,
                    occurredAtUtc);
            }

            List<SaleIngredientConsumptionLineEvidence> evidence = new();

            foreach (CafeOrderLineSnapshot orderLine in revision.Lines)
            {
                Guid saleItemId = saleItemIdByOrderLineId[orderLine.OrderLineId];

                RecipeBinding? baseBinding = null;
                if (!string.IsNullOrWhiteSpace(orderLine.VariantId))
                {
                    baseBinding = ReadBinding(
                        connection,
                        transaction,
                        recipeBookRevisionId,
                        RecipeBindingTypes.Variant,
                        orderLine.VariantId!);
                }

                baseBinding ??= ReadBinding(
                    connection,
                    transaction,
                    recipeBookRevisionId,
                    RecipeBindingTypes.Item,
                    orderLine.ProductId.ToString("D"));

                if (baseBinding is not null)
                {
                    AddBindingLines(
                        connection,
                        transaction,
                        evidence,
                        recipeBookRevisionId,
                        saleId,
                        saleItemId,
                        orderLine.QuantityX1000,
                        baseBinding);
                }

                foreach (CafeOrderModifierSelectionSnapshot modifier in orderLine.ModifierSelections)
                {
                    RecipeBinding? modifierBinding = ReadBinding(
                        connection,
                        transaction,
                        recipeBookRevisionId,
                        RecipeBindingTypes.ModifierOption,
                        modifier.OptionId);
                    if (modifierBinding is null)
                    {
                        continue;
                    }

                    AddBindingLines(
                        connection,
                        transaction,
                        evidence,
                        recipeBookRevisionId,
                        saleId,
                        saleItemId,
                        orderLine.QuantityX1000,
                        modifierBinding);
                }
            }

            if (evidence.Count == 0)
            {
                return InsertNoRecipeDecision(
                    connection,
                    transaction,
                    saleId,
                    sourceSaleOperationId,
                    occurredAtUtc);
            }

            IngredientConsumptionLineV1[] lines = evidence
                .Select(x => new IngredientConsumptionLineV1(
                    x.Id,
                    x.SaleItemId,
                    x.IngredientId,
                    x.RecipeId,
                    x.RecipeRevisionId,
                    x.SourceBindingType,
                    x.SourceBindingId,
                    x.QuantityConsumedX1000,
                    x.StockUnit))
                .OrderBy(x => x.SaleItemId)
                .ThenBy(x => x.IngredientId)
                .ThenBy(x => x.LineId, StringComparer.Ordinal)
                .ToArray();

            string decisionHash = IngredientConsumptionDecisionHashV1.Compute(
                saleId,
                sourceSaleOperationId,
                recipeBookRevisionId,
                IngredientConsumptionDecisionHashV1.AppliedResult,
                lines);

            IngredientConsumptionCommittedV1 payload = new(
                sourceSaleOperationId,
                saleId,
                recipeBookRevisionId,
                decisionHash,
                actorUserId,
                occurredAtUtc.ToUniversalTime(),
                lines);

            return SqliteIngredientConsumptionParticipant.ApplyCanonical(
                connection,
                transaction,
                payload);
        }
        catch (OrderIngredientValidationException ex)
        {
            return Result<SaleIngredientConsumptionDecision>.Fail(
                Error.Conflict(ex.Code, ex.Message));
        }
        catch (Exception ex) when (
            ex is SqliteException or InvalidOperationException or FormatException or OverflowException)
        {
            return Result<SaleIngredientConsumptionDecision>.Fail(
                Error.Unexpected(
                    "CafeOrder.IngredientConsumptionFailed",
                    ex.Message));
        }
    }

    private static void AddBindingLines(
        SqliteConnection connection,
        SqliteTransaction transaction,
        List<SaleIngredientConsumptionLineEvidence> output,
        string recipeBookRevisionId,
        Guid saleId,
        Guid saleItemId,
        long soldQuantityX1000,
        RecipeBinding binding)
    {
        foreach (PublishedRecipeLine line in ReadRecipeLines(
            connection,
            transaction,
            recipeBookRevisionId,
            binding.RecipeId,
            binding.RecipeRevisionId))
        {
            Result<long> quantity = IngredientQuantityMath.ScaleForSoldQuantity(
                line.NormalizedQuantityX1000,
                soldQuantityX1000,
                binding.YieldX1000);
            if (quantity.IsFailure)
            {
                throw new OrderIngredientValidationException(
                    quantity.ErrorDetail.Code,
                    quantity.ErrorDetail.Message);
            }

            string lineId = DeterministicLineId(
                saleId,
                saleItemId,
                binding.BindingType,
                binding.BindingId,
                line.IngredientId,
                binding.RecipeRevisionId);

            output.Add(new SaleIngredientConsumptionLineEvidence(
                lineId,
                saleId,
                saleItemId,
                line.IngredientId,
                binding.RecipeId,
                binding.RecipeRevisionId,
                binding.BindingType,
                binding.BindingId,
                quantity.Value,
                line.StockUnit));
        }
    }

    private static RecipeBinding? ReadBinding(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string revisionId,
        string bindingType,
        string bindingId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT b.recipe_id,b.recipe_revision_id,r.yield_x1000
            FROM recipe_book_revision_bindings b
            INNER JOIN recipe_book_revision_recipes r
              ON r.revision_id=b.revision_id
             AND r.recipe_id=b.recipe_id
             AND r.recipe_revision_id=b.recipe_revision_id
            WHERE b.revision_id=$revision
              AND b.binding_type=$type
              AND b.binding_id=$id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$revision", revisionId);
        command.Parameters.AddWithValue("$type", bindingType);
        command.Parameters.AddWithValue("$id", bindingId);
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new RecipeBinding(
                bindingType,
                bindingId,
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2))
            : null;
    }

    private static IReadOnlyList<PublishedRecipeLine> ReadRecipeLines(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string revisionId,
        string recipeId,
        string recipeRevisionId)
    {
        List<PublishedRecipeLine> result = new();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT ingredient_id,stock_unit,normalized_quantity_x1000
            FROM recipe_book_revision_lines
            WHERE revision_id=$revision
              AND recipe_id=$recipe
              AND recipe_revision_id=$recipeRevision
            ORDER BY display_order,ingredient_id;
            """;
        command.Parameters.AddWithValue("$revision", revisionId);
        command.Parameters.AddWithValue("$recipe", recipeId);
        command.Parameters.AddWithValue("$recipeRevision", recipeRevisionId);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new PublishedRecipeLine(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetInt64(2)));
        }
        return result;
    }

    private static Result<SaleIngredientConsumptionDecision> InsertNoRecipeDecision(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid saleId,
        Guid sourceSaleOperationId,
        DateTimeOffset occurredAtUtc)
    {
        string hash = IngredientConsumptionDecisionHashV1.Compute(
            saleId,
            sourceSaleOperationId,
            null,
            IngredientConsumptionDecisionHashV1.NoRecipeAppliedResult,
            Array.Empty<IngredientConsumptionLineV1>());

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO sale_ingredient_consumption_decisions(
                sale_id,source_sale_operation_id,recipe_book_revision_id,
                decision_hash,result,line_count,created_at_utc)
            VALUES($sale,$source,NULL,$hash,'NO_RECIPE_APPLIED',0,$at);
            """;
        command.Parameters.AddWithValue("$sale", saleId.ToString("D"));
        command.Parameters.AddWithValue("$source", sourceSaleOperationId.ToString("D"));
        command.Parameters.AddWithValue("$hash", hash);
        command.Parameters.AddWithValue("$at", occurredAtUtc.ToUniversalTime().ToString("O"));
        command.ExecuteNonQuery();

        return Result<SaleIngredientConsumptionDecision>.Success(
            new SaleIngredientConsumptionDecision(
                saleId,
                sourceSaleOperationId,
                null,
                hash,
                IngredientConsumptionDecisionHashV1.NoRecipeAppliedResult,
                0,
                occurredAtUtc.ToUniversalTime(),
                Array.Empty<SaleIngredientConsumptionLineEvidence>()));
    }

    private static string DeterministicLineId(
        Guid saleId,
        Guid saleItemId,
        string bindingType,
        string bindingId,
        Guid ingredientId,
        string recipeRevisionId)
    {
        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                "salryn-c4-order-consumption-line-v1|" +
                saleId.ToString("D") + "|" +
                saleItemId.ToString("D") + "|" +
                bindingType + "|" +
                bindingId + "|" +
                ingredientId.ToString("D") + "|" +
                recipeRevisionId));
        byte[] bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes).ToString("D");
    }

    private sealed record RecipeBinding(
        string BindingType,
        string BindingId,
        string RecipeId,
        string RecipeRevisionId,
        long YieldX1000);

    private sealed record PublishedRecipeLine(
        Guid IngredientId,
        string StockUnit,
        long NormalizedQuantityX1000);

    private sealed class OrderIngredientValidationException : Exception
    {
        public OrderIngredientValidationException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }
}

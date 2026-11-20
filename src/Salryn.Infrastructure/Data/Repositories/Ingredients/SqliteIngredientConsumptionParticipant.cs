using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Repositories.Ingredients;

public static class SqliteIngredientConsumptionParticipant
{
    public static Result<SaleIngredientConsumptionDecision> ApplySale(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CompleteCheckoutSaleRequest request,
        Guid? sourceSaleOperationId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(request);

        SaleIngredientConsumptionDecision? existing =
            ReadDecision(connection, transaction, request.Sale.Id);
        if (existing is not null)
        {
            return existing.SourceSaleOperationId == sourceSaleOperationId
                ? Result<SaleIngredientConsumptionDecision>.Success(existing)
                : Result<SaleIngredientConsumptionDecision>.Fail(Error.Conflict(
                    "Ingredients.ConsumptionDecisionConflict",
                    "Sale already has ingredient-consumption evidence bound to different source authority."));
        }

        try
        {
            string? recipeBookRevisionId = ReadCurrentRecipeBookRevisionId(connection, transaction);
            List<SaleIngredientConsumptionLineEvidence> lines = new();

            if (recipeBookRevisionId is not null)
            {
                foreach (SaleItem saleItem in request.Items)
                {
                    ItemRecipeBinding? binding = ReadItemBinding(
                        connection, transaction, recipeBookRevisionId, saleItem.ProductId);
                    if (binding is null)
                    {
                        continue;
                    }

                    foreach (PublishedRecipeLine line in ReadRecipeLines(
                        connection,
                        transaction,
                        recipeBookRevisionId,
                        binding.RecipeId,
                        binding.RecipeRevisionId))
                    {
                        Result<long> scaled = IngredientQuantityMath.ScaleForSoldQuantity(
                            line.NormalizedQuantityX1000,
                            saleItem.Quantity.ValueX1000,
                            binding.YieldX1000);
                        if (scaled.IsFailure)
                        {
                            return Result<SaleIngredientConsumptionDecision>.Fail(scaled.ErrorDetail);
                        }

                        string lineId = DeterministicLineId(
                            request.Sale.Id,
                            saleItem.Id,
                            line.IngredientId,
                            binding.RecipeRevisionId);

                        lines.Add(new SaleIngredientConsumptionLineEvidence(
                            lineId,
                            request.Sale.Id,
                            saleItem.Id,
                            line.IngredientId,
                            binding.RecipeId,
                            binding.RecipeRevisionId,
                            RecipeBindingTypes.Item,
                            saleItem.ProductId.ToString("D"),
                            scaled.Value,
                            line.StockUnit));
                    }
                }
            }

            string result = lines.Count == 0
                ? IngredientConsumptionDecisionHashV1.NoRecipeAppliedResult
                : IngredientConsumptionDecisionHashV1.AppliedResult;
            string? boundRevision = lines.Count == 0 ? null : recipeBookRevisionId;
            string decisionHash = ComputeDecisionHash(
                request.Sale.Id, sourceSaleOperationId, boundRevision, result, lines);
            DateTimeOffset createdAt = DateTimeOffset.UtcNow;

            InsertDecision(
                connection,
                transaction,
                request.Sale.Id,
                sourceSaleOperationId,
                boundRevision,
                decisionHash,
                result,
                lines.Count,
                createdAt);

            foreach (SaleIngredientConsumptionLineEvidence line in lines)
            {
                Result applyLine = ApplyLine(
                    connection,
                    transaction,
                    line,
                    request.Sale.CashierUserId,
                    createdAt);
                if (applyLine.IsFailure)
                {
                    return Result<SaleIngredientConsumptionDecision>.Fail(applyLine.ErrorDetail);
                }
            }

            return Result<SaleIngredientConsumptionDecision>.Success(
                new SaleIngredientConsumptionDecision(
                    request.Sale.Id,
                    sourceSaleOperationId,
                    boundRevision,
                    decisionHash,
                    result,
                    lines.Count,
                    createdAt,
                    lines));
        }
        catch (Exception ex) when (
            ex is SqliteException or InvalidOperationException or OverflowException or FormatException)
        {
            return Result<SaleIngredientConsumptionDecision>.Fail(Error.Unexpected(
                "Ingredients.ConsumptionFailed", ex.Message));
        }
    }

    public static Result<SaleIngredientConsumptionDecision> ApplyCanonical(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IngredientConsumptionCommittedV1 payload)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(payload);

        IngredientConsumptionValidationResult validation =
            IngredientConsumptionCommittedV1CanonicalJson.Validate(payload);
        if (!validation.IsValid)
        {
            return Result<SaleIngredientConsumptionDecision>.Fail(Error.Validation(
                validation.ErrorCode ?? "IngredientConsumption.PayloadInvalid",
                validation.ErrorMessage ?? "Ingredient consumption payload is invalid."));
        }

        SaleIngredientConsumptionDecision? existing =
            ReadDecision(connection, transaction, payload.SaleId);
        if (existing is not null)
        {
            return CanonicalMatches(existing, payload)
                ? Result<SaleIngredientConsumptionDecision>.Success(existing)
                : Result<SaleIngredientConsumptionDecision>.Fail(Error.Conflict(
                    "IngredientConsumption.DecisionConflict",
                    "Existing ingredient-consumption decision differs from canonical semantic evidence."));
        }

        if (!Exists(connection, transaction, "sales", "id", payload.SaleId.ToString("D")))
        {
            return Result<SaleIngredientConsumptionDecision>.Fail(Error.Conflict(
                "IngredientConsumption.SaleMissing",
                "Ingredient consumption requires the matching committed sale first."));
        }

        DateTimeOffset createdAt = payload.OccurredAtUtc.ToUniversalTime();
        InsertDecision(
            connection,
            transaction,
            payload.SaleId,
            payload.SourceSaleOperationId,
            payload.RecipeBookRevisionId,
            payload.DecisionHash,
            IngredientConsumptionDecisionHashV1.AppliedResult,
            payload.Lines.Count,
            createdAt);

        List<SaleIngredientConsumptionLineEvidence> lines = new();
        foreach (IngredientConsumptionLineV1 canonicalLine in payload.Lines)
        {
            if (!SaleItemExists(
                    connection,
                    transaction,
                    payload.SaleId,
                    canonicalLine.SaleItemId))
            {
                return Result<SaleIngredientConsumptionDecision>.Fail(Error.Conflict(
                    "IngredientConsumption.SaleItemMissing",
                    "Canonical ingredient consumption references a missing sale item."));
            }

            var line = new SaleIngredientConsumptionLineEvidence(
                canonicalLine.LineId,
                payload.SaleId,
                canonicalLine.SaleItemId,
                canonicalLine.IngredientId,
                canonicalLine.RecipeId,
                canonicalLine.RecipeRevisionId,
                canonicalLine.SourceBindingType,
                canonicalLine.SourceBindingId,
                canonicalLine.QuantityConsumedX1000,
                canonicalLine.StockUnit);

            Result applyLine = ApplyLine(
                connection,
                transaction,
                line,
                payload.ActorUserId,
                createdAt);
            if (applyLine.IsFailure)
            {
                return Result<SaleIngredientConsumptionDecision>.Fail(applyLine.ErrorDetail);
            }

            lines.Add(line);
        }

        return Result<SaleIngredientConsumptionDecision>.Success(
            new SaleIngredientConsumptionDecision(
                payload.SaleId,
                payload.SourceSaleOperationId,
                payload.RecipeBookRevisionId,
                payload.DecisionHash,
                IngredientConsumptionDecisionHashV1.AppliedResult,
                lines.Count,
                createdAt,
                lines));
    }

    public static Result VerifyCanonical(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IngredientConsumptionCommittedV1 payload)
    {
        IngredientConsumptionValidationResult validation =
            IngredientConsumptionCommittedV1CanonicalJson.Validate(payload);
        if (!validation.IsValid)
        {
            return Result.Fail(Error.Validation(
                validation.ErrorCode ?? "IngredientConsumption.PayloadInvalid",
                validation.ErrorMessage ?? "Ingredient consumption payload is invalid."));
        }

        SaleIngredientConsumptionDecision? existing =
            ReadDecision(connection, transaction, payload.SaleId);
        return existing is not null && CanonicalMatches(existing, payload)
            ? Result.Success()
            : Result.Fail(Error.Conflict(
                "IngredientConsumption.OriginEvidenceMismatch",
                "Canonical ingredient-consumption echo conflicts with durable local evidence."));
    }

    public static SaleIngredientConsumptionDecision? ReadDecision(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid saleId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT source_sale_operation_id,recipe_book_revision_id,decision_hash,
                   result,line_count,created_at_utc
            FROM sale_ingredient_consumption_decisions
            WHERE sale_id=$sale LIMIT 1;
            """;
        command.Parameters.AddWithValue("$sale", saleId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        Guid? sourceOperation = reader.IsDBNull(0) ? null : Guid.Parse(reader.GetString(0));
        string? recipeRevision = reader.IsDBNull(1) ? null : reader.GetString(1);
        string hash = reader.GetString(2);
        string result = reader.GetString(3);
        int lineCount = reader.GetInt32(4);
        DateTimeOffset created = DateTimeOffset.Parse(reader.GetString(5));
        reader.Close();

        IReadOnlyList<SaleIngredientConsumptionLineEvidence> lines =
            ReadDecisionLines(connection, transaction, saleId);

        if (lines.Count != lineCount)
        {
            throw new InvalidOperationException(
                "Ingredient consumption decision line-count evidence is inconsistent.");
        }

        string expectedHash = ComputeDecisionHash(
            saleId, sourceOperation, recipeRevision, result, lines);
        if (!string.Equals(expectedHash, hash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Ingredient consumption decision hash no longer matches immutable evidence.");
        }

        return new SaleIngredientConsumptionDecision(
            saleId, sourceOperation, recipeRevision, hash, result, lineCount, created, lines);
    }

    public static Result ReverseSale(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid saleId,
        Guid actorUserId)
    {
        SaleIngredientConsumptionDecision? decision = ReadDecision(connection, transaction, saleId);
        if (decision is null)
        {
            // Historical pre-C2 sales legitimately have no C2 decision.
            return Result.Success();
        }

        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            foreach (SaleIngredientConsumptionLineEvidence line in decision.Lines)
            {
                using (SqliteCommand existing = connection.CreateCommand())
                {
                    existing.Transaction = transaction;
                    existing.CommandText = """
                        SELECT 1 FROM ingredient_stock_movements
                        WHERE original_consumption_line_id=$line
                          AND movement_kind='RecipeConsumptionReversal'
                        LIMIT 1;
                        """;
                    existing.Parameters.AddWithValue("$line", line.Id);
                    if (existing.ExecuteScalar() is not null)
                    {
                        continue;
                    }
                }

                using (SqliteCommand restore = connection.CreateCommand())
                {
                    restore.Transaction = transaction;
                    restore.CommandText = """
                        UPDATE ingredients
                        SET stock_on_hand_x1000=stock_on_hand_x1000+$quantity,
                            updated_at_utc=$at
                        WHERE id=$ingredient AND stock_unit=$unit;
                        """;
                    restore.Parameters.AddWithValue("$quantity", line.QuantityConsumedX1000);
                    restore.Parameters.AddWithValue("$at", now.ToString("O"));
                    restore.Parameters.AddWithValue("$ingredient", line.IngredientId.ToString("D"));
                    restore.Parameters.AddWithValue("$unit", line.StockUnit);
                    if (restore.ExecuteNonQuery() != 1)
                    {
                        return Result.Fail(Error.Conflict(
                            "Ingredients.ReversalIngredientMissing",
                            "Original ingredient identity required for reversal is missing."));
                    }
                }

                using SqliteCommand movement = connection.CreateCommand();
                movement.Transaction = transaction;
                movement.CommandText = """
                    INSERT INTO ingredient_stock_movements(
                        id,ingredient_id,sale_id,sale_item_id,consumption_line_id,
                        original_consumption_line_id,movement_kind,quantity_delta_x1000,
                        actor_user_id,created_at_utc)
                    VALUES($id,$ingredient,$sale,$item,NULL,$original,
                           'RecipeConsumptionReversal',$quantity,$actor,$at);
                    """;
                movement.Parameters.AddWithValue("$id", Guid.CreateVersion7().ToString("D"));
                movement.Parameters.AddWithValue("$ingredient", line.IngredientId.ToString("D"));
                movement.Parameters.AddWithValue("$sale", saleId.ToString("D"));
                movement.Parameters.AddWithValue("$item", line.SaleItemId.ToString("D"));
                movement.Parameters.AddWithValue("$original", line.Id);
                movement.Parameters.AddWithValue("$quantity", line.QuantityConsumedX1000);
                movement.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
                movement.Parameters.AddWithValue("$at", now.ToString("O"));
                movement.ExecuteNonQuery();
            }

            return Result.Success();
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or OverflowException)
        {
            return Result.Fail(Error.Unexpected("Ingredients.ReversalFailed", ex.Message));
        }
    }

    private static Result ApplyLine(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SaleIngredientConsumptionLineEvidence line,
        Guid actorUserId,
        DateTimeOffset at)
    {
        using (SqliteCommand decrement = connection.CreateCommand())
        {
            decrement.Transaction = transaction;
            decrement.CommandText = """
                UPDATE ingredients
                SET stock_on_hand_x1000=stock_on_hand_x1000-$quantity,
                    updated_at_utc=$at
                WHERE id=$ingredient
                  AND is_active=1
                  AND stock_unit=$unit
                  AND stock_on_hand_x1000 >= $quantity;
                """;
            decrement.Parameters.AddWithValue("$quantity", line.QuantityConsumedX1000);
            decrement.Parameters.AddWithValue("$at", at.ToUniversalTime().ToString("O"));
            decrement.Parameters.AddWithValue("$ingredient", line.IngredientId.ToString("D"));
            decrement.Parameters.AddWithValue("$unit", line.StockUnit);
            if (decrement.ExecuteNonQuery() != 1)
            {
                return Result.Fail(Error.Conflict(
                    "Ingredients.InsufficientStock",
                    "Ingredient stock is insufficient or ingredient configuration is unavailable."));
            }
        }

        using (SqliteCommand insertLine = connection.CreateCommand())
        {
            insertLine.Transaction = transaction;
            insertLine.CommandText = """
                INSERT INTO sale_ingredient_consumption_lines(
                    id,sale_id,sale_item_id,ingredient_id,recipe_id,recipe_revision_id,
                    source_binding_type,source_binding_id,quantity_consumed_x1000,stock_unit)
                VALUES($id,$sale,$item,$ingredient,$recipe,$recipeRevision,
                       $bindingType,$bindingId,$quantity,$unit);
                """;
            insertLine.Parameters.AddWithValue("$id", line.Id);
            insertLine.Parameters.AddWithValue("$sale", line.SaleId.ToString("D"));
            insertLine.Parameters.AddWithValue("$item", line.SaleItemId.ToString("D"));
            insertLine.Parameters.AddWithValue("$ingredient", line.IngredientId.ToString("D"));
            insertLine.Parameters.AddWithValue("$recipe", line.RecipeId);
            insertLine.Parameters.AddWithValue("$recipeRevision", line.RecipeRevisionId);
            insertLine.Parameters.AddWithValue("$bindingType", line.SourceBindingType);
            insertLine.Parameters.AddWithValue("$bindingId", line.SourceBindingId);
            insertLine.Parameters.AddWithValue("$quantity", line.QuantityConsumedX1000);
            insertLine.Parameters.AddWithValue("$unit", line.StockUnit);
            insertLine.ExecuteNonQuery();
        }

        using SqliteCommand movement = connection.CreateCommand();
        movement.Transaction = transaction;
        movement.CommandText = """
            INSERT INTO ingredient_stock_movements(
                id,ingredient_id,sale_id,sale_item_id,consumption_line_id,
                original_consumption_line_id,movement_kind,quantity_delta_x1000,
                actor_user_id,created_at_utc)
            VALUES($id,$ingredient,$sale,$item,$line,NULL,
                   'RecipeConsumption',$quantity,$actor,$at);
            """;
        movement.Parameters.AddWithValue("$id", Guid.CreateVersion7().ToString("D"));
        movement.Parameters.AddWithValue("$ingredient", line.IngredientId.ToString("D"));
        movement.Parameters.AddWithValue("$sale", line.SaleId.ToString("D"));
        movement.Parameters.AddWithValue("$item", line.SaleItemId.ToString("D"));
        movement.Parameters.AddWithValue("$line", line.Id);
        movement.Parameters.AddWithValue("$quantity", checked(-line.QuantityConsumedX1000));
        movement.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
        movement.Parameters.AddWithValue("$at", at.ToUniversalTime().ToString("O"));
        movement.ExecuteNonQuery();

        return Result.Success();
    }

    private static void InsertDecision(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid saleId,
        Guid? sourceSaleOperationId,
        string? recipeBookRevisionId,
        string decisionHash,
        string result,
        int lineCount,
        DateTimeOffset createdAt)
    {
        using SqliteCommand decision = connection.CreateCommand();
        decision.Transaction = transaction;
        decision.CommandText = """
            INSERT INTO sale_ingredient_consumption_decisions(
                sale_id,source_sale_operation_id,recipe_book_revision_id,
                decision_hash,result,line_count,created_at_utc)
            VALUES($sale,$source,$revision,$hash,$result,$count,$at);
            """;
        decision.Parameters.AddWithValue("$sale", saleId.ToString("D"));
        decision.Parameters.AddWithValue(
            "$source",
            sourceSaleOperationId.HasValue
                ? sourceSaleOperationId.Value.ToString("D")
                : DBNull.Value);
        decision.Parameters.AddWithValue("$revision", recipeBookRevisionId ?? (object)DBNull.Value);
        decision.Parameters.AddWithValue("$hash", decisionHash);
        decision.Parameters.AddWithValue("$result", result);
        decision.Parameters.AddWithValue("$count", lineCount);
        decision.Parameters.AddWithValue("$at", createdAt.ToUniversalTime().ToString("O"));
        decision.ExecuteNonQuery();
    }

    private static bool CanonicalMatches(
        SaleIngredientConsumptionDecision decision,
        IngredientConsumptionCommittedV1 payload)
    {
        if (decision.SourceSaleOperationId != payload.SourceSaleOperationId ||
            !string.Equals(decision.RecipeBookRevisionId, payload.RecipeBookRevisionId, StringComparison.Ordinal) ||
            !string.Equals(decision.DecisionHash, payload.DecisionHash, StringComparison.Ordinal) ||
            !string.Equals(decision.Result, IngredientConsumptionDecisionHashV1.AppliedResult, StringComparison.Ordinal) ||
            decision.Lines.Count != payload.Lines.Count)
        {
            return false;
        }

        Dictionary<string, SaleIngredientConsumptionLineEvidence> local =
            decision.Lines.ToDictionary(x => x.Id, StringComparer.Ordinal);
        foreach (IngredientConsumptionLineV1 line in payload.Lines)
        {
            if (!local.TryGetValue(line.LineId, out SaleIngredientConsumptionLineEvidence? x) ||
                x.SaleItemId != line.SaleItemId ||
                x.IngredientId != line.IngredientId ||
                !string.Equals(x.RecipeId, line.RecipeId, StringComparison.Ordinal) ||
                !string.Equals(x.RecipeRevisionId, line.RecipeRevisionId, StringComparison.Ordinal) ||
                !string.Equals(x.SourceBindingType, line.SourceBindingType, StringComparison.Ordinal) ||
                !string.Equals(x.SourceBindingId, line.SourceBindingId, StringComparison.Ordinal) ||
                x.QuantityConsumedX1000 != line.QuantityConsumedX1000 ||
                !string.Equals(x.StockUnit, line.StockUnit, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string? ReadCurrentRecipeBookRevisionId(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT revision_id FROM recipe_book_current_publication LIMIT 1;";
        return command.ExecuteScalar() as string;
    }

    private static ItemRecipeBinding? ReadItemBinding(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string revisionId,
        Guid productId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT b.recipe_id,b.recipe_revision_id,r.yield_x1000
            FROM recipe_book_revision_bindings b
            INNER JOIN recipe_book_revision_recipes r
              ON r.revision_id=b.revision_id AND r.recipe_id=b.recipe_id
            WHERE b.revision_id=$revision
              AND b.binding_type='Item'
              AND b.binding_id=$product
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$revision", revisionId);
        command.Parameters.AddWithValue("$product", productId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new ItemRecipeBinding(reader.GetString(0), reader.GetString(1), reader.GetInt64(2))
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
                Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetInt64(2)));
        }
        return result;
    }

    private static IReadOnlyList<SaleIngredientConsumptionLineEvidence> ReadDecisionLines(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid saleId)
    {
        List<SaleIngredientConsumptionLineEvidence> result = new();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id,sale_item_id,ingredient_id,recipe_id,recipe_revision_id,
                   source_binding_type,source_binding_id,quantity_consumed_x1000,stock_unit
            FROM sale_ingredient_consumption_lines
            WHERE sale_id=$sale
            ORDER BY sale_item_id,ingredient_id,id;
            """;
        command.Parameters.AddWithValue("$sale", saleId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new SaleIngredientConsumptionLineEvidence(
                reader.GetString(0),
                saleId,
                Guid.Parse(reader.GetString(1)),
                Guid.Parse(reader.GetString(2)),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetInt64(7),
                reader.GetString(8)));
        }
        return result;
    }

    private static string ComputeDecisionHash(
        Guid saleId,
        Guid? sourceSaleOperationId,
        string? recipeBookRevisionId,
        string result,
        IReadOnlyList<SaleIngredientConsumptionLineEvidence> lines) =>
        IngredientConsumptionDecisionHashV1.Compute(
            saleId,
            sourceSaleOperationId,
            recipeBookRevisionId,
            result,
            lines.Select(line => new IngredientConsumptionLineV1(
                line.Id,
                line.SaleItemId,
                line.IngredientId,
                line.RecipeId,
                line.RecipeRevisionId,
                line.SourceBindingType,
                line.SourceBindingId,
                line.QuantityConsumedX1000,
                line.StockUnit)));

    private static string DeterministicLineId(
        Guid saleId,
        Guid saleItemId,
        Guid ingredientId,
        string recipeRevisionId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            "salryn-c2-consumption-line-v1|" + saleId.ToString("D") + "|" +
            saleItemId.ToString("D") + "|" + ingredientId.ToString("D") + "|" +
            recipeRevisionId));
        byte[] bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes).ToString("D");
    }

    private static bool Exists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string column,
        string id)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM " + table + " WHERE " + column + "=$id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteScalar() is not null;
    }

    private static bool SaleItemExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid saleId,
        Guid saleItemId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1 FROM sale_items
            WHERE id=$item AND sale_id=$sale
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$item", saleItemId.ToString("D"));
        command.Parameters.AddWithValue("$sale", saleId.ToString("D"));
        return command.ExecuteScalar() is not null;
    }

    private sealed record ItemRecipeBinding(
        string RecipeId,
        string RecipeRevisionId,
        long YieldX1000);

    private sealed record PublishedRecipeLine(
        Guid IngredientId,
        string StockUnit,
        long NormalizedQuantityX1000);
}

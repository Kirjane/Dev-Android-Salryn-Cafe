using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public sealed record CafeOrderBootstrapApplyResult(
    bool IsReplay,
    Guid SnapshotId,
    string SnapshotHash,
    long BaselineServerSequence,
    int OrderCount);

public sealed class SqliteCafeOrderBootstrapStore
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteCafeOrderBootstrapStore(
        SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory =
            connectionFactory ??
            throw new ArgumentNullException(nameof(connectionFactory));
    }

    public Result<CafeOrderBootstrapApplyResult> Apply(
        CafeOrderBootstrapV1 snapshot)
    {
        if (!CafeOrderBootstrapProtocolV1.Validate(snapshot))
        {
            return Result<CafeOrderBootstrapApplyResult>.Fail(
                Error.Conflict(
                    "CafeOrderBootstrap.SnapshotIntegrityMismatch",
                    "Cafe order bootstrap identity/hash does not match canonical content."));
        }

        try
        {
            using SqliteConnection connection =
                _connectionFactory.CreateConnection();
            connection.Open();
            SqlitePragma.Apply(connection);
            using SqliteTransaction transaction =
                connection.BeginTransaction();

            Guid localStore =
                ReadStoreId(connection, transaction);
            if (localStore != snapshot.StoreId)
            {
                transaction.Rollback();
                return Result<CafeOrderBootstrapApplyResult>.Fail(
                    Error.Conflict(
                        "CafeOrderBootstrap.StoreMismatch",
                        "Cafe order bootstrap StoreId does not match local deployment authority."));
            }

            string? authorityError =
                ValidateInheritedBootstrapAuthority(
                    connection,
                    transaction,
                    snapshot);
            if (authorityError is not null)
            {
                transaction.Rollback();
                return Result<CafeOrderBootstrapApplyResult>.Fail(
                    Error.Conflict(
                        "CafeOrderBootstrap.AuthorityConflict",
                        authorityError));
            }

            ExistingState? existing =
                ReadState(
                    connection,
                    transaction,
                    snapshot.StoreId);
            if (existing is not null)
            {
                bool exact =
                    existing.CentralInstanceId ==
                        snapshot.CentralInstanceId &&
                    existing.BaselineServerSequence ==
                        snapshot.BaselineServerSequence &&
                    existing.SnapshotId == snapshot.SnapshotId &&
                    string.Equals(
                        existing.SnapshotHash,
                        snapshot.SnapshotHash,
                        StringComparison.Ordinal);

                transaction.Commit();
                return exact
                    ? Result<CafeOrderBootstrapApplyResult>.Success(
                        new CafeOrderBootstrapApplyResult(
                            true,
                            snapshot.SnapshotId,
                            snapshot.SnapshotHash,
                            snapshot.BaselineServerSequence,
                            snapshot.Orders.Count))
                    : Result<CafeOrderBootstrapApplyResult>.Fail(
                        Error.Conflict(
                            "CafeOrderBootstrap.EpochConflict",
                            "Different C4 bootstrap evidence already exists; explicit reconciliation/rebootstrap is required."));
            }

            if (ScalarLong(
                    connection,
                    transaction,
                    "SELECT COUNT(*) FROM cafe_orders;") != 0)
            {
                transaction.Rollback();
                return Result<CafeOrderBootstrapApplyResult>.Fail(
                    Error.Conflict(
                        "CafeOrderBootstrap.NonEmptyDomainConflict",
                        "C4 bootstrap requires an empty local order domain."));
            }

            foreach (CafeOrderBootstrapOrderV1 order in snapshot.Orders)
            {
                string? prerequisiteError =
                    ValidateOrderPrerequisites(
                        connection,
                        transaction,
                        snapshot.StoreId,
                        order);
                if (prerequisiteError is not null)
                {
                    transaction.Rollback();
                    return Result<CafeOrderBootstrapApplyResult>.Fail(
                        Error.Conflict(
                            "CafeOrderBootstrap.PrerequisiteMissing",
                            prerequisiteError));
                }

                InsertOrder(
                    connection,
                    transaction,
                    snapshot.StoreId,
                    order);
                InsertCurrentRevision(
                    connection,
                    transaction,
                    order);
            }

            using (SqliteCommand state =
                connection.CreateCommand())
            {
                state.Transaction = transaction;
                state.CommandText = """
                    INSERT INTO cafe_order_bootstrap_state(
                        store_id,central_instance_id,
                        baseline_server_sequence,
                        snapshot_id,snapshot_hash,applied_at_utc)
                    VALUES(
                        $store,$instance,$baseline,
                        $snapshot,$hash,$at);
                    """;
                state.Parameters.AddWithValue(
                    "$store",
                    snapshot.StoreId.ToString("D"));
                state.Parameters.AddWithValue(
                    "$instance",
                    snapshot.CentralInstanceId.ToString("D"));
                state.Parameters.AddWithValue(
                    "$baseline",
                    snapshot.BaselineServerSequence);
                state.Parameters.AddWithValue(
                    "$snapshot",
                    snapshot.SnapshotId.ToString("D"));
                state.Parameters.AddWithValue(
                    "$hash",
                    snapshot.SnapshotHash);
                state.Parameters.AddWithValue(
                    "$at",
                    DateTimeOffset.UtcNow.ToString("O"));
                state.ExecuteNonQuery();
            }

            transaction.Commit();
            return Result<CafeOrderBootstrapApplyResult>.Success(
                new CafeOrderBootstrapApplyResult(
                    false,
                    snapshot.SnapshotId,
                    snapshot.SnapshotHash,
                    snapshot.BaselineServerSequence,
                    snapshot.Orders.Count));
        }
        catch (Exception ex) when (
            ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Result<CafeOrderBootstrapApplyResult>.Fail(
                Error.Unexpected(
                    "CafeOrderBootstrap.ApplyFailed",
                    ex.Message));
        }
    }

    private static string? ValidateInheritedBootstrapAuthority(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeOrderBootstrapV1 snapshot)
    {
        SyncRemoteState? f1eState =
            SqliteSyncPullStore.ReadRemoteState(
                connection,
                transaction,
                snapshot.StoreId);
        SyncBootstrapEvidence? f1eEvidence =
            SqliteSyncBootstrapStore.ReadBootstrapEvidence(
                connection,
                transaction,
                snapshot.StoreId);

        if (f1eState is null ||
            !f1eState.CentralInstanceId.HasValue ||
            f1eEvidence is null)
        {
            return "C4 bootstrap requires accepted F1E bootstrap authority first.";
        }

        if (f1eState.CentralInstanceId.Value !=
                snapshot.CentralInstanceId ||
            f1eEvidence.CentralInstanceId !=
                snapshot.CentralInstanceId ||
            f1eEvidence.StoreId != snapshot.StoreId ||
            f1eEvidence.BaselineServerSequence !=
                snapshot.BaselineServerSequence ||
            f1eState.LastReceivedServerSequence !=
                snapshot.BaselineServerSequence ||
            f1eState.LastAppliedServerSequence !=
                snapshot.BaselineServerSequence ||
            !string.Equals(
                f1eState.State,
                AppliedBusinessChangeContract.Ready,
                StringComparison.Ordinal))
        {
            return "C4 bootstrap must exactly extend accepted F1E StoreId, epoch and baseline authority.";
        }

        AuthorityRow? c2 =
            ReadAuthority(
                connection,
                transaction,
                "cafe_ingredient_bootstrap_state",
                snapshot.StoreId);
        AuthorityRow? c3 =
            ReadAuthority(
                connection,
                transaction,
                "cafe_inventory_bootstrap_state",
                snapshot.StoreId);

        if (c2 is null || c3 is null)
        {
            return "C4 bootstrap requires accepted C2 and C3 bootstrap authority first.";
        }

        if (c2.CentralInstanceId != snapshot.CentralInstanceId ||
            c3.CentralInstanceId != snapshot.CentralInstanceId ||
            c2.BaselineServerSequence != snapshot.BaselineServerSequence ||
            c3.BaselineServerSequence != snapshot.BaselineServerSequence)
        {
            return "C4 bootstrap epoch/baseline conflicts with accepted C2/C3 authority.";
        }

        return null;
    }

    private static string? ValidateOrderPrerequisites(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId,
        CafeOrderBootstrapOrderV1 order)
    {
        if (!Exists(
                connection,
                transaction,
                "menu_revisions",
                "revision_id",
                order.MenuPublicationRevisionId,
                "store_id",
                storeId.ToString("D")))
        {
            return "Referenced MenuPublication is missing for Order " +
                order.OrderId.ToString("D") + ".";
        }

        if (!string.IsNullOrWhiteSpace(
                order.SubmittedRecipeBookRevisionId) &&
            !Exists(
                connection,
                transaction,
                "recipe_book_revisions",
                "revision_id",
                order.SubmittedRecipeBookRevisionId!,
                "store_id",
                storeId.ToString("D")))
        {
            return "Referenced RecipeBook publication is missing for Order " +
                order.OrderId.ToString("D") + ".";
        }

        if (!UserExists(
                connection,
                transaction,
                order.CreatedByUserId) ||
            !UserExists(
                connection,
                transaction,
                order.RevisionActorUserId))
        {
            return "Order bootstrap actor prerequisite is missing for Order " +
                order.OrderId.ToString("D") + ".";
        }

        if (order.State is "Submitted" or "Preparing" or "Ready")
        {
            if (!order.SubmittedRevisionId.HasValue ||
                order.SubmittedRevisionId.Value !=
                    order.CurrentRevisionId)
            {
                return "Submitted/preparing/ready order must bind its current frozen revision.";
            }
        }

        return null;
    }

    private static void InsertOrder(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId,
        CafeOrderBootstrapOrderV1 order)
    {
        using SqliteCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cafe_orders(
                order_id,store_id,current_version,
                current_revision_id,menu_publication_revision_id,
                state,service_mode,service_reference,
                created_by_user_id,created_at_utc,updated_at_utc,
                submitted_revision_id,
                submitted_recipe_book_revision_id,
                completed_sale_id,cancellation_reason)
            VALUES(
                $order,$store,$version,
                $revision,$menu,
                $state,$mode,$reference,
                $actor,$created,$updated,
                $submitted,$recipe,
                NULL,NULL);
            """;
        command.Parameters.AddWithValue(
            "$order",
            order.OrderId.ToString("D"));
        command.Parameters.AddWithValue(
            "$store",
            storeId.ToString("D"));
        command.Parameters.AddWithValue(
            "$version",
            order.CurrentVersion);
        command.Parameters.AddWithValue(
            "$revision",
            order.CurrentRevisionId.ToString("D"));
        command.Parameters.AddWithValue(
            "$menu",
            order.MenuPublicationRevisionId);
        command.Parameters.AddWithValue(
            "$state",
            order.State);
        command.Parameters.AddWithValue(
            "$mode",
            order.ServiceMode);
        command.Parameters.AddWithValue(
            "$reference",
            DbText(order.ServiceReference));
        command.Parameters.AddWithValue(
            "$actor",
            order.CreatedByUserId.ToString("D"));
        command.Parameters.AddWithValue(
            "$created",
            order.CreatedAtUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue(
            "$updated",
            order.UpdatedAtUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue(
            "$submitted",
            DbGuid(order.SubmittedRevisionId));
        command.Parameters.AddWithValue(
            "$recipe",
            DbText(order.SubmittedRecipeBookRevisionId));
        command.ExecuteNonQuery();
    }

    private static void InsertCurrentRevision(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeOrderBootstrapOrderV1 order)
    {
        using (SqliteCommand revision =
            connection.CreateCommand())
        {
            revision.Transaction = transaction;
            revision.CommandText = """
                INSERT INTO cafe_order_revisions(
                    revision_id,order_id,revision_number,
                    order_version,previous_revision_id,
                    menu_publication_revision_id,
                    service_mode,service_reference,
                    content_hash,actor_user_id,created_at_utc)
                VALUES(
                    $revision,$order,$number,
                    $orderVersion,NULL,
                    $menu,$mode,$reference,
                    $hash,$actor,$created);
                """;
            revision.Parameters.AddWithValue(
                "$revision",
                order.CurrentRevisionId.ToString("D"));
            revision.Parameters.AddWithValue(
                "$order",
                order.OrderId.ToString("D"));
            revision.Parameters.AddWithValue(
                "$number",
                order.RevisionNumber);
            revision.Parameters.AddWithValue(
                "$orderVersion",
                order.RevisionOrderVersion);
            revision.Parameters.AddWithValue(
                "$menu",
                order.MenuPublicationRevisionId);
            revision.Parameters.AddWithValue(
                "$mode",
                order.ServiceMode);
            revision.Parameters.AddWithValue(
                "$reference",
                DbText(order.ServiceReference));
            revision.Parameters.AddWithValue(
                "$hash",
                order.RevisionContentHash);
            revision.Parameters.AddWithValue(
                "$actor",
                order.RevisionActorUserId.ToString("D"));
            revision.Parameters.AddWithValue(
                "$created",
                order.RevisionCreatedAtUtc.ToUniversalTime().ToString("O"));
            revision.ExecuteNonQuery();
        }

        foreach (CafeOrderBootstrapLineV1 line in order.Lines)
        {
            using (SqliteCommand item =
                connection.CreateCommand())
            {
                item.Transaction = transaction;
                item.CommandText = """
                    INSERT INTO cafe_order_revision_lines(
                        revision_id,order_line_id,product_id,
                        product_name_snapshot,unit_name_snapshot,
                        quantity_x1000,base_unit_price_cents,
                        variant_id,variant_name_snapshot,
                        variant_unit_price_cents,note,
                        calculated_unit_price_cents,
                        line_total_cents,display_order)
                    VALUES(
                        $revision,$line,$product,
                        $name,$unit,
                        $quantity,$basePrice,
                        $variant,$variantName,
                        $variantPrice,$note,
                        $unitPrice,$total,$display);
                    """;
                item.Parameters.AddWithValue(
                    "$revision",
                    order.CurrentRevisionId.ToString("D"));
                item.Parameters.AddWithValue(
                    "$line",
                    line.OrderLineId.ToString("D"));
                item.Parameters.AddWithValue(
                    "$product",
                    line.ProductId.ToString("D"));
                item.Parameters.AddWithValue(
                    "$name",
                    line.ProductNameSnapshot);
                item.Parameters.AddWithValue(
                    "$unit",
                    line.UnitNameSnapshot);
                item.Parameters.AddWithValue(
                    "$quantity",
                    line.QuantityX1000);
                item.Parameters.AddWithValue(
                    "$basePrice",
                    line.BaseUnitPriceCents);
                item.Parameters.AddWithValue(
                    "$variant",
                    DbText(line.VariantId));
                item.Parameters.AddWithValue(
                    "$variantName",
                    DbText(line.VariantNameSnapshot));
                item.Parameters.AddWithValue(
                    "$variantPrice",
                    line.VariantUnitPriceCents ??
                        (object)DBNull.Value);
                item.Parameters.AddWithValue(
                    "$note",
                    DbText(line.Note));
                item.Parameters.AddWithValue(
                    "$unitPrice",
                    line.CalculatedUnitPriceCents);
                item.Parameters.AddWithValue(
                    "$total",
                    line.LineTotalCents);
                item.Parameters.AddWithValue(
                    "$display",
                    line.DisplayOrder);
                item.ExecuteNonQuery();
            }

            foreach (CafeOrderBootstrapModifierV1 modifier in
                line.ModifierSelections)
            {
                using SqliteCommand option =
                    connection.CreateCommand();
                option.Transaction = transaction;
                option.CommandText = """
                    INSERT INTO cafe_order_revision_modifiers(
                        revision_id,order_line_id,
                        group_id,group_name_snapshot,
                        option_id,option_name_snapshot,
                        price_delta_cents,display_order)
                    VALUES(
                        $revision,$line,
                        $group,$groupName,
                        $option,$optionName,
                        $price,$display);
                    """;
                option.Parameters.AddWithValue(
                    "$revision",
                    order.CurrentRevisionId.ToString("D"));
                option.Parameters.AddWithValue(
                    "$line",
                    line.OrderLineId.ToString("D"));
                option.Parameters.AddWithValue(
                    "$group",
                    modifier.GroupId);
                option.Parameters.AddWithValue(
                    "$groupName",
                    modifier.GroupNameSnapshot);
                option.Parameters.AddWithValue(
                    "$option",
                    modifier.OptionId);
                option.Parameters.AddWithValue(
                    "$optionName",
                    modifier.OptionNameSnapshot);
                option.Parameters.AddWithValue(
                    "$price",
                    modifier.PriceDeltaCents);
                option.Parameters.AddWithValue(
                    "$display",
                    modifier.DisplayOrder);
                option.ExecuteNonQuery();
            }
        }
    }

    private static Guid ReadStoreId(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT store_id FROM deployment_identity ORDER BY created_at LIMIT 1;";
        object? value = command.ExecuteScalar();

        return value is string text &&
               Guid.TryParse(text, out Guid id)
            ? id
            : throw new InvalidOperationException(
                "Local deployment StoreId missing.");
    }

    private static AuthorityRow? ReadAuthority(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        Guid storeId)
    {
        using SqliteCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT central_instance_id,baseline_server_sequence " +
            "FROM " + table + " WHERE store_id=$store LIMIT 1;";
        command.Parameters.AddWithValue(
            "$store",
            storeId.ToString("D"));
        using SqliteDataReader reader =
            command.ExecuteReader();

        return reader.Read()
            ? new AuthorityRow(
                Guid.Parse(reader.GetString(0)),
                reader.GetInt64(1))
            : null;
    }

    private static ExistingState? ReadState(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId)
    {
        using SqliteCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT central_instance_id,
                   baseline_server_sequence,
                   snapshot_id,snapshot_hash
            FROM cafe_order_bootstrap_state
            WHERE store_id=$store
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$store",
            storeId.ToString("D"));
        using SqliteDataReader reader =
            command.ExecuteReader();

        return reader.Read()
            ? new ExistingState(
                Guid.Parse(reader.GetString(0)),
                reader.GetInt64(1),
                Guid.Parse(reader.GetString(2)),
                reader.GetString(3))
            : null;
    }

    private static bool UserExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid userId)
    {
        using SqliteCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT COUNT(*) FROM users WHERE id=$id;";
        command.Parameters.AddWithValue(
            "$id",
            userId.ToString("D"));
        return Convert.ToInt64(
            command.ExecuteScalar() ?? 0L) == 1;
    }

    private static bool Exists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string idColumn,
        string id,
        string scopeColumn,
        string scope)
    {
        using SqliteCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT COUNT(*) FROM " + table +
            " WHERE " + idColumn + "=$id AND " +
            scopeColumn + "=$scope;";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$scope", scope);
        return Convert.ToInt64(
            command.ExecuteScalar() ?? 0L) == 1;
    }

    private static long ScalarLong(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql)
    {
        using SqliteCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return Convert.ToInt64(
            command.ExecuteScalar() ?? 0L);
    }

    private static object DbText(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? DBNull.Value
            : value.Trim();

    private static object DbGuid(Guid? value) =>
        value.HasValue && value.Value != Guid.Empty
            ? value.Value.ToString("D")
            : DBNull.Value;

    private sealed record AuthorityRow(
        Guid CentralInstanceId,
        long BaselineServerSequence);

    private sealed record ExistingState(
        Guid CentralInstanceId,
        long BaselineServerSequence,
        Guid SnapshotId,
        string SnapshotHash);
}

using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Infrastructure.Data.Repositories.Orders;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Services;

internal sealed record CafeBookingHistoricalOrderSeedResult(
    CafeOrderRevisionSnapshot Revision,
    CafeOrderRevisionSnapshotV1 ContractRevision);

internal static class SqliteCafeBookingHistoricalOrderParticipant
{
    internal static Result<CafeBookingHistoricalOrderSeedResult> Seed(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeBookingOrderBindMutation mutation,
        CafeBookingRevisionSnapshot bookingRevision)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentNullException.ThrowIfNull(bookingRevision);

        if (OrderExists(connection, transaction, mutation.OrderId))
            return Result<CafeBookingHistoricalOrderSeedResult>.Fail(
                Error.Conflict(
                    "CafeBooking.OrderIdConflict",
                    "Booking fulfillment requires an unused C4 OrderId."));

        Result publication = ValidatePublication(
            connection,
            transaction,
            bookingRevision.MenuPublicationRevisionId,
            mutation.StoreId);
        if (publication.IsFailure)
            return Result<CafeBookingHistoricalOrderSeedResult>.Fail(
                publication.ErrorDetail);

        foreach (CafeBookingLineSnapshot line in bookingRevision.Lines)
        {
            Result lineValidation = ValidateHistoricalLine(
                connection,
                transaction,
                bookingRevision.MenuPublicationRevisionId,
                line);
            if (lineValidation.IsFailure)
                return Result<CafeBookingHistoricalOrderSeedResult>.Fail(
                    lineValidation.ErrorDetail);
        }

        Guid orderRevisionId = SqliteCafeOrderStore.DeterministicGuid(
            "salryn-c9-booking-order-revision-v1",
            mutation.OperationId.ToString("D"));
        string serviceReference = "BOOKING:" + mutation.BookingId.ToString("D");

        CafeOrderLineSnapshot[] lines = bookingRevision.Lines
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.BookingLineId)
            .Select(line => new CafeOrderLineSnapshot(
                line.BookingLineId,
                line.ProductId,
                line.ProductNameSnapshot,
                line.UnitNameSnapshot,
                line.QuantityX1000,
                line.BaseUnitPriceCents,
                line.VariantId,
                line.VariantNameSnapshot,
                line.VariantUnitPriceCents,
                line.ModifierSelections
                    .OrderBy(x => x.DisplayOrder)
                    .ThenBy(x => x.GroupId, StringComparer.Ordinal)
                    .ThenBy(x => x.OptionId, StringComparer.Ordinal)
                    .Select(m => new CafeOrderModifierSelectionSnapshot(
                        m.GroupId,
                        m.GroupNameSnapshot,
                        m.OptionId,
                        m.OptionNameSnapshot,
                        m.PriceDeltaCents,
                        m.DisplayOrder))
                    .ToArray(),
                line.LineNote,
                line.QuotedUnitPriceCents,
                line.LineTotalCents,
                line.DisplayOrder))
            .ToArray();

        string revisionHash = SqliteCafeOrderStore.ComputeRevisionContentHash(
            mutation.OrderId,
            orderRevisionId,
            1,
            1,
            null,
            bookingRevision.MenuPublicationRevisionId,
            bookingRevision.IntendedServiceMode,
            serviceReference,
            lines);

        CafeOrderRevisionSnapshot revision = new(
            orderRevisionId,
            mutation.OrderId,
            1,
            1,
            null,
            bookingRevision.MenuPublicationRevisionId,
            bookingRevision.IntendedServiceMode,
            serviceReference,
            revisionHash,
            mutation.ActorUserId,
            mutation.OccurredAtUtc.ToUniversalTime(),
            lines);

        CafeOrderRevisionSnapshotV1 contractRevision =
            SqliteCafeOrderStore.ToContractRevision(revision);

        CafeOrderOperationV1 c4Payload = new(
            CafeOrderOperationContract.OrderRevisionCommittedV1,
            mutation.OrderId,
            mutation.StoreId,
            0,
            1,
            CafeOrderLifecycleActions.Created,
            null,
            CafeOrderStates.Open,
            mutation.ActorUserId,
            mutation.OccurredAtUtc.ToUniversalTime(),
            orderRevisionId,
            bookingRevision.MenuPublicationRevisionId,
            bookingRevision.IntendedServiceMode,
            serviceReference,
            contractRevision,
            null,
            null,
            null,
            null,
            null,
            null,
            Array.Empty<CafeOrderSaleItemLinkV1>());

        string c4Json = CafeOrderOperationCanonicalJson.Serialize(c4Payload);
        CafeOrderCanonicalParseResult c4Parsed =
            CafeOrderOperationCanonicalJson.Parse(
                CafeOrderOperationContract.OrderRevisionCommittedV1,
                c4Json);
        if (!c4Parsed.IsValid)
            return Result<CafeBookingHistoricalOrderSeedResult>.Fail(
                Error.Conflict(
                    c4Parsed.ErrorCode ?? "CafeBooking.HistoricalOrderInvalid",
                    c4Parsed.ErrorMessage ?? "Historical C4 seed evidence is invalid."));

        string c4Hash = CafeOrderOperationCanonicalJson.ComputeHash(c4Json);

        InsertOrder(
            connection,
            transaction,
            mutation,
            bookingRevision,
            orderRevisionId,
            serviceReference);

        SqliteCafeOrderStore.InsertRevision(
            connection,
            transaction,
            revision);

        SqliteCafeOrderStore.InsertOperationAndLifecycle(
            connection,
            transaction,
            mutation.OperationId,
            mutation.OrderId,
            CafeOrderOperationContract.OrderRevisionCommittedV1,
            c4Hash,
            0,
            1,
            orderRevisionId,
            CafeOrderStates.Open,
            null,
            mutation.ActorUserId,
            mutation.OccurredAtUtc,
            CafeOrderLifecycleActions.Created,
            null,
            CafeOrderStates.Open,
            null,
            orderRevisionId,
            null,
            null);

        return Result<CafeBookingHistoricalOrderSeedResult>.Success(
            new CafeBookingHistoricalOrderSeedResult(
                revision,
                contractRevision));
    }

    private static Result ValidatePublication(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string revisionId,
        Guid storeId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM menu_revisions
            WHERE revision_id=$revision
              AND store_id=$store
              AND status IN ('Published','Superseded');
            """;
        command.Parameters.AddWithValue("$revision", revisionId);
        command.Parameters.AddWithValue("$store", storeId.ToString("D"));

        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) == 1
            ? Result.Success()
            : Result.Fail(
                Error.Conflict(
                    "CafeBooking.HistoricalMenuPublicationMissing",
                    "Frozen Booking MenuPublication does not exist for the same Store."));
    }

    private static Result ValidateHistoricalLine(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string revisionId,
        CafeBookingLineSnapshot line)
    {
        using (SqliteCommand item = connection.CreateCommand())
        {
            item.Transaction = transaction;
            item.CommandText = """
                SELECT name,COALESCE(unit_name,'Unit'),selling_price_cents,
                       is_temporarily_unavailable
                FROM menu_revision_items
                WHERE revision_id=$revision
                  AND product_id=$product
                LIMIT 1;
                """;
            item.Parameters.AddWithValue("$revision", revisionId);
            item.Parameters.AddWithValue("$product", line.ProductId.ToString("D"));
            using SqliteDataReader reader = item.ExecuteReader();
            if (!reader.Read())
                return Result.Fail(
                    Error.Conflict(
                        "CafeBooking.HistoricalProductMismatch",
                        "Frozen Booking Product is absent from the historical MenuPublication."));

            if (!string.Equals(reader.GetString(0), line.ProductNameSnapshot, StringComparison.Ordinal) ||
                !string.Equals(reader.GetString(1), line.UnitNameSnapshot, StringComparison.Ordinal) ||
                reader.GetInt64(2) != line.BaseUnitPriceCents ||
                reader.GetInt32(3) != 0)
                return Result.Fail(
                    Error.Conflict(
                        "CafeBooking.HistoricalProductSnapshotMismatch",
                        "Frozen Booking Product snapshot no longer matches its historical MenuPublication evidence."));
        }

        long selectedBase = line.BaseUnitPriceCents;
        if (line.VariantId is not null)
        {
            using SqliteCommand variant = connection.CreateCommand();
            variant.Transaction = transaction;
            variant.CommandText = """
                SELECT product_id,name,selling_price_cents,is_temporarily_unavailable
                FROM menu_revision_variants
                WHERE revision_id=$revision
                  AND variant_id=$variant
                LIMIT 1;
                """;
            variant.Parameters.AddWithValue("$revision", revisionId);
            variant.Parameters.AddWithValue("$variant", line.VariantId);
            using SqliteDataReader reader = variant.ExecuteReader();
            if (!reader.Read() ||
                Guid.Parse(reader.GetString(0)) != line.ProductId ||
                !string.Equals(reader.GetString(1), line.VariantNameSnapshot, StringComparison.Ordinal) ||
                !line.VariantUnitPriceCents.HasValue ||
                reader.GetInt64(2) != line.VariantUnitPriceCents.Value ||
                reader.GetInt32(3) != 0)
                return Result.Fail(
                    Error.Conflict(
                        "CafeBooking.HistoricalVariantSnapshotMismatch",
                        "Frozen Booking Variant snapshot does not match the historical MenuPublication."));

            selectedBase = line.VariantUnitPriceCents.Value;
        }
        else if (line.VariantNameSnapshot is not null ||
                 line.VariantUnitPriceCents.HasValue)
        {
            return Result.Fail(
                Error.Conflict(
                    "CafeBooking.HistoricalVariantSnapshotMismatch",
                    "Frozen Booking Variant evidence is internally inconsistent."));
        }

        long modifierDelta = 0;
        foreach (CafeBookingModifierSelectionSnapshot modifier in line.ModifierSelections)
        {
            using SqliteCommand option = connection.CreateCommand();
            option.Transaction = transaction;
            option.CommandText = """
                SELECT g.name,o.name,o.price_delta_cents,o.display_order,
                       CASE
                         WHEN EXISTS(
                           SELECT 1
                           FROM menu_revision_item_modifier_groups l
                           WHERE l.revision_id=$revision
                             AND l.product_id=$product
                             AND l.group_id=o.group_id)
                         OR EXISTS(
                           SELECT 1
                           FROM menu_revision_variant_modifier_groups l
                           WHERE l.revision_id=$revision
                             AND l.variant_id=$variant
                             AND l.group_id=o.group_id)
                         THEN 1 ELSE 0
                       END
                FROM menu_revision_modifier_options o
                INNER JOIN menu_revision_modifier_groups g
                  ON g.revision_id=o.revision_id
                 AND g.group_id=o.group_id
                WHERE o.revision_id=$revision
                  AND o.option_id=$option
                  AND o.group_id=$group
                LIMIT 1;
                """;
            option.Parameters.AddWithValue("$revision", revisionId);
            option.Parameters.AddWithValue("$product", line.ProductId.ToString("D"));
            option.Parameters.AddWithValue("$variant", line.VariantId ?? string.Empty);
            option.Parameters.AddWithValue("$option", modifier.OptionId);
            option.Parameters.AddWithValue("$group", modifier.GroupId);
            using SqliteDataReader reader = option.ExecuteReader();
            if (!reader.Read() ||
                !string.Equals(reader.GetString(0), modifier.GroupNameSnapshot, StringComparison.Ordinal) ||
                !string.Equals(reader.GetString(1), modifier.OptionNameSnapshot, StringComparison.Ordinal) ||
                reader.GetInt64(2) != modifier.PriceDeltaCents ||
                reader.GetInt32(3) != modifier.DisplayOrder ||
                reader.GetInt32(4) != 1)
                return Result.Fail(
                    Error.Conflict(
                        "CafeBooking.HistoricalModifierSnapshotMismatch",
                        "Frozen Booking Modifier snapshot does not match the historical MenuPublication."));

            modifierDelta = checked(modifierDelta + modifier.PriceDeltaCents);
        }

        long expectedUnit = checked(selectedBase + modifierDelta);
        long expectedTotal = checked((long)Math.Round(
            expectedUnit * (line.QuantityX1000 / 1000m),
            0,
            MidpointRounding.AwayFromZero));

        if (expectedUnit != line.QuotedUnitPriceCents ||
            expectedTotal != line.LineTotalCents)
            return Result.Fail(
                Error.Conflict(
                    "CafeBooking.HistoricalPriceSnapshotMismatch",
                    "Frozen Booking quote does not re-derive from its historical MenuPublication."));

        return Result.Success();
    }

    private static void InsertOrder(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeBookingOrderBindMutation mutation,
        CafeBookingRevisionSnapshot revision,
        Guid orderRevisionId,
        string serviceReference)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cafe_orders(
                order_id,store_id,current_version,current_revision_id,
                menu_publication_revision_id,state,service_mode,service_reference,
                created_by_user_id,created_at_utc,updated_at_utc,
                submitted_revision_id,submitted_recipe_book_revision_id,
                completed_sale_id,cancellation_reason)
            VALUES(
                $order,$store,1,$revision,
                $menu,'Open',$mode,$reference,
                $actor,$at,$at,NULL,NULL,NULL,NULL);
            """;
        command.Parameters.AddWithValue("$order", mutation.OrderId.ToString("D"));
        command.Parameters.AddWithValue("$store", mutation.StoreId.ToString("D"));
        command.Parameters.AddWithValue("$revision", orderRevisionId.ToString("D"));
        command.Parameters.AddWithValue("$menu", revision.MenuPublicationRevisionId);
        command.Parameters.AddWithValue("$mode", revision.IntendedServiceMode);
        command.Parameters.AddWithValue("$reference", serviceReference);
        command.Parameters.AddWithValue("$actor", mutation.ActorUserId.ToString("D"));
        command.Parameters.AddWithValue("$at", mutation.OccurredAtUtc.ToUniversalTime().ToString("O"));
        command.ExecuteNonQuery();
    }

    private static bool OrderExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid orderId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM cafe_orders WHERE order_id=$order;";
        command.Parameters.AddWithValue("$order", orderId.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) != 0;
    }
}

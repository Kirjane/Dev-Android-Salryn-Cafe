using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Seeding;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tools.CafePrintProof;

internal static class C7PrintBehaviorProof
{
    internal static void Run(string root)
    {
        DbPaths paths = DbPaths.CreateAtRoot(root);
        _ = RequireValue(
            DefaultDatabaseInitializationFactory.Create(paths)
                .InitializeDatabase(),
            "C7 behavior initialization");

        SqliteConnectionFactory factory = new(paths);
        Guid storeId = Guid.Parse(
            ScalarString(
                factory,
                "SELECT store_id FROM deployment_identity LIMIT 1;"));

        Guid adminId = Guid.Parse("77000000-0000-0000-0000-000000000101");
        Guid cashierId = Guid.Parse("77000000-0000-0000-0000-000000000102");
        Guid receiptStationId = Guid.Parse("77000000-0000-0000-0000-000000000103");
        Guid kitchenStationId = Guid.Parse("77000000-0000-0000-0000-000000000104");
        Guid deviceId = Guid.Parse("77000000-0000-0000-0000-000000000105");
        DateTimeOffset at = DateTimeOffset.Parse("2026-09-22T10:30:00Z");

        InsertUser(
            factory,
            adminId,
            "c7-admin",
            DefaultSeedData.AdminRoleId,
            at);
        InsertUser(
            factory,
            cashierId,
            "c7-cashier",
            DefaultSeedData.CashierRoleId,
            at);

        SqliteCafePrintService service = new(factory);

        CafePrinterStationMutationResult receiptStation =
            RequireValue(
                service.PublishStation(
                    new CafePrinterStationMutation(
                        Guid.NewGuid(),
                        receiptStationId,
                        storeId,
                        adminId,
                        "Customer Receipt",
                        CafePrinterStationKind.CustomerReceipt,
                        "front-counter",
                        true,
                        0,
                        at)),
                "C7 receipt station publish");

        CafePrinterStationMutationResult kitchenStation =
            RequireValue(
                service.PublishStation(
                    new CafePrinterStationMutation(
                        Guid.NewGuid(),
                        kitchenStationId,
                        storeId,
                        adminId,
                        "Kitchen",
                        CafePrinterStationKind.Kitchen,
                        "kitchen-main",
                        true,
                        0,
                        at.AddSeconds(1))),
                "C7 kitchen station publish");

        Require(
            receiptStation.Station.Version == 1 &&
            kitchenStation.Station.Version == 1,
            "C7 printer stations must begin at version 1.");

        Result<CafePrinterStationMutationResult> cashierStationDenied =
            service.PublishStation(
                new CafePrinterStationMutation(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    storeId,
                    cashierId,
                    "Unauthorized",
                    CafePrinterStationKind.CustomerReceipt,
                    null,
                    true,
                    0,
                    at.AddSeconds(2)));

        Require(
            cashierStationDenied.IsFailure &&
            string.Equals(
                cashierStationDenied.ErrorDetail.Code,
                "CafePrint.PermissionRequired",
                StringComparison.Ordinal),
            "Cashier must not manage logical printer stations.");

        Guid saleId = Guid.Parse("77000000-0000-0000-0000-000000000111");
        const string receiptNo = "C7-000001";
        const string persistedReceipt =
            "CAFE YZA\nReceipt C7-000001\nCoffee x1 100.00\nTOTAL 100.00";

        InsertSale(
            factory,
            saleId,
            receiptNo,
            cashierId,
            SaleStatus.Completed,
            persistedReceipt,
            null,
            null,
            at.AddMinutes(1));

        Guid receiptOperation = Guid.NewGuid();
        Guid receiptJobId = Guid.NewGuid();

        CafePrintJobMutationResult receiptJob =
            RequireValue(
                service.CreateReceiptJobAsync(
                        new CafeReceiptPrintJobMutation(
                            receiptOperation,
                            receiptJobId,
                            deviceId,
                            storeId,
                            cashierId,
                            receiptStationId,
                            saleId,
                            CafePrintCopyKind.Original,
                            null,
                            at.AddMinutes(2)))
                    .GetAwaiter()
                    .GetResult(),
                "C7 customer receipt job");

        Require(
            !receiptJob.IsReplay &&
            receiptJob.Job.SnapshotText == persistedReceipt &&
            receiptJob.Job.CopyKind == CafePrintCopyKind.Original,
            "C7 initial receipt job must bind the exact persisted receipt snapshot.");

        CafePrintJobMutationResult receiptReplay =
            RequireValue(
                service.CreateReceiptJobAsync(
                        new CafeReceiptPrintJobMutation(
                            receiptOperation,
                            receiptJobId,
                            deviceId,
                            storeId,
                            cashierId,
                            receiptStationId,
                            saleId,
                            CafePrintCopyKind.Original,
                            null,
                            at.AddMinutes(2)))
                    .GetAwaiter()
                    .GetResult(),
                "C7 exact receipt replay");

        Require(
            receiptReplay.IsReplay &&
            receiptReplay.Job.JobId == receiptJobId &&
            ScalarLong(
                factory,
                "SELECT COUNT(*) FROM cafe_print_jobs WHERE operation_id=$operation;",
                ("$operation", receiptOperation.ToString("D"))) == 1,
            "C7 exact receipt operation replay must be idempotent.");

        Result<CafePrintJobMutationResult> timestampCollision =
            service.CreateReceiptJobAsync(
                    new CafeReceiptPrintJobMutation(
                        receiptOperation,
                        receiptJobId,
                        deviceId,
                        storeId,
                        cashierId,
                        receiptStationId,
                        saleId,
                        CafePrintCopyKind.Original,
                        null,
                        at.AddMinutes(2).AddSeconds(1)))
                .GetAwaiter()
                .GetResult();

        Require(
            timestampCollision.IsFailure &&
            string.Equals(
                timestampCollision.ErrorDetail.Code,
                "CafePrint.OperationPayloadConflict",
                StringComparison.Ordinal),
            "C7 same OperationId with changed canonical timestamp must conflict.");

        Result<CafePrintJobMutationResult> collision =
            service.CreateReceiptJobAsync(
                    new CafeReceiptPrintJobMutation(
                        receiptOperation,
                        Guid.NewGuid(),
                        deviceId,
                        storeId,
                        cashierId,
                        receiptStationId,
                        saleId,
                        CafePrintCopyKind.Original,
                        null,
                        at.AddMinutes(2)))
                .GetAwaiter()
                .GetResult();

        Require(
            collision.IsFailure &&
            string.Equals(
                collision.ErrorDetail.Code,
                "CafePrint.OperationPayloadConflict",
                StringComparison.Ordinal),
            "C7 same OperationId with different receipt job evidence must conflict.");

        CafePrintAttemptResult successAttempt =
            RequireValue(
                service.ExecuteAttemptAsync(
                        receiptJobId,
                        deviceId,
                        cashierId,
                        new NoOpReceiptPrinterAdapter("C7.Success"),
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult(),
                "C7 success attempt");

        CafePrintAttemptResult failedAttempt =
            RequireValue(
                service.ExecuteAttemptAsync(
                        receiptJobId,
                        deviceId,
                        cashierId,
                        new FailingAdapter(),
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult(),
                "C7 failed attempt");

        CafePrintAttemptResult disabledAttempt =
            RequireValue(
                service.ExecuteAttemptAsync(
                        receiptJobId,
                        deviceId,
                        cashierId,
                        new DisabledReceiptPrinterAdapter(
                            "C7 disabled proof",
                            "C7.Disabled"),
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult(),
                "C7 disabled attempt");

        Require(
            successAttempt.Attempt.Status == CafePrintAttemptStatus.Succeeded &&
            failedAttempt.Attempt.Status == CafePrintAttemptStatus.Failed &&
            disabledAttempt.Attempt.Status == CafePrintAttemptStatus.DisabledUnavailable &&
            successAttempt.Attempt.AttemptNumber == 1 &&
            failedAttempt.Attempt.AttemptNumber == 2 &&
            disabledAttempt.Attempt.AttemptNumber == 3,
            "C7 print attempts must append exact outcome evidence and attempt numbers.");

        Require(
            ScalarLong(
                factory,
                """
                SELECT COUNT(*)
                FROM sales
                WHERE id=$sale
                  AND status=$completed
                  AND receipt_snapshot=$snapshot;
                """,
                ("$sale", saleId.ToString("D")),
                ("$completed", Convert.ToInt32(SaleStatus.Completed)),
                ("$snapshot", persistedReceipt)) == 1,
            "Printer success/failure/disabled attempts must leave Sale state and receipt snapshot unchanged.");

        Guid receiptReprintJobId = Guid.NewGuid();
        CafePrintJobMutationResult reprint =
            RequireValue(
                service.CreateReceiptJobAsync(
                        new CafeReceiptPrintJobMutation(
                            Guid.NewGuid(),
                            receiptReprintJobId,
                            deviceId,
                            storeId,
                            adminId,
                            receiptStationId,
                            saleId,
                            CafePrintCopyKind.Reprint,
                            receiptJobId,
                            at.AddMinutes(3)))
                    .GetAwaiter()
                    .GetResult(),
                "C7 receipt reprint");

        Require(
            reprint.Job.CopyKind == CafePrintCopyKind.Reprint &&
            reprint.Job.ParentJobId == receiptJobId &&
            reprint.Job.SnapshotText == persistedReceipt,
            "C7 explicit receipt reprint must retain original persisted receipt body.");

        string voidReason = "Wrong order";
        DateTimeOffset voidedAt = at.AddMinutes(4);

        Execute(
            factory,
            """
            UPDATE sales
            SET status=$voided,
                voided_by_user_id=$actor,
                void_reason=$reason,
                voided_at=$at
            WHERE id=$sale;
            """,
            ("$voided", Convert.ToInt32(SaleStatus.Voided)),
            ("$actor", adminId.ToString("D")),
            ("$reason", voidReason),
            ("$at", voidedAt.ToString("O")),
            ("$sale", saleId.ToString("D")));

        CafePrintJobMutationResult voidedCopy =
            RequireValue(
                service.CreateReceiptJobAsync(
                        new CafeReceiptPrintJobMutation(
                            Guid.NewGuid(),
                            Guid.NewGuid(),
                            deviceId,
                            storeId,
                            adminId,
                            receiptStationId,
                            saleId,
                            CafePrintCopyKind.VoidedReprint,
                            receiptJobId,
                            at.AddMinutes(5)))
                    .GetAwaiter()
                    .GetResult(),
                "C7 voided receipt copy");

        Require(
            voidedCopy.Job.SnapshotText.StartsWith(
                persistedReceipt,
                StringComparison.Ordinal) &&
            voidedCopy.Job.SnapshotText.Contains(
                "*** VOIDED RECEIPT COPY ***",
                StringComparison.Ordinal) &&
            voidedCopy.Job.SnapshotText.Contains(
                voidReason,
                StringComparison.Ordinal) &&
            ScalarString(
                factory,
                "SELECT snapshot_text FROM cafe_print_jobs WHERE job_id=$job;",
                ("$job", receiptJobId.ToString("D"))) == persistedReceipt,
            "C7 voided receipt copy must preserve original job body and append immutable void-copy evidence.");

        Result<CafePrintJobMutationResult> cashierReprintDenied =
            service.CreateReceiptJobAsync(
                    new CafeReceiptPrintJobMutation(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        deviceId,
                        storeId,
                        cashierId,
                        receiptStationId,
                        saleId,
                        CafePrintCopyKind.VoidedReprint,
                        receiptJobId,
                        at.AddMinutes(6)))
                .GetAwaiter()
                .GetResult();

        Require(
            cashierReprintDenied.IsFailure &&
            string.Equals(
                cashierReprintDenied.ErrorDetail.Code,
                "CafePrint.PermissionRequired",
                StringComparison.Ordinal),
            "Cashier default role must not perform reprint authority.");

        KitchenFixture kitchen =
            InsertKitchenFixture(
                factory,
                storeId,
                adminId,
                at.AddMinutes(10));

        Guid kitchenJobId = Guid.NewGuid();

        CafePrintJobMutationResult kitchenJob =
            RequireValue(
                service.CreateKitchenTicketJob(
                    new CafeKitchenPrintJobMutation(
                        Guid.NewGuid(),
                        kitchenJobId,
                        deviceId,
                        storeId,
                        adminId,
                        kitchenStationId,
                        kitchen.OrderId,
                        kitchen.RevisionId,
                        CafePrintCopyKind.Original,
                        null,
                        at.AddMinutes(11))),
                "C7 kitchen ticket");

        Require(
            kitchenJob.Job.SnapshotText.Contains(
                kitchen.OrderId.ToString("D"),
                StringComparison.Ordinal) &&
            kitchenJob.Job.SnapshotText.Contains(
                kitchen.RevisionId.ToString("D"),
                StringComparison.Ordinal) &&
            kitchenJob.Job.SnapshotText.Contains(
                "2 x Iced Coffee [Large]",
                StringComparison.Ordinal) &&
            kitchenJob.Job.SnapshotText.Contains(
                "+ Sugar: Less Sugar",
                StringComparison.Ordinal) &&
            kitchenJob.Job.SnapshotText.Contains(
                "NOTE: No straw",
                StringComparison.Ordinal),
            "C7 kitchen snapshot must freeze exact submitted C4 revision content.");

        string frozenKitchenSnapshot =
            kitchenJob.Job.SnapshotText;

        Execute(
            factory,
            """
            UPDATE cafe_orders
            SET current_version=current_version+1,
                state='Preparing',
                service_reference='CHANGED-AFTER-PRINT',
                updated_at_utc=$at
            WHERE order_id=$order;
            """,
            ("$at", at.AddMinutes(12).ToString("O")),
            ("$order", kitchen.OrderId.ToString("D")));

        CafePrintJobMutationResult kitchenReprint =
            RequireValue(
                service.CreateKitchenTicketJob(
                    new CafeKitchenPrintJobMutation(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        deviceId,
                        storeId,
                        adminId,
                        kitchenStationId,
                        kitchen.OrderId,
                        kitchen.RevisionId,
                        CafePrintCopyKind.Reprint,
                        kitchenJobId,
                        at.AddMinutes(13))),
                "C7 kitchen ticket reprint");

        Require(
            kitchenReprint.Job.SnapshotText == frozenKitchenSnapshot,
            "C7 kitchen reprint must reuse exact immutable submitted revision content despite later order-state changes.");

        CafePrintJobMutationResult legacySourceKitchenReprint =
            RequireValue(
                service.CreateKitchenTicketJob(
                    new CafeKitchenPrintJobMutation(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        deviceId,
                        storeId,
                        adminId,
                        kitchenStationId,
                        kitchen.OrderId,
                        kitchen.RevisionId,
                        CafePrintCopyKind.Reprint,
                        null,
                        at.AddMinutes(13).AddSeconds(30))),
                "C7 kitchen ticket legacy-source reprint");

        Require(
            legacySourceKitchenReprint.Job.SnapshotText == frozenKitchenSnapshot,
            "C7 kitchen reprint without ParentJobId must rebuild only from immutable submitted revision/lifecycle evidence.");

        Result<CafePrintJobMutationResult> duplicateInitial =
            service.CreateKitchenTicketJob(
                new CafeKitchenPrintJobMutation(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    deviceId,
                    storeId,
                    adminId,
                    kitchenStationId,
                    kitchen.OrderId,
                    kitchen.RevisionId,
                    CafePrintCopyKind.Original,
                    null,
                    at.AddMinutes(14)));

        Require(
            duplicateInitial.IsFailure,
            "C7 duplicate initial kitchen job must be rejected.");

        CafePrinterStationMutationResult stationUpdate =
            RequireValue(
                service.PublishStation(
                    new CafePrinterStationMutation(
                        Guid.NewGuid(),
                        kitchenStationId,
                        storeId,
                        adminId,
                        "Kitchen Main",
                        CafePrinterStationKind.Kitchen,
                        "kitchen-main-v2",
                        true,
                        1,
                        at.AddMinutes(15))),
                "C7 station version update");

        Require(
            stationUpdate.Station.Version == 2 &&
            ScalarString(
                factory,
                "SELECT snapshot_text FROM cafe_print_jobs WHERE job_id=$job;",
                ("$job", kitchenJobId.ToString("D"))) == frozenKitchenSnapshot,
            "C7 station configuration updates must not rewrite retained print job snapshots.");

        ExpectSqliteFailure(
            factory,
            "UPDATE cafe_print_jobs SET snapshot_text='tampered' WHERE job_id=$job;",
            ("$job", receiptJobId.ToString("D")));

        ExpectSqliteFailure(
            factory,
            "DELETE FROM cafe_print_attempts WHERE job_id=$job;",
            ("$job", receiptJobId.ToString("D")));

        Console.WriteLine(
            "PASS C7 stations, receipt snapshots, attempt outcomes, voided copies, kitchen snapshots and reprints");
    }

    private static KitchenFixture InsertKitchenFixture(
        SqliteConnectionFactory factory,
        Guid storeId,
        Guid actorUserId,
        DateTimeOffset at)
    {
        Guid menuRevisionId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        Guid revisionId = Guid.NewGuid();
        Guid orderLineId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        Guid submitOperation = Guid.NewGuid();

        Execute(
            factory,
            """
            INSERT INTO menu_revisions(
                revision_id,store_id,revision_number,
                content_hash,publication_hash,status,
                created_at_utc,published_at_utc,superseded_at_utc)
            VALUES(
                $revision,$store,9001,
                $content,$publication,'Published',
                $at,$at,NULL);
            """,
            ("$revision", menuRevisionId.ToString("D")),
            ("$store", storeId.ToString("D")),
            ("$content", new string('1', 64)),
            ("$publication", new string('2', 64)),
            ("$at", at.ToString("O")));

        Execute(
            factory,
            """
            INSERT INTO cafe_orders(
                order_id,store_id,current_version,current_revision_id,
                menu_publication_revision_id,state,service_mode,service_reference,
                created_by_user_id,created_at_utc,updated_at_utc,
                submitted_revision_id,submitted_recipe_book_revision_id,
                completed_sale_id,cancellation_reason)
            VALUES(
                $order,$store,2,$revision,
                $menu,'Submitted','DineIn','TABLE-7',
                $actor,$at,$at,
                $revision,NULL,NULL,NULL);
            """,
            ("$order", orderId.ToString("D")),
            ("$store", storeId.ToString("D")),
            ("$revision", revisionId.ToString("D")),
            ("$menu", menuRevisionId.ToString("D")),
            ("$actor", actorUserId.ToString("D")),
            ("$at", at.ToString("O")));

        Execute(
            factory,
            """
            INSERT INTO cafe_order_revisions(
                revision_id,order_id,revision_number,order_version,
                previous_revision_id,menu_publication_revision_id,
                service_mode,service_reference,content_hash,
                actor_user_id,created_at_utc)
            VALUES(
                $revision,$order,1,1,
                NULL,$menu,
                'DineIn','TABLE-7',$hash,
                $actor,$at);
            """,
            ("$revision", revisionId.ToString("D")),
            ("$order", orderId.ToString("D")),
            ("$menu", menuRevisionId.ToString("D")),
            ("$hash", new string('3', 64)),
            ("$actor", actorUserId.ToString("D")),
            ("$at", at.ToString("O")));

        Execute(
            factory,
            """
            INSERT INTO cafe_order_revision_lines(
                revision_id,order_line_id,product_id,
                product_name_snapshot,unit_name_snapshot,
                quantity_x1000,base_unit_price_cents,
                variant_id,variant_name_snapshot,variant_unit_price_cents,
                note,calculated_unit_price_cents,line_total_cents,display_order)
            VALUES(
                $revision,$line,$product,
                'Iced Coffee','cup',
                2000,10000,
                'large','Large',12000,
                'No straw',12000,24000,1);
            """,
            ("$revision", revisionId.ToString("D")),
            ("$line", orderLineId.ToString("D")),
            ("$product", productId.ToString("D")));

        Execute(
            factory,
            """
            INSERT INTO cafe_order_revision_modifiers(
                revision_id,order_line_id,
                group_id,group_name_snapshot,
                option_id,option_name_snapshot,
                price_delta_cents,display_order)
            VALUES(
                $revision,$line,
                'sugar','Sugar',
                'less','Less Sugar',
                0,1);
            """,
            ("$revision", revisionId.ToString("D")),
            ("$line", orderLineId.ToString("D")));

        Execute(
            factory,
            """
            INSERT INTO cafe_order_operations(
                operation_id,order_id,operation_type,payload_hash,
                expected_version,resulting_version,result_revision_id,
                result_state,result_sale_id,actor_user_id,occurred_at_utc)
            VALUES(
                $operation,$order,'OrderPreparationSubmittedV1',$hash,
                1,2,$revision,
                'Submitted',NULL,$actor,$at);
            """,
            ("$operation", submitOperation.ToString("D")),
            ("$order", orderId.ToString("D")),
            ("$hash", new string('4', 64)),
            ("$revision", revisionId.ToString("D")),
            ("$actor", actorUserId.ToString("D")),
            ("$at", at.AddMinutes(1).ToString("O")));

        Execute(
            factory,
            """
            INSERT INTO cafe_order_lifecycle_events(
                event_id,operation_id,order_id,
                expected_version,resulting_version,
                action,from_state,to_state,
                actor_user_id,occurred_at_utc,
                reason,order_revision_id,recipe_book_revision_id,
                sale_id,content_hash)
            VALUES(
                $event,$operation,$order,
                1,2,
                'PreparationSubmitted','Open','Submitted',
                $actor,$at,
                NULL,$revision,NULL,
                NULL,$hash);
            """,
            ("$event", Guid.NewGuid().ToString("D")),
            ("$operation", submitOperation.ToString("D")),
            ("$order", orderId.ToString("D")),
            ("$actor", actorUserId.ToString("D")),
            ("$at", at.AddMinutes(1).ToString("O")),
            ("$revision", revisionId.ToString("D")),
            ("$hash", new string('5', 64)));

        return new KitchenFixture(orderId, revisionId);
    }

    private static void InsertSale(
        SqliteConnectionFactory factory,
        Guid saleId,
        string receiptNo,
        Guid cashierUserId,
        SaleStatus status,
        string receiptSnapshot,
        string? voidReason,
        DateTimeOffset? voidedAt,
        DateTimeOffset at)
    {
        Execute(
            factory,
            """
            INSERT INTO sales(
                id,receipt_no,sale_date,cashier_user_id,cashier_session_id,
                subtotal_cents,discount_cents,tax_cents,total_cents,
                amount_paid_cents,change_cents,status,
                voided_by_user_id,void_reason,voided_at,
                receipt_snapshot,created_at)
            VALUES(
                $id,$receipt,$at,$cashier,NULL,
                10000,0,0,10000,
                10000,0,$status,
                NULL,$reason,$voidedAt,
                $snapshot,$at);
            """,
            ("$id", saleId.ToString("D")),
            ("$receipt", receiptNo),
            ("$at", at.ToString("O")),
            ("$cashier", cashierUserId.ToString("D")),
            ("$status", Convert.ToInt32(status)),
            ("$reason", voidReason),
            ("$voidedAt", voidedAt?.ToString("O")),
            ("$snapshot", receiptSnapshot));
    }

    private static void InsertUser(
        SqliteConnectionFactory factory,
        Guid id,
        string username,
        string roleId,
        DateTimeOffset at)
    {
        Execute(
            factory,
            """
            INSERT INTO users(
                id,username,display_name,password_hash,
                role_id,status,created_at,updated_at)
            VALUES(
                $id,$username,$username,'proof',
                $role,1,$at,NULL);
            """,
            ("$id", id.ToString("D")),
            ("$username", username),
            ("$role", roleId),
            ("$at", at.ToString("O")));
    }

    private static long ScalarLong(
        SqliteConnectionFactory factory,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);

        return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
    }

    private static string ScalarString(
        SqliteConnectionFactory factory,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);

        return Convert.ToString(command.ExecuteScalar())
            ?? throw new InvalidOperationException(
                "Expected scalar string.");
    }

    private static void Execute(
        SqliteConnectionFactory factory,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);

        command.ExecuteNonQuery();
    }

    private static void ExpectSqliteFailure(
        SqliteConnectionFactory factory,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        bool failed = false;
        try
        {
            Execute(factory, sql, parameters);
        }
        catch (SqliteException)
        {
            failed = true;
        }

        Require(
            failed,
            "Expected immutable C7 print evidence mutation to fail.");
    }

    private static T RequireValue<T>(
        Result<T> result,
        string operation)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                operation + " failed: " +
                result.ErrorDetail.Code + " / " +
                result.ErrorDetail.Message);
        }

        return result.Value;
    }

    private static void Require(
        bool condition,
        string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed record KitchenFixture(
        Guid OrderId,
        Guid RevisionId);

    private sealed class FailingAdapter : IReceiptPrinterAdapter
    {
        public string AdapterName => "C7.Failing";

        public bool IsEnabled => true;

        public Task<ReceiptPrinterAdapterResult> PrintAsync(
            string jobName,
            string receiptText,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                ReceiptPrinterAdapterResult.Failed(
                    "C7 forced adapter failure.",
                    "proof failure"));
        }
    }
}

using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Repositories.Orders;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Data.Sync;
using Salryn.Infrastructure.Paths;
using Salryn.Sync.Contracts;

namespace Salryn.Cafe.I3Tests;

// Synthetic fixture adapted from preserved Yza4241ea1e C5 proof. No production data.
internal static class CanonicalOrderCompletionFixture
{
    private static readonly Guid StoreId =
        Guid.Parse("54000000-0000-0000-0000-000000000001");
    private static readonly Guid DeviceId =
        Guid.Parse("54000000-0000-0000-0000-000000000002");
    private static readonly Guid ActorUserId =
        Guid.Parse("54000000-0000-0000-0000-000000000003");
    private static readonly Guid CashierSessionId =
        Guid.Parse("54000000-0000-0000-0000-000000000004");
    private static readonly Guid CustomerId =
        Guid.Parse("54000000-0000-0000-0000-000000000005");
    private static readonly Guid ProductId =
        Guid.Parse("54000000-0000-0000-0000-000000000006");
    private static readonly Guid OrderId =
        Guid.Parse("54000000-0000-0000-0000-000000000007");
    private static readonly Guid OrderLineId =
        Guid.Parse("54000000-0000-0000-0000-000000000008");

    private const string MenuRevisionId =
        "c5-financial-proof-menu-r1";

    internal static (SqliteConnectionFactory Factory, CafeOrderCompletionMutation Mutation) Create(string root)
    {
        DbPaths paths = DbPaths.CreateAtRoot(root);
        Result<DatabaseInitializationSummary> initialized =
            DefaultDatabaseInitializationFactory
                .Create(paths)
                .InitializeDatabase();

        Require(
            initialized.IsSuccess,
            "C5 order-financial fixture initialization failed.");

        SqliteConnectionFactory factory =
            new(paths);
        SqliteCafeOrderStore orders =
            new(factory);
        SqliteCafeOrderCompletionCoordinator completion =
            new(factory);

        DateTimeOffset at =
            DateTimeOffset.Parse(
                "2026-09-22T04:00:00Z");

        SeedAuthority(factory, at);

        RequireValue(
            orders.SaveRevision(
                new CafeOrderRevisionMutation(
                    Guid.Parse(
                        "54000000-0000-0000-0000-000000000010"),
                    DeviceId,
                    OrderId,
                    StoreId,
                    0,
                    ActorUserId,
                    at,
                    CafeOrderServiceModes.DineIn,
                    "C5 Table",
                    new[]
                    {
                        new CafeOrderLineMutation(
                            OrderLineId,
                            ProductId,
                            null,
                            Array.Empty<string>(),
                            1000,
                            null,
                            0)
                    })),
            "create C5 financial order");

        RequireValue(
            orders.SubmitPreparation(
                new CafeOrderPreparationSubmitMutation(
                    Guid.Parse(
                        "54000000-0000-0000-0000-000000000011"),
                    DeviceId,
                    OrderId,
                    StoreId,
                    1,
                    ActorUserId,
                    at.AddSeconds(1))),
            "submit C5 financial order");

        RequireValue(
            orders.SetPreparationStatus(
                new CafeOrderPreparationStatusMutation(
                    Guid.Parse(
                        "54000000-0000-0000-0000-000000000012"),
                    DeviceId,
                    OrderId,
                    StoreId,
                    2,
                    ActorUserId,
                    at.AddSeconds(2),
                    CafeOrderStates.Preparing)),
            "prepare C5 financial order");

        RequireValue(
            orders.SetPreparationStatus(
                new CafeOrderPreparationStatusMutation(
                    Guid.Parse(
                        "54000000-0000-0000-0000-000000000013"),
                    DeviceId,
                    OrderId,
                    StoreId,
                    3,
                    ActorUserId,
                    at.AddSeconds(3),
                    CafeOrderStates.Ready)),
            "ready C5 financial order");

        Guid completionOperationId =
            Guid.Parse(
                "54000000-0000-0000-0000-000000000014");
        Guid saleOperationId =
            Guid.Parse(
                "54000000-0000-0000-0000-000000000015");
        Guid financialOperationId =
            Guid.Parse(
                "54000000-0000-0000-0000-000000000016");
        Guid financialDecisionId =
            Guid.Parse(
                "54000000-0000-0000-0000-000000000017");
        Guid feeId =
            Guid.Parse(
                "54000000-0000-0000-0000-000000000018");

        CafeOrderCompletionMutation mutation =
            new(
                completionOperationId,
                saleOperationId,
                DeviceId,
                OrderId,
                StoreId,
                4,
                ActorUserId,
                at.AddSeconds(4),
                new[]
                {
                    new CafeOrderPaymentMutation(
                        PaymentMethod.Cash,
                        350),
                    new CafeOrderPaymentMutation(
                        PaymentMethod.AccountsReceivable,
                        500)
                })
            {
                FinancialDecisionOperationId =
                    financialOperationId,
                FinancialDecisionId =
                    financialDecisionId,
                CustomerId =
                    CustomerId,
                ReceivableDueDate =
                    new DateOnly(2026, 10, 22),
                ReceivableNote =
                    "C5 proof receivable",
                LineDiscounts =
                    new[]
                    {
                        new CafeOrderLineDiscountMutation(
                            OrderLineId,
                            100)
                    },
                OrderDiscountCents =
                    100,
                DiscountReason =
                    "C5 proof promo",
                Fees =
                    new[]
                    {
                        new CafeSaleFeeMutation(
                            feeId,
                            CafeSaleFeeTypes.Service,
                            "Service fee",
                            50,
                            "C5 proof service")
                    },
                TaxCents =
                    0
            };

        return (factory, mutation);
    }

    private static void SeedAuthority(
        SqliteConnectionFactory factory,
        DateTimeOffset at)
    {
        Execute(
            factory,
            """
            UPDATE deployment_identity
            SET store_id=$store,
                updated_at=$at;
            """,
            ("$store", StoreId.ToString("D")),
            ("$at", at.ToString("O")));

        string adminRole =
            ScalarString(
                factory,
                """
                SELECT id
                FROM roles
                WHERE name='Admin'
                LIMIT 1;
                """);

        string unitId =
            ScalarString(
                factory,
                """
                SELECT id
                FROM units
                WHERE name='Piece'
                LIMIT 1;
                """);

        Execute(
            factory,
            """
            INSERT INTO users(
                id,username,display_name,password_hash,
                role_id,status,created_at,updated_at)
            VALUES(
                $id,'c5-financial-admin','C5 Financial Admin',
                'proof-hash',$role,1,$at,NULL);
            """,
            ("$id", ActorUserId.ToString("D")),
            ("$role", adminRole),
            ("$at", at.ToString("O")));

        Execute(
            factory,
            """
            INSERT INTO cashier_sessions(
                id,user_id,opened_at,closed_at,
                opening_cash_cents,expected_cash_cents,
                counted_cash_cents,variance_cents,status,notes)
            VALUES(
                $id,$user,$at,NULL,
                100000,100000,NULL,0,1,'C5 financial proof');
            """,
            ("$id", CashierSessionId.ToString("D")),
            ("$user", ActorUserId.ToString("D")),
            ("$at", at.ToString("O")));

        Execute(
            factory,
            """
            INSERT INTO customers(
                id,full_name,phone,address,note,
                credit_limit_cents,is_active,
                created_by_user_id,created_at,updated_at)
            VALUES(
                $id,'C5 Financial Customer',NULL,NULL,NULL,
                100000,1,$user,$at,$at);
            """,
            ("$id", CustomerId.ToString("D")),
            ("$user", ActorUserId.ToString("D")),
            ("$at", at.ToString("O")));

        Execute(
            factory,
            """
            INSERT INTO products(
                id,sku,barcode,name,description,
                category_id,brand_id,unit_id,supplier_id,
                cost_price_cents,selling_price_cents,
                wholesale_price_cents,stock_on_hand_x1000,
                reorder_level_x1000,track_stock,allow_discount,
                is_active,notes,created_at,updated_at)
            VALUES(
                $id,'C5-FIN',NULL,'C5 Financial Product',NULL,
                NULL,NULL,$unit,NULL,
                400,1000,NULL,10000,
                1000,1,1,1,NULL,$at,$at);
            """,
            ("$id", ProductId.ToString("D")),
            ("$unit", unitId),
            ("$at", at.ToString("O")));

        Execute(
            factory,
            """
            INSERT INTO menu_revisions(
                revision_id,store_id,revision_number,
                content_hash,publication_hash,status,
                created_at_utc,published_at_utc,
                superseded_at_utc)
            VALUES(
                $revision,$store,1,
                'c5-financial-content',
                'c5-financial-publication',
                'Published',$at,$at,NULL);
            """,
            ("$revision", MenuRevisionId),
            ("$store", StoreId.ToString("D")),
            ("$at", at.ToString("O")));

        Execute(
            factory,
            """
            INSERT INTO menu_revision_items(
                revision_id,product_id,category_id,
                name,description,unit_id,unit_name,
                sku,barcode,selling_price_cents,
                cost_price_cents,is_temporarily_unavailable,
                display_order)
            VALUES(
                $revision,$product,NULL,
                'C5 Financial Product',NULL,$unit,'Piece',
                'C5-FIN',NULL,1000,
                400,0,0);
            """,
            ("$revision", MenuRevisionId),
            ("$product", ProductId.ToString("D")),
            ("$unit", unitId));

        Execute(
            factory,
            """
            INSERT INTO menu_current_publication(
                store_id,revision_id,content_hash,
                publication_hash,applied_at_utc)
            VALUES(
                $store,$revision,
                'c5-financial-content',
                'c5-financial-publication',$at);
            """,
            ("$store", StoreId.ToString("D")),
            ("$revision", MenuRevisionId),
            ("$at", at.ToString("O")));
    }

    private static long ScalarLong(
        SqliteConnectionFactory factory,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        using SqliteConnection connection =
            factory.CreateConnection();
        connection.Open();
        using SqliteCommand command =
            connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters)
            command.Parameters.AddWithValue(
                name,
                value ?? DBNull.Value);
        return Convert.ToInt64(
            command.ExecuteScalar() ?? 0L);
    }

    private static string ScalarString(
        SqliteConnectionFactory factory,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        using SqliteConnection connection =
            factory.CreateConnection();
        connection.Open();
        using SqliteCommand command =
            connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters)
            command.Parameters.AddWithValue(
                name,
                value ?? DBNull.Value);

        object? scalar =
            command.ExecuteScalar();

        return scalar is null or DBNull
            ? throw new InvalidOperationException(
                "Expected scalar text value.")
            : Convert.ToString(scalar)
              ?? throw new InvalidOperationException(
                  "Expected scalar text value.");
    }

    private static void Execute(
        SqliteConnectionFactory factory,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        using SqliteConnection connection =
            factory.CreateConnection();
        connection.Open();
        using SqliteCommand command =
            connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters)
            command.Parameters.AddWithValue(
                name,
                value ?? DBNull.Value);
        command.ExecuteNonQuery();
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
}

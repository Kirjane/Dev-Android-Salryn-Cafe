using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Seeding;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tools.CafeReportingProof;

internal sealed class C11ProofFixture : IDisposable
{
    public static readonly DateTimeOffset Day =
        DateTimeOffset.Parse("2026-09-24T00:00:00Z");

    private C11ProofFixture(
        string root,
        DbPaths paths,
        SqliteConnectionFactory factory,
        Guid storeId,
        Guid managerUserId,
        Guid cashierUserId,
        Guid terminalId,
        Guid sessionId,
        Guid supplierId,
        Guid productId,
        Guid ingredientId)
    {
        Root = root;
        Paths = paths;
        Factory = factory;
        StoreId = storeId;
        ManagerUserId = managerUserId;
        CashierUserId = cashierUserId;
        TerminalId = terminalId;
        SessionId = sessionId;
        SupplierId = supplierId;
        ProductId = productId;
        IngredientId = ingredientId;
        Service = new SqliteCafeManagementReportingService(factory);
    }

    public string Root { get; }
    public DbPaths Paths { get; }
    public SqliteConnectionFactory Factory { get; }
    public SqliteCafeManagementReportingService Service { get; }
    public Guid StoreId { get; }
    public Guid ManagerUserId { get; }
    public Guid CashierUserId { get; }
    public Guid TerminalId { get; }
    public Guid SessionId { get; }
    public Guid SupplierId { get; }
    public Guid ProductId { get; }
    public Guid IngredientId { get; }

    public static C11ProofFixture Create(string root)
    {
        Directory.CreateDirectory(root);

        DbPaths paths = DbPaths.CreateAtRoot(root);
        Result<DatabaseInitializationSummary> initialized =
            DefaultDatabaseInitializationFactory.Create(paths)
                .InitializeDatabase();

        if (initialized.IsFailure)
            throw new InvalidOperationException(
                "C11 fixture initialization failed: " +
                initialized.ErrorDetail.Code + " / " +
                initialized.ErrorDetail.Message);

        SqliteConnectionFactory factory = new(paths);

        Guid storeId = Guid.Parse(
            ScalarString(
                factory,
                "SELECT store_id FROM deployment_identity ORDER BY created_at LIMIT 1;")
            ?? throw new InvalidOperationException("Deployment StoreId missing."));

        Guid terminalId = Guid.Parse(
            ScalarString(
                factory,
                """
                SELECT terminal_id
                FROM salryn_terminal_registrations
                WHERE store_id=$store
                  AND terminal_status=2
                  AND revoked_at_utc IS NULL
                ORDER BY registered_at_utc,terminal_id
                LIMIT 1;
                """,
                ("$store", storeId.ToString("D")))
            ?? throw new InvalidOperationException("Active terminal missing."));

        Guid manager = Id("manager");
        Guid cashier = Id("cashier");
        Guid session = Id("session");
        Guid supplier = Id("supplier");
        Guid product = Id("product");
        Guid ingredient = Id("ingredient");

        Execute(
            factory,
            """
            INSERT INTO users(
                id,username,display_name,password_hash,
                role_id,status,created_at,updated_at)
            VALUES(
                $manager,'c11_manager','C11 Manager','!C11!',
                $managerRole,1,$at,NULL),
                (
                $cashier,'c11_cashier','C11 Cashier','!C11!',
                $cashierRole,1,$at,NULL);

            INSERT INTO suppliers(
                id,name,contact_number,notes,is_active)
            VALUES(
                $supplier,'C11 Supplier',NULL,'C11 proof',1);

            INSERT INTO products(
                id,sku,barcode,name,description,category_id,brand_id,
                unit_id,supplier_id,cost_price_cents,selling_price_cents,
                wholesale_price_cents,stock_on_hand_x1000,reorder_level_x1000,
                track_stock,allow_discount,is_active,notes,created_at,updated_at)
            VALUES(
                $product,'C11-PRODUCT',NULL,'C11 Product',NULL,NULL,NULL,
                $unit,$supplier,99999,1000,NULL,0,0,
                0,1,1,'Current Product.CostPrice is deliberately irrelevant',
                $at,NULL);

            INSERT INTO ingredients(
                id,name,stock_unit,stock_on_hand_x1000,reorder_level_x1000,
                is_active,notes,created_at_utc,updated_at_utc)
            VALUES(
                $ingredient,'C11 Ingredient','Gram',8000,0,
                1,'C11 proof ingredient',$at,NULL);

            INSERT INTO cashier_sessions(
                id,user_id,opened_at,closed_at,opening_cash_cents,
                expected_cash_cents,counted_cash_cents,variance_cents,
                status,notes)
            VALUES(
                $session,$manager,$at,NULL,0,0,NULL,0,1,'C11 proof open session');
            """,
            ("$manager", manager.ToString("D")),
            ("$cashier", cashier.ToString("D")),
            ("$managerRole", DefaultSeedData.ManagerRoleId),
            ("$cashierRole", DefaultSeedData.CashierRoleId),
            ("$supplier", supplier.ToString("D")),
            ("$product", product.ToString("D")),
            ("$ingredient", ingredient.ToString("D")),
            ("$unit", "00000000-0000-0000-0000-000000000101"),
            ("$session", session.ToString("D")),
            ("$at", Day.ToString("O")));

        return new(
            root,
            paths,
            factory,
            storeId,
            manager,
            cashier,
            terminalId,
            session,
            supplier,
            product,
            ingredient);
    }

    public CafeManagementReport ReadReport(
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        DateTimeOffset? generated = null,
        Guid? actor = null)
    {
        Result<CafeManagementReport> result =
            Service.GetReport(
                new(
                    StoreId,
                    actor ?? ManagerUserId,
                    start ?? Day.AddHours(8),
                    end ?? Day.AddHours(20),
                    generated ?? Day.AddHours(21)));

        if (result.IsFailure)
            throw new InvalidOperationException(
                "C11 report failed: " +
                result.ErrorDetail.Code + " / " +
                result.ErrorDetail.Message);

        return result.Value;
    }

    public void SeedSyncComplete()
    {
        string now = Day.AddHours(7).ToString("O");
        Guid central = Id("central-instance");

        Execute(
            Factory,
            """
            INSERT OR REPLACE INTO sync_device_state(
                device_id,next_local_sequence,created_at_utc,updated_at_utc)
            VALUES($device,1,$at,$at);

            INSERT OR REPLACE INTO sync_remote_state(
                store_id,central_instance_id,last_received_server_sequence,
                last_applied_server_sequence,state,last_error,
                created_at_utc,updated_at_utc)
            VALUES($store,$central,0,0,'Ready',NULL,$at,$at);
            """,
            ("$device", TerminalId.ToString("D")),
            ("$store", StoreId.ToString("D")),
            ("$central", central.ToString("D")),
            ("$at", now));
    }

    public void SeedAcquisition(
        string key,
        long quantityX1000,
        long costCents,
        DateTimeOffset at,
        string? correctionOfKey = null)
    {
        Guid receipt = Id("receipt-" + key);
        Guid line = Id("receipt-line-" + key);
        Guid operation = Id("receipt-operation-" + key);
        Guid movement = Id("receipt-movement-" + key);
        object correction =
            correctionOfKey is null
                ? DBNull.Value
                : Id("receipt-" + correctionOfKey).ToString("D");

        Execute(
            Factory,
            """
            INSERT INTO purchase_receipts(
                purchase_receipt_id,operation_id,store_id,supplier_id,
                supplier_name_snapshot,external_reference,received_at_utc,
                actor_user_id,notes,total_acquisition_cost_cents,content_hash,
                correction_of_purchase_receipt_id,created_at_utc)
            VALUES(
                $receipt,$operation,$store,$supplier,
                'C11 Supplier',$reference,$at,
                $actor,'C11 acquisition proof',$cost,$hash,
                $correction,$at);

            INSERT INTO purchase_receipt_lines(
                line_id,purchase_receipt_id,stock_target_type,stock_target_id,
                name_snapshot,stock_unit_snapshot,quantity_received_x1000,
                purchase_package_label,purchase_package_quantity_x1000,
                purchase_package_content_x1000,line_acquisition_cost_cents)
            VALUES(
                $line,$receipt,'Ingredient',$ingredient,
                'C11 Ingredient','Gram',$quantity,
                NULL,NULL,NULL,$cost);

            INSERT INTO ingredient_stock_movements(
                id,ingredient_id,sale_id,sale_item_id,
                consumption_line_id,original_consumption_line_id,
                reference_type,reference_id,reference_line_id,
                correction_of_reference_id,movement_kind,
                quantity_delta_x1000,actor_user_id,created_at_utc)
            VALUES(
                $movement,$ingredient,NULL,NULL,
                NULL,NULL,'PurchaseReceipt',$receipt,$line,
                $correction,$kind,$quantity,$actor,$at);
            """,
            ("$receipt", receipt.ToString("D")),
            ("$operation", operation.ToString("D")),
            ("$store", StoreId.ToString("D")),
            ("$supplier", SupplierId.ToString("D")),
            ("$reference", "C11-" + key),
            ("$actor", ManagerUserId.ToString("D")),
            ("$cost", costCents),
            ("$hash", Hash(key + "|receipt")),
            ("$correction", correction),
            ("$line", line.ToString("D")),
            ("$ingredient", IngredientId.ToString("D")),
            ("$quantity", quantityX1000),
            ("$movement", movement.ToString("D")),
            ("$kind", correctionOfKey is null ? "PurchaseReceiving" : "PurchaseCorrection"),
            ("$at", at.ToUniversalTime().ToString("O")));
    }

    public Guid SeedPublishedRecipe(
        string key,
        long quantityX1000,
        long yieldX1000 = 1_000)
    {
        if (quantityX1000 <= 0 || yieldX1000 <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantityX1000));

        Guid revision = Id("recipe-book-" + key);
        Guid recipe = Id("recipe-" + key);
        Guid recipeRevision = Id("recipe-revision-" + key);
        string at = Day.AddHours(16).ToString("O");

        Execute(
            Factory,
            """
            INSERT INTO recipe_book_revisions(
                revision_id,store_id,revision_number,content_hash,
                publication_hash,status,created_at_utc,published_at_utc,
                superseded_at_utc)
            VALUES(
                $revision,$store,91,$content,$publication,
                'Published',$at,$at,NULL);

            INSERT INTO recipe_book_revision_recipes(
                revision_id,recipe_id,recipe_revision_id,name,
                yield_x1000,display_order)
            VALUES(
                $revision,$recipe,$recipeRevision,'C11 Current Recipe',
                $yield,0);

            INSERT INTO recipe_book_revision_lines(
                revision_id,recipe_revision_id,recipe_id,ingredient_id,
                stock_unit,normalized_quantity_x1000,display_order)
            VALUES(
                $revision,$recipeRevision,$recipe,$ingredient,
                'Gram',$quantity,0);

            INSERT OR REPLACE INTO recipe_book_current_publication(
                store_id,revision_id,content_hash,publication_hash,applied_at_utc)
            VALUES(
                $store,$revision,$content,$publication,$at);
            """,
            ("$revision", revision.ToString("D")),
            ("$store", StoreId.ToString("D")),
            ("$content", Hash(key + "|recipe-content")),
            ("$publication", Hash(key + "|recipe-publication")),
            ("$at", at),
            ("$recipe", recipe.ToString("D")),
            ("$recipeRevision", recipeRevision.ToString("D")),
            ("$yield", yieldX1000),
            ("$ingredient", IngredientId.ToString("D")),
            ("$quantity", quantityX1000));

        return recipe;
    }

    public Guid SeedSale(
        string key,
        long revenueCents,
        DateTimeOffset at,
        bool appliedRecipe,
        long consumptionX1000 = 0,
        bool retainConsumptionLine = true,
        long feeCents = 0)
    {
        if (feeCents < 0 || feeCents > revenueCents)
            throw new ArgumentOutOfRangeException(nameof(feeCents));

        long grossSubtotalCents =
            checked(revenueCents - feeCents);

        Guid sale = Id("sale-" + key);
        Guid item = Id("sale-item-" + key);
        Guid financial = Id("financial-" + key);
        Guid operation = Id("sale-operation-" + key);

        Execute(
            Factory,
            """
            INSERT INTO sales(
                id,receipt_no,sale_date,cashier_user_id,cashier_session_id,
                subtotal_cents,discount_cents,tax_cents,total_cents,
                amount_paid_cents,change_cents,status,voided_by_user_id,
                void_reason,voided_at,receipt_snapshot,created_at,
                customer_id,payment_status,balance_due_cents,
                receivable_due_date,receivable_note,fee_total_cents)
            VALUES(
                $sale,$receipt,$at,$actor,$session,
                $gross,0,0,$revenue,
                $revenue,0,$completed,NULL,
                NULL,NULL,NULL,$at,
                NULL,'Paid',0,NULL,NULL,$fee);

            INSERT INTO sale_items(
                id,sale_id,product_id,product_name_snapshot,barcode_snapshot,
                unit_name_snapshot,qty_x1000,unit_price_cents,cost_price_cents,
                discount_cents,line_total_cents)
            VALUES(
                $item,$sale,$product,'C11 Product',NULL,
                'Piece',1000,$gross,99999,0,$gross);

            INSERT INTO payments(
                id,sale_id,method,amount_cents,paid_at)
            VALUES(
                $payment,$sale,$cashMethod,$revenue,$at);

            INSERT INTO cafe_sale_financial_decisions(
                financial_decision_id,operation_id,store_id,
                order_id,order_revision_id,expected_order_version,
                sale_id,cashier_session_id,actor_user_id,
                gross_subtotal_cents,line_discount_total_cents,
                order_discount_total_cents,fee_total_cents,tax_cents,
                net_sale_total_cents,cash_tender_cents,
                accounts_receivable_tender_cents,booking_deposit_applied_cents,
                amount_paid_cents,balance_due_cents,change_cents,
                discount_reason,content_hash,created_at_utc)
            VALUES(
                $financial,$operation,$store,
                NULL,NULL,NULL,
                $sale,$session,$actor,
                $gross,0,0,$fee,0,
                $revenue,$revenue,0,0,
                $revenue,0,0,
                NULL,$hash,$at);

            INSERT INTO sale_ingredient_consumption_decisions(
                sale_id,source_sale_operation_id,recipe_book_revision_id,
                decision_hash,result,line_count,created_at_utc)
            VALUES(
                $sale,$operation,NULL,
                $decisionHash,$result,$lineCount,$at);
            """,
            ("$sale", sale.ToString("D")),
            ("$receipt", "C11-" + key),
            ("$at", at.ToUniversalTime().ToString("O")),
            ("$actor", ManagerUserId.ToString("D")),
            ("$session", SessionId.ToString("D")),
            ("$gross", grossSubtotalCents),
            ("$revenue", revenueCents),
            ("$fee", feeCents),
            ("$completed", Convert.ToInt32(SaleStatus.Completed)),
            ("$item", item.ToString("D")),
            ("$product", ProductId.ToString("D")),
            ("$payment", Id("payment-" + key).ToString("D")),
            ("$cashMethod", Convert.ToInt32(PaymentMethod.Cash)),
            ("$financial", financial.ToString("D")),
            ("$operation", operation.ToString("D")),
            ("$store", StoreId.ToString("D")),
            ("$hash", Hash(key + "|financial")),
            ("$decisionHash", Hash(key + "|consumption")),
            ("$result", appliedRecipe ? "APPLIED" : "NO_RECIPE_APPLIED"),
            ("$lineCount", appliedRecipe ? 1 : 0));

        if (feeCents > 0)
        {
            Execute(
                Factory,
                """
                INSERT INTO cafe_sale_fees(
                    fee_id,financial_decision_id,sale_id,fee_type,
                    display_label_snapshot,amount_cents,actor_user_id,
                    reason,created_at_utc)
                VALUES(
                    $feeId,$financial,$sale,'Delivery',
                    'Delivery fee',$amount,$actor,
                    'C11 delivery reporting proof',$at);
                """,
                ("$feeId", Id("delivery-fee-" + key).ToString("D")),
                ("$financial", financial.ToString("D")),
                ("$sale", sale.ToString("D")),
                ("$amount", feeCents),
                ("$actor", ManagerUserId.ToString("D")),
                ("$at", at.ToUniversalTime().ToString("O")));
        }

        if (appliedRecipe)
        {
            if (consumptionX1000 <= 0)
                throw new ArgumentOutOfRangeException(nameof(consumptionX1000));

            if (!retainConsumptionLine)
                return sale;

            Guid line = Id("consumption-line-" + key);

            Execute(
                Factory,
                """
                INSERT INTO sale_ingredient_consumption_lines(
                    id,sale_id,sale_item_id,ingredient_id,
                    recipe_id,recipe_revision_id,source_binding_type,
                    source_binding_id,quantity_consumed_x1000,stock_unit)
                VALUES(
                    $line,$sale,$item,$ingredient,
                    $recipe,$recipeRevision,'Item',
                    $product,$quantity,'Gram');

                INSERT INTO ingredient_stock_movements(
                    id,ingredient_id,sale_id,sale_item_id,
                    consumption_line_id,original_consumption_line_id,
                    reference_type,reference_id,reference_line_id,
                    correction_of_reference_id,movement_kind,
                    quantity_delta_x1000,actor_user_id,created_at_utc)
                VALUES(
                    $movement,$ingredient,$sale,$item,
                    $line,NULL,'Sale',$sale,$line,
                    NULL,'RecipeConsumption',$delta,$actor,$at);
                """,
                ("$line", line.ToString("D")),
                ("$sale", sale.ToString("D")),
                ("$item", item.ToString("D")),
                ("$ingredient", IngredientId.ToString("D")),
                ("$recipe", Id("recipe").ToString("D")),
                ("$recipeRevision", Id("recipe-revision").ToString("D")),
                ("$product", ProductId.ToString("D")),
                ("$quantity", consumptionX1000),
                ("$movement", Id("consumption-movement-" + key).ToString("D")),
                ("$delta", -consumptionX1000),
                ("$actor", ManagerUserId.ToString("D")),
                ("$at", at.ToUniversalTime().ToString("O")));
        }

        return sale;
    }

    public void SeedCommittedRefund(
        string key,
        Guid saleId,
        long amountCents,
        DateTimeOffset at)
    {
        Execute(
            Factory,
            """
            INSERT INTO cafe_sale_refunds(
                refund_id,sale_id,store_id,current_version,state,reason,
                requested_by_user_id,requested_at_utc,request_hash,
                resolved_by_user_id,resolved_at_utc,resolution_reason,
                committed_by_user_id,committed_at_utc,total_refund_cents)
            VALUES(
                $refund,$sale,$store,3,'Committed','C11 proof refund',
                $actor,$requested,$requestHash,
                $actor,$resolved,'Approved for proof',
                $actor,$committed,$amount);
            """,
            ("$refund", Id("refund-" + key).ToString("D")),
            ("$sale", saleId.ToString("D")),
            ("$store", StoreId.ToString("D")),
            ("$actor", ManagerUserId.ToString("D")),
            ("$requested", at.AddMinutes(-2).ToUniversalTime().ToString("O")),
            ("$resolved", at.AddMinutes(-1).ToUniversalTime().ToString("O")),
            ("$committed", at.ToUniversalTime().ToString("O")),
            ("$amount", amountCents),
            ("$requestHash", Hash(key + "|refund")));

        Execute(
            Factory,
            """
            INSERT INTO cafe_sale_refund_lines(
                refund_line_id,refund_id,sale_item_id,product_id,
                product_name_snapshot,original_quantity_x1000,
                refund_quantity_x1000,frozen_unit_price_cents,
                frozen_discount_cents,refund_amount_cents,
                stock_disposition,operator_note)
            VALUES(
                $line,$refund,$saleItem,$product,
                'C11 Product',1000,
                200,$unitPrice,
                0,$amount,
                'ServiceNoStock','C11 reporting refund boundary proof');
            """,
            ("$line", Id("refund-line-" + key).ToString("D")),
            ("$refund", Id("refund-" + key).ToString("D")),
            ("$saleItem", Id("sale-item-" + key).ToString("D")),
            ("$product", ProductId.ToString("D")),
            ("$unitPrice", 1_000L),
            ("$amount", amountCents));
    }

    public void SeedCashMovement(
        string key,
        CashMovementType type,
        long deltaCents,
        DateTimeOffset at)
    {
        Execute(
            Factory,
            """
            INSERT INTO cash_movements(
                id,cashier_session_id,actor_user_id,movement_type,
                delta_cents,reference_type,reference_id,
                reversal_of_movement_id,reason,created_at_utc)
            VALUES(
                $id,$session,$actor,$type,
                $delta,$referenceType,$referenceId,
                NULL,'C11 proof',$at);
            """,
            ("$id", Id("cash-" + key).ToString("D")),
            ("$session", SessionId.ToString("D")),
            ("$actor", ManagerUserId.ToString("D")),
            ("$type", Convert.ToInt32(type)),
            ("$delta", deltaCents),
            ("$referenceType", "C11_" + key.ToUpperInvariant()),
            ("$referenceId", Id("cash-ref-" + key).ToString("D")),
            ("$at", at.ToUniversalTime().ToString("O")));
    }

    public void SeedPerformanceSnapshots(
        string saleKey,
        Guid saleId,
        DateTimeOffset at)
    {
        Guid menuRevision = Id("performance-menu-" + saleKey);
        Guid category = Id("performance-category-" + saleKey);
        Guid variant = Id("performance-variant-" + saleKey);
        Guid order = Id("performance-order-" + saleKey);
        Guid orderRevision = Id("performance-order-revision-" + saleKey);
        Guid orderLine = Id("performance-order-line-" + saleKey);
        string group = Id("performance-modifier-group-" + saleKey).ToString("D");
        string option = Id("performance-modifier-option-" + saleKey).ToString("D");
        string unit =
            "00000000-0000-0000-0000-000000000101";

        Execute(
            Factory,
            """
            INSERT INTO menu_revisions(
                revision_id,store_id,revision_number,content_hash,
                publication_hash,status,created_at_utc,published_at_utc,
                superseded_at_utc)
            VALUES(
                $menu,$store,92,$menuHash,$publicationHash,
                'Published',$at,$at,NULL);

            INSERT INTO menu_revision_categories(
                revision_id,category_id,name,display_order)
            VALUES(
                $menu,$category,'C11 Snapshot Category',0);

            INSERT INTO menu_revision_items(
                revision_id,product_id,category_id,name,description,
                unit_id,unit_name,sku,barcode,selling_price_cents,
                cost_price_cents,is_temporarily_unavailable,display_order)
            VALUES(
                $menu,$product,$category,'C11 Snapshot Product',NULL,
                $unit,'Piece','C11-SNAPSHOT',NULL,1000,
                NULL,0,0);

            INSERT INTO menu_revision_variants(
                revision_id,variant_id,product_id,name,sku,barcode,
                selling_price_cents,cost_price_cents,
                is_temporarily_unavailable,display_order)
            VALUES(
                $menu,$variant,$product,'C11 Snapshot Large',
                NULL,NULL,1000,NULL,0,0);

            INSERT INTO menu_revision_modifier_groups(
                revision_id,group_id,name,is_required,
                min_selections,max_selections,display_order)
            VALUES(
                $menu,$group,'C11 Snapshot Extras',0,0,1,0);

            INSERT INTO menu_revision_modifier_options(
                revision_id,option_id,group_id,name,price_delta_cents,display_order)
            VALUES(
                $menu,$option,$group,'C11 Snapshot Extra Shot',50,0);

            INSERT INTO cafe_orders(
                order_id,store_id,current_version,current_revision_id,
                menu_publication_revision_id,state,service_mode,
                service_reference,created_by_user_id,created_at_utc,
                updated_at_utc,submitted_revision_id,
                submitted_recipe_book_revision_id,completed_sale_id,
                cancellation_reason)
            VALUES(
                $order,$store,1,$orderRevision,
                $menu,'Completed','DineIn',
                NULL,$actor,$at,
                $at,$orderRevision,
                NULL,$sale,NULL);

            INSERT INTO cafe_order_revisions(
                revision_id,order_id,revision_number,order_version,
                previous_revision_id,menu_publication_revision_id,
                service_mode,service_reference,content_hash,
                actor_user_id,created_at_utc)
            VALUES(
                $orderRevision,$order,1,1,
                NULL,$menu,
                'DineIn',NULL,$orderHash,
                $actor,$at);

            INSERT INTO cafe_order_revision_lines(
                revision_id,order_line_id,product_id,
                product_name_snapshot,unit_name_snapshot,
                quantity_x1000,base_unit_price_cents,
                variant_id,variant_name_snapshot,variant_unit_price_cents,
                note,calculated_unit_price_cents,line_total_cents,
                display_order)
            VALUES(
                $orderRevision,$orderLine,$product,
                'C11 Snapshot Product','Piece',
                1000,950,
                $variant,'C11 Snapshot Large',1000,
                NULL,1050,1050,
                0);

            INSERT INTO cafe_order_revision_modifiers(
                revision_id,order_line_id,group_id,group_name_snapshot,
                option_id,option_name_snapshot,price_delta_cents,display_order)
            VALUES(
                $orderRevision,$orderLine,$group,'C11 Snapshot Extras',
                $option,'C11 Snapshot Extra Shot',50,0);

            INSERT INTO sale_item_order_links(
                sale_item_id,order_id,order_revision_id,order_line_id)
            VALUES(
                $saleItem,$order,$orderRevision,$orderLine);
            """,
            ("$menu", menuRevision.ToString("D")),
            ("$store", StoreId.ToString("D")),
            ("$menuHash", Hash(saleKey + "|menu")),
            ("$publicationHash", Hash(saleKey + "|menu-publication")),
            ("$at", at.ToUniversalTime().ToString("O")),
            ("$category", category.ToString("D")),
            ("$product", ProductId.ToString("D")),
            ("$unit", unit),
            ("$variant", variant.ToString("D")),
            ("$group", group),
            ("$option", option),
            ("$order", order.ToString("D")),
            ("$orderRevision", orderRevision.ToString("D")),
            ("$actor", ManagerUserId.ToString("D")),
            ("$sale", saleId.ToString("D")),
            ("$orderHash", Hash(saleKey + "|order")),
            ("$orderLine", orderLine.ToString("D")),
            ("$saleItem", Id("sale-item-" + saleKey).ToString("D")));
    }

    public void SeedCashierSessionReport(
        DateTimeOffset closedAt)
    {
        Execute(
            Factory,
            """
            INSERT INTO cashier_session_reports(
                session_id,cashier_user_id,opened_at_utc,closed_at_utc,
                opening_cash_cents,gross_sales_cents,discount_total_cents,
                net_sales_cents,cash_sales_cents,noncash_sales_cents,
                transaction_count,expected_closing_cash_cents,
                declared_closing_cash_cents,variance_cents,close_status,
                close_notes,snapshot_version,snapshot_hash,created_at_utc,
                reconciliation_basis,ledger_net_delta_cents)
            VALUES(
                $session,$cashier,$opened,$closed,
                0,1000,200,
                800,800,0,
                1,800,
                800,0,2,
                'C11 immutable session report proof',2,$hash,$closed,
                'LedgerAuthoritative',0);
            """,
            ("$session", SessionId.ToString("D")),
            ("$cashier", ManagerUserId.ToString("D")),
            ("$opened", Day.AddHours(8).ToString("O")),
            ("$closed", closedAt.ToUniversalTime().ToString("O")),
            ("$hash", Hash("c11-session-report")));
    }

    public void SeedConsumptionReversal(
        string saleKey,
        Guid saleId,
        long quantityX1000,
        DateTimeOffset at)
    {
        if (quantityX1000 <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantityX1000));

        Guid line = Id("consumption-line-" + saleKey);

        Execute(
            Factory,
            """
            INSERT INTO ingredient_stock_movements(
                id,ingredient_id,sale_id,sale_item_id,
                consumption_line_id,original_consumption_line_id,
                reference_type,reference_id,reference_line_id,
                correction_of_reference_id,movement_kind,
                quantity_delta_x1000,actor_user_id,created_at_utc)
            VALUES(
                $movement,$ingredient,$sale,$item,
                NULL,$line,'SaleReversal',$sale,$line,
                NULL,'RecipeConsumptionReversal',
                $quantity,$actor,$at);
            """,
            ("$movement", Id("consumption-reversal-" + saleKey).ToString("D")),
            ("$ingredient", IngredientId.ToString("D")),
            ("$sale", saleId.ToString("D")),
            ("$item", Id("sale-item-" + saleKey).ToString("D")),
            ("$line", line.ToString("D")),
            ("$quantity", quantityX1000),
            ("$actor", ManagerUserId.ToString("D")),
            ("$at", at.ToUniversalTime().ToString("O")));
    }

    public void SeedWaste(
        string key,
        long quantityX1000,
        DateTimeOffset at)
    {
        Guid waste = Id("waste-" + key);
        Guid line = Id("waste-line-" + key);

        Execute(
            Factory,
            """
            INSERT INTO inventory_waste_events(
                waste_event_id,operation_id,store_id,reason,actor_user_id,
                occurred_at_utc,notes,content_hash,
                correction_of_waste_event_id,created_at_utc)
            VALUES(
                $waste,$operation,$store,'Waste',$actor,
                $at,'C11 proof waste',$hash,NULL,$at);

            INSERT INTO inventory_waste_lines(
                line_id,waste_event_id,stock_target_type,stock_target_id,
                name_snapshot,stock_unit_snapshot,quantity_wasted_x1000)
            VALUES(
                $line,$waste,'Ingredient',$ingredient,
                'C11 Ingredient','Gram',$quantity);

            INSERT INTO ingredient_stock_movements(
                id,ingredient_id,sale_id,sale_item_id,
                consumption_line_id,original_consumption_line_id,
                reference_type,reference_id,reference_line_id,
                correction_of_reference_id,movement_kind,
                quantity_delta_x1000,actor_user_id,created_at_utc)
            VALUES(
                $movement,$ingredient,NULL,NULL,
                NULL,NULL,'InventoryWaste',$waste,$line,
                NULL,'Waste',$delta,$actor,$at);
            """,
            ("$waste", waste.ToString("D")),
            ("$operation", Id("waste-operation-" + key).ToString("D")),
            ("$store", StoreId.ToString("D")),
            ("$actor", ManagerUserId.ToString("D")),
            ("$at", at.ToUniversalTime().ToString("O")),
            ("$hash", Hash(key + "|waste")),
            ("$line", line.ToString("D")),
            ("$ingredient", IngredientId.ToString("D")),
            ("$quantity", quantityX1000),
            ("$movement", Id("waste-movement-" + key).ToString("D")),
            ("$delta", -quantityX1000));
    }

    public void SeedArDebit(
        string key,
        long amountCents,
        DateTimeOffset at)
    {
        Guid customer = Id("customer-" + key);

        Execute(
            Factory,
            """
            INSERT INTO customers(
                id,full_name,phone,address,note,credit_limit_cents,
                is_active,created_by_user_id,created_at,updated_at)
            VALUES(
                $customer,'C11 AR Customer',NULL,NULL,'C11 proof',NULL,
                1,$actor,$at,$at);

            INSERT INTO accounts_receivable_entries(
                id,customer_id,sale_id,collection_id,entry_type,
                debit_cents,credit_cents,balance_after_cents,
                due_date,note,created_by_user_id,created_at,refund_id)
            VALUES(
                $entry,$customer,NULL,NULL,'ManualDebit',
                $amount,0,$amount,
                NULL,'C11 proof receivable',$actor,$at,NULL);
            """,
            ("$customer", customer.ToString("D")),
            ("$actor", ManagerUserId.ToString("D")),
            ("$at", at.ToUniversalTime().ToString("O")),
            ("$entry", Id("ar-entry-" + key).ToString("D")),
            ("$amount", amountCents));
    }

    public void RemoveRemoteState()
    {
        Execute(
            Factory,
            "DELETE FROM sync_remote_state WHERE store_id=$store;",
            ("$store", StoreId.ToString("D")));
    }

    public void SeedPendingUpload()
    {
        Execute(
            Factory,
            """
            INSERT INTO sync_outbox(
                operation_id,device_id,store_id,actor_user_id,
                local_sequence,protocol_version,operation_type,schema_version,
                aggregate_id,base_version,occurred_at_local,
                payload_json,payload_hash,operation_hash,
                delivery_status,attempt_count,last_error,
                central_received_sequence,central_received_at_utc,
                resolution_status,resolution_code,resolution_json,
                resolved_at_utc,created_at_utc)
            VALUES(
                $operation,$device,$store,$actor,
                1,1,'C11ProofOperation',1,
                $aggregate,NULL,$at,
                '{}',$hash,$operationHash,
                'PendingUpload',0,NULL,
                NULL,NULL,
                NULL,NULL,NULL,
                NULL,$at);
            """,
            ("$operation", Id("pending-operation").ToString("D")),
            ("$device", TerminalId.ToString("D")),
            ("$store", StoreId.ToString("D")),
            ("$actor", ManagerUserId.ToString("D")),
            ("$aggregate", Id("pending-aggregate").ToString("D")),
            ("$at", Day.AddHours(18).ToString("O")),
            ("$hash", Hash("pending-payload")),
            ("$operationHash", Hash("pending-operation-hash")));
    }

    public void Execute(
        string sql,
        params (string Name, object? Value)[] parameters) =>
        Execute(Factory, sql, parameters);

    public long ScalarLong(
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        using SqliteConnection connection = Factory.CreateConnection();
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
    }

    public static Guid Id(string scope)
    {
        byte[] bytes =
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(
                    "salryn-c11-reporting-proof|" + scope));

        byte[] id = bytes[..16];
        id[6] = (byte)((id[6] & 0x0f) | 0x50);
        id[8] = (byte)((id[8] & 0x3f) | 0x80);
        return new Guid(id);
    }

    private static string Hash(string text) =>
        Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(text)))
            .ToLowerInvariant();

    private static void Execute(
        SqliteConnectionFactory factory,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();
        SqlitePragma.Apply(connection);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;

        foreach ((string name, object? value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);

        command.ExecuteNonQuery();
    }

    private static string? ScalarString(
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

        object? scalarValue = command.ExecuteScalar();
        return scalarValue is null or DBNull
            ? null
            : Convert.ToString(scalarValue);
    }

    public void Dispose()
    {
        SqliteConnectionFactory.ClearAllPools();
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch
        {
        }
    }
}

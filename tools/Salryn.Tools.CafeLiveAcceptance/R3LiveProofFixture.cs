using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Salryn.Cafe.I6ATests;
using Salryn.Composition.Local;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Infrastructure.Data.Seeding;

internal sealed class R3LiveProofFixture
{
    public const string OwnerUsername = "c4-proof";
    public const string OwnerPassword = "Synthetic-R3-Owner!2026";
    public const string ReportsOnlyUsername = "r3-reports-only";
    public const string ReportsOnlyPassword = "Synthetic-R3-ReportsOnly!2026";
    public const string CashierUsername = "r3-cashier";
    public const string CashierPassword = "Synthetic-R3-Cashier!2026";

    public static readonly Guid ReportsOnlyRoleId = Guid.Parse("40000000-0000-0000-0000-000000000091");
    public static readonly Guid ReportsOnlyUserId = Guid.Parse("40000000-0000-0000-0000-000000000092");
    public static readonly Guid CashierUserId = Guid.Parse("40000000-0000-0000-0000-000000000093");
    public static readonly Guid CustomerId = Guid.Parse("40000000-0000-0000-0000-000000000094");

    public required string Root { get; init; }
    public required C4ProofDatabase Db { get; init; }
    public required Guid SupplierId { get; init; }
    public required Guid CompleteRecipeId { get; init; }
    public required Guid IncompleteRecipeId { get; init; }
    public required DateTimeOffset TodayStartManila { get; init; }
    public required DateTimeOffset WeekStartManila { get; init; }
    public required DateTimeOffset MonthStartManila { get; init; }
    public required bool HasEarlierWeekSale { get; init; }
    public required bool HasEarlierMonthSale { get; init; }
    public required long ExpectedDailyNetRevenueCents { get; init; }
    public required long ExpectedWeeklyNetRevenueCents { get; init; }
    public required long ExpectedMonthlyNetRevenueCents { get; init; }
    public required long ExpectedDailyCompletedRevenueCents { get; init; }
    public required long ExpectedDailyRefundCents { get; init; }

    public static R3LiveProofFixture Create(string root)
    {
        Directory.CreateDirectory(root);
        var db = C4ProofDatabase.Create(root);
        string nowIso = DateTimeOffset.UtcNow.ToString("O");

        // Configure owner password and generous product stock for multi-period sales.
        db.Execute(
            "UPDATE users SET password_hash=$hash WHERE id=$id;",
            ("$hash", SalrynPasswordHasher.HashPassword(OwnerPassword).Value),
            ("$id", C4ProofIds.ActorUserId.ToString("D")));
        db.Execute(
            "UPDATE products SET stock_on_hand_x1000=50000 WHERE id=$id;",
            ("$id", C4ProofIds.ProductId.ToString("D")));

        // Use canonical supported Manager role for r3-reports-only with Receivables.View and Inventory.View revoked
        // so product role boundaries (DefaultSeedData.IsSupportedProductRoleId) remain strictly enforced.
        db.Execute(
            "DELETE FROM role_permissions WHERE role_id=$role AND permission IN ($recView, $invView);",
            ("$role", DefaultSeedData.ManagerRoleId),
            ("$recView", DefaultSeedData.Permissions.ReceivablesView),
            ("$invView", DefaultSeedData.Permissions.InventoryView));
        db.Execute(
            """
            INSERT INTO users(id,username,display_name,password_hash,role_id,status,created_at,updated_at)
            VALUES($id,$user,'R3 Reports Viewer',$hash,$role,1,$at,NULL);
            """,
            ("$id", ReportsOnlyUserId.ToString("D")),
            ("$user", ReportsOnlyUsername),
            ("$hash", SalrynPasswordHasher.HashPassword(ReportsOnlyPassword).Value),
            ("$role", DefaultSeedData.ManagerRoleId),
            ("$at", nowIso));

        // Seed Cashier user (no Reports.View, no Inventory.Adjust/Count).
        db.Execute(
            """
            INSERT INTO users(id,username,display_name,password_hash,role_id,status,created_at,updated_at)
            VALUES($id,$user,'R3 Synthetic Cashier',$hash,$role,1,$at,NULL);
            """,
            ("$id", CashierUserId.ToString("D")),
            ("$user", CashierUsername),
            ("$hash", SalrynPasswordHasher.HashPassword(CashierPassword).Value),
            ("$role", DefaultSeedData.CashierRoleId),
            ("$at", nowIso));

        // Seed synthetic AR customer.
        db.Execute(
            """
            INSERT INTO customers(id,full_name,is_active,created_by_user_id,created_at,updated_at)
            VALUES($id,'Synthetic AR Account',1,$actor,$at,$at);
            """,
            ("$id", CustomerId.ToString("D")),
            ("$actor", C4ProofIds.ActorUserId.ToString("D")),
            ("$at", nowIso));

        // Rename ingredients clearly and place VariantIngredientId below reorder level for Low Stock proof.
        db.Execute(
            "UPDATE ingredients SET name='Espresso Beans', stock_on_hand_x1000=100000, reorder_level_x1000=1000 WHERE id=$id;",
            ("$id", C4ProofIds.ItemIngredientId.ToString("D")));
        db.Execute(
            "UPDATE ingredients SET name='Oat Milk', stock_on_hand_x1000=2000, reorder_level_x1000=5000 WHERE id=$id;",
            ("$id", C4ProofIds.VariantIngredientId.ToString("D")));

        // Seed real GUID-keyed recipes and publish via canonical IngredientRecipes service so C11 recipeCosts populates.
        var comp = SalrynLocalComposition.Create(db.Paths);
        var supplierRes = comp.SupplierDirectory.SaveSupplier(new SupplierMutation(
            null, "Synthetic Roasters", "09170001111", "Synthetic R3 fixture supplier", true));
        if (supplierRes.IsFailure) throw new InvalidOperationException("Seed supplier failed: " + supplierRes.ErrorDetail.Message);

        var rec1 = comp.IngredientRecipes.SaveRecipe(new RecipeMutation(null, "Espresso Base", 1000, true));
        if (rec1.IsFailure) throw new InvalidOperationException("Seed recipe 1 failed: " + rec1.ErrorDetail.Message);
        var rec2 = comp.IngredientRecipes.SaveRecipe(new RecipeMutation(null, "Oat Latte Special", 1000, true));
        if (rec2.IsFailure) throw new InvalidOperationException("Seed recipe 2 failed: " + rec2.ErrorDetail.Message);

        var l1 = comp.IngredientRecipes.SaveRecipeLine(new RecipeLineMutation(
            rec1.Value.Id, C4ProofIds.ItemIngredientId, RecipeInputUnits.Piece, 1000, 0));
        if (l1.IsFailure) throw new InvalidOperationException("Seed recipe 1 line failed: " + l1.ErrorDetail.Message);

        var l2a = comp.IngredientRecipes.SaveRecipeLine(new RecipeLineMutation(
            rec2.Value.Id, C4ProofIds.ItemIngredientId, RecipeInputUnits.Piece, 1000, 0));
        if (l2a.IsFailure) throw new InvalidOperationException("Seed recipe 2 line A failed: " + l2a.ErrorDetail.Message);

        var l2b = comp.IngredientRecipes.SaveRecipeLine(new RecipeLineMutation(
            rec2.Value.Id, C4ProofIds.VariantIngredientId, RecipeInputUnits.Piece, 1000, 1));
        if (l2b.IsFailure) throw new InvalidOperationException("Seed recipe 2 line B failed: " + l2b.ErrorDetail.Message);

        var bind = comp.IngredientRecipes.SetItemRecipe(C4ProofIds.ProductId, rec1.Value.Id);
        if (bind.IsFailure) throw new InvalidOperationException("Bind recipe failed: " + bind.ErrorDetail.Message);

        var pub = comp.IngredientRecipes.PublishCurrent();
        if (pub.IsFailure) throw new InvalidOperationException("Publish recipe book failed: " + pub.ErrorDetail.Message);

        var nowManila = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8));
        var todayStart = new DateTimeOffset(nowManila.Year, nowManila.Month, nowManila.Day, 0, 0, 0, TimeSpan.FromHours(8));
        var weekStart = todayStart.AddDays(-(((int)todayStart.DayOfWeek + 6) % 7));
        var monthStart = new DateTimeOffset(nowManila.Year, nowManila.Month, 1, 0, 0, 0, TimeSpan.FromHours(8));

        bool hasEarlierWeek = weekStart < todayStart && weekStart >= monthStart;
        bool hasEarlierMonth = monthStart < weekStart && monthStart < todayStart;

        const long dailyCompleted = 1000L; // 2 x 500
        const long dailyRefund = 500L;     // 1 x 500 refunded
        const long dailyNet = dailyCompleted - dailyRefund; // 500
        long weeklyNet = dailyNet + (hasEarlierWeek ? 1500L : 0L); // + 3 x 500
        long monthlyNet = weeklyNet + (hasEarlierMonth ? 2500L : 0L); // + 5 x 500

        return new R3LiveProofFixture
        {
            Root = root,
            Db = db,
            SupplierId = supplierRes.Value.Id,
            CompleteRecipeId = Guid.Parse(rec1.Value.Id),
            IncompleteRecipeId = Guid.Parse(rec2.Value.Id),
            TodayStartManila = todayStart,
            WeekStartManila = weekStart,
            MonthStartManila = monthStart,
            HasEarlierWeekSale = hasEarlierWeek,
            HasEarlierMonthSale = hasEarlierMonth,
            ExpectedDailyCompletedRevenueCents = dailyCompleted,
            ExpectedDailyRefundCents = dailyRefund,
            ExpectedDailyNetRevenueCents = dailyNet,
            ExpectedWeeklyNetRevenueCents = weeklyNet,
            ExpectedMonthlyNetRevenueCents = monthlyNet,
        };
    }

    public async Task SeedLiveFinancialHistoryAsync(HttpClient ownerHttp)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var todayAtUtc = nowUtc.AddMinutes(-10);
        if (todayAtUtc <= TodayStartManila.ToUniversalTime())
        {
            todayAtUtc = TodayStartManila.ToUniversalTime().AddSeconds(10);
        }

        // 1. Purchase receipt dated before month start so every sale in month has real historical COGS basis.
        var purchaseAtUtc = MonthStartManila.AddDays(-1).ToUniversalTime();
        Guid purchaseOpId = Guid.NewGuid();
        await PostJsonOkAsync(ownerHttp, "/api/cafe/purchases/receive", purchaseOpId, new
        {
            operationId = purchaseOpId,
            purchaseReceiptId = Guid.NewGuid(),
            supplierId = SupplierId,
            receivedAtUtc = purchaseAtUtc,
            externalReference = "PO-R3-SYNTH-001",
            notes = "Synthetic Espresso Beans cost basis",
            lines = new[]
            {
                new
                {
                    lineId = Guid.NewGuid(),
                    stockTargetType = "Ingredient",
                    stockTargetId = C4ProofIds.ItemIngredientId,
                    quantityReceivedX1000 = 10000L, // 10 units
                    lineAcquisitionCostCents = 2000L // 200 cents per unit
                }
            }
        }, "seed purchase receipt");

        // 2. Earlier-in-month sale (5 x 500 = 2500 cents) when monthStart < weekStart.
        if (HasEarlierMonthSale)
        {
            var earlierMonthAt = MonthStartManila.AddHours(9).ToUniversalTime();
            await CompleteOrderAsync(ownerHttp, 5000L, earlierMonthAt, "R3 Month Sale", new[]
            {
                new { method = "Cash", amountCents = 2500L }
            }, null);
        }

        // 3. Earlier-in-week sale (3 x 500 = 1500 cents) when weekStart < todayStart.
        if (HasEarlierWeekSale)
        {
            var earlierWeekAt = WeekStartManila.AddHours(9).ToUniversalTime();
            await CompleteOrderAsync(ownerHttp, 3000L, earlierWeekAt, "R3 Week Sale", new[]
            {
                new { method = "Cash", amountCents = 1500L }
            }, null);
        }

        // 4. Today sale (2 x 500 = 1000 cents, Partial: 600 Cash + 400 AR).
        var todaySale = await CompleteOrderAsync(ownerHttp, 2000L, todayAtUtc, "R3 Today Partial Sale", new[]
        {
            new { method = "Cash", amountCents = 600L },
            new { method = "AccountsReceivable", amountCents = 400L }
        }, CustomerId);

        Guid todaySaleId = todaySale.GetProperty("saleId").GetGuid();
        Guid todaySaleItemId = Guid.Parse(
            Db.ScalarString("SELECT id FROM sale_items WHERE sale_id=$sale LIMIT 1;", ("$sale", todaySaleId.ToString("D")))
            ?? throw new InvalidOperationException("Expected sale_item for seeded today sale."));

        // 5. Partial Cash Refund on today sale (1 x 500 = 500 cents).
        Guid refundReqOpId = Guid.NewGuid();
        var refundCreate = await PostJsonOkAsync(ownerHttp, "/api/cafe/refunds", refundReqOpId, new
        {
            operationId = refundReqOpId,
            saleId = todaySaleId,
            expectedVersion = 0,
            occurredAtUtc = todayAtUtc.AddSeconds(5),
            reason = "Synthetic R3 partial refund",
            lines = new[]
            {
                new
                {
                    saleItemId = todaySaleItemId,
                    quantityX1000 = 1000L,
                    stockDisposition = "Restock",
                    operatorNote = "Customer returned one cup"
                }
            }
        }, "seed refund request");
        Guid refundId = refundCreate.GetProperty("refundId").GetGuid();
        int refundVer = refundCreate.GetProperty("version").GetInt32();

        Guid refundApproveOpId = Guid.NewGuid();
        var refundApprove = await PostJsonOkAsync(ownerHttp, $"/api/cafe/refunds/{refundId:D}/resolve", refundApproveOpId, new
        {
            operationId = refundApproveOpId,
            saleId = todaySaleId,
            expectedVersion = refundVer,
            occurredAtUtc = todayAtUtc.AddSeconds(10),
            approve = true,
            reason = "Manager approved synthetic refund"
        }, "seed refund approve");
        refundVer = refundApprove.GetProperty("version").GetInt32();

        Guid refundCommitOpId = Guid.NewGuid();
        await PostJsonOkAsync(ownerHttp, $"/api/cafe/refunds/{refundId:D}/commit", refundCommitOpId, new
        {
            operationId = refundCommitOpId,
            saleId = todaySaleId,
            expectedVersion = refundVer,
            occurredAtUtc = todayAtUtc.AddSeconds(15),
            settlements = new[]
            {
                new { method = "Cash", amountCents = 500L }
            }
        }, "seed refund commit");

        // 6. Operational Expense (Today: 150 cents).
        Guid expenseOpId = Guid.NewGuid();
        await PostJsonOkAsync(ownerHttp, "/api/cafe/cash/movements", expenseOpId, new
        {
            operationId = expenseOpId,
            movementId = Guid.NewGuid(),
            movementType = "OperationalExpense",
            amountCents = 150L,
            reason = "Synthetic filter papers",
            occurredAtUtc = todayAtUtc.AddSeconds(20)
        }, "seed operational expense");

        // 7. Waste Event (Today: 1000 x1000 Espresso Beans [costed -> 200 cents] + 500 x1000 Oat Milk [uncosted -> triggers ProvisionalCostIncomplete]).
        Guid wasteOpId = Guid.NewGuid();
        await PostJsonOkAsync(ownerHttp, "/api/cafe/inventory/waste", wasteOpId, new
        {
            operationId = wasteOpId,
            wasteEventId = Guid.NewGuid(),
            occurredAtUtc = todayAtUtc.AddSeconds(25),
            reason = "Spoilage",
            notes = "Synthetic R3 waste proof",
            lines = new[]
            {
                new
                {
                    lineId = Guid.NewGuid(),
                    stockTargetType = "Ingredient",
                    stockTargetId = C4ProofIds.ItemIngredientId,
                    quantityWastedX1000 = 1000L
                },
                new
                {
                    lineId = Guid.NewGuid(),
                    stockTargetType = "Ingredient",
                    stockTargetId = C4ProofIds.VariantIngredientId,
                    quantityWastedX1000 = 500L
                }
            }
        }, "seed waste event");
    }

    private static async Task<JsonElement> CompleteOrderAsync(
        HttpClient http,
        long quantityX1000,
        DateTimeOffset occurredAtUtc,
        string reference,
        object[] payments,
        Guid? customerId)
    {
        Guid orderId = Guid.NewGuid();
        Guid revOpId = Guid.NewGuid();
        var rev = await PostJsonOkAsync(http, "/api/cafe/orders/revision", revOpId, new
        {
            operationId = revOpId,
            orderId,
            expectedVersion = 0,
            serviceMode = "DineIn",
            serviceReference = reference,
            occurredAtUtc,
            lines = new[]
            {
                new
                {
                    orderLineId = Guid.NewGuid(),
                    productId = C4ProofIds.ProductId,
                    variantId = (string?)null,
                    modifierOptionIds = Array.Empty<string>(),
                    quantityX1000,
                    note = (string?)null,
                    displayOrder = 0
                }
            }
        }, "seed order revision " + reference);
        int ver = rev.GetProperty("orderVersion").GetInt32();

        Guid subOpId = Guid.NewGuid();
        var sub = await PostJsonOkAsync(http, $"/api/cafe/orders/{orderId:D}/submit", subOpId, new
        {
            operationId = subOpId,
            expectedVersion = ver,
            occurredAtUtc = occurredAtUtc.AddSeconds(1)
        }, "seed order submit " + reference);
        ver = sub.GetProperty("orderVersion").GetInt32();

        Guid prepOpId = Guid.NewGuid();
        var prep = await PostJsonOkAsync(http, $"/api/cafe/orders/{orderId:D}/preparation", prepOpId, new
        {
            operationId = prepOpId,
            expectedVersion = ver,
            targetState = "Preparing",
            occurredAtUtc = occurredAtUtc.AddSeconds(2)
        }, "seed order preparing " + reference);
        ver = prep.GetProperty("orderVersion").GetInt32();

        Guid readyOpId = Guid.NewGuid();
        var ready = await PostJsonOkAsync(http, $"/api/cafe/orders/{orderId:D}/preparation", readyOpId, new
        {
            operationId = readyOpId,
            expectedVersion = ver,
            targetState = "Ready",
            occurredAtUtc = occurredAtUtc.AddSeconds(3)
        }, "seed order ready " + reference);
        ver = ready.GetProperty("orderVersion").GetInt32();

        Guid completeOpId = Guid.NewGuid();
        return await PostJsonOkAsync(http, $"/api/cafe/orders/{orderId:D}/complete", completeOpId, new
        {
            completionOperationId = completeOpId,
            saleCommittedOperationId = Guid.NewGuid(),
            expectedVersion = ver,
            occurredAtUtc = occurredAtUtc.AddSeconds(4),
            payments,
            fees = Array.Empty<object>(),
            customerId,
            receivableDueDate = customerId.HasValue ? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)) : (DateOnly?)null,
            receivableNote = customerId.HasValue ? "Synthetic R3 AR proof" : null
        }, "seed order complete " + reference);
    }

    public static async Task<JsonElement> PostJsonOkAsync(HttpClient http, string path, Guid operationId, object body, string label)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path);
        req.Headers.Add("Idempotency-Key", operationId.ToString("D"));
        req.Content = JsonContent.Create(body);
        using var resp = await http.SendAsync(req);
        string raw = await resp.Content.ReadAsStringAsync();
        if (resp.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"{label} failed HTTP {(int)resp.StatusCode}: {raw}");
        }
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    public static async Task LoginAsync(HttpClient http, string username, string password)
    {
        using var resp = await http.PostAsJsonAsync("/api/auth/login", new { username, password });
        string raw = await resp.Content.ReadAsStringAsync();
        if (resp.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Login failed for {username}: HTTP {(int)resp.StatusCode} {raw}");
        }
    }

    public static string ReportPath(DateTimeOffset from, DateTimeOffset to) =>
        "/api/owner-reports/summary?fromUtc=" + Uri.EscapeDataString(from.ToString("O"))
        + "&toUtc=" + Uri.EscapeDataString(to.ToString("O"));
}

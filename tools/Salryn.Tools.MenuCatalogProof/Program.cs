using System.Globalization;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Migrations;
using Salryn.Infrastructure.Data.Repositories.Menu;
using Salryn.Infrastructure.Data.Seeding;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;
using Salryn.Mobile.Runtime;

string proofRoot = Path.Combine(
    Path.GetTempPath(),
    "salryn-c1-menu-proof-" + Guid.NewGuid().ToString("N"));

Directory.CreateDirectory(proofRoot);

try
{
    RunMigrationAndDomainProof(Path.Combine(proofRoot, "upgrade"));
    RunAndroidCompositionProof(Path.Combine(proofRoot, "android"));
    Console.WriteLine("PASS C1 Menu / Catalog proof");
}
finally
{
    SqliteConnectionFactory.ClearAllPools();
    TryDeleteDirectory(proofRoot);
}

static void RunMigrationAndDomainProof(string root)
{
    DbPaths paths = DbPaths.CreateAtRoot(root);
    SqliteConnectionFactory factory = new(paths);

    IReadOnlyList<IDatabaseMigration> migrationPlan =
        CafeMigrationCatalog.CreateLegacyYzaV39Migrations()
            .Where(x => x.Version <= 30)
            .ToArray();

    Assert(migrationPlan.Max(x => x.Version) == 30, "C1 migration authority must stop at 30.");
    Assert(migrationPlan.All(x => x.Version != 23), "Migration 23 must remain absent.");
    Assert(migrationPlan.All(x => x.Version <= 30), "Migration 31+ is not authorized.");

    MigrationRunner preC1 = new(
        factory,
        migrationPlan.Where(x => x.Version <= 29));
    preC1.RunPendingMigrations();
    new DatabaseSeeder(factory).SeedDefaultData();

    UpgradeFixture fixture = CreateAcceptedMigration29Fixture(factory);

    Assert(ReadMaxMigration(factory) == 29, "Pre-C1 fixture must be migration 29.");
    Assert(ReadMigrationCount(factory, 23) == 0, "Migration 23 must be absent before upgrade.");

    MigrationRunner c1Upgrade = new(
        factory,
        new IDatabaseMigration[] { new MenuCatalogSchemaMigration() });
    c1Upgrade.RunPendingMigrations();

    Assert(ReadMaxMigration(factory) == 30, "29 -> 30 upgrade must reach migration 30.");
    Assert(ReadMigrationCount(factory, 23) == 0, "Migration 23 must remain absent after upgrade.");
    Assert(ReadMigrationCount(factory, 30) == 1, "Migration 30 must be recorded exactly once.");

    Assert(ScalarLong(factory, "SELECT COUNT(*) FROM products WHERE id = $id;", ("$id", fixture.ProductId)) == 1,
        "Existing product must survive migration 30.");
    Assert(ScalarLong(factory, "SELECT COUNT(*) FROM categories WHERE id = $id;", ("$id", fixture.CategoryId)) == 1,
        "Existing category must survive migration 30.");
    Assert(ScalarLong(factory, "SELECT COUNT(*) FROM menu_variants WHERE product_id = $id AND name = 'Standard';", ("$id", fixture.ProductId)) == 1,
        "Migration 30 must backfill one Standard variant for an existing product.");

    HistoricalSnapshot before = ReadHistoricalSnapshot(factory, fixture.SaleId);

    SqliteMenuCatalogStore menu = new(factory);
    MenuCatalogDraft initialDraft = RequireValue(menu.GetDraft(), "read migration-30 draft");

    Assert(initialDraft.Items.Any(x => x.ProductId == Guid.Parse(fixture.ProductId)),
        "Existing product must map into C1 menu draft.");

    MenuItemConfig existing = initialDraft.Items.Single(x => x.ProductId == Guid.Parse(fixture.ProductId));

    RequireValue(menu.SaveItem(new MenuItemMutation(
        existing.ProductId,
        existing.CategoryId,
        existing.Name,
        existing.Description,
        existing.UnitId,
        existing.Sku,
        existing.Barcode,
        existing.SellingPriceCents,
        existing.CostPriceCents,
        IsActive: false,
        IsTemporarilyUnavailable: true)), "deactivate menu item");

    MenuItemConfig inactive = RequireValue(menu.GetDraft(), "read inactive draft")
        .Items.Single(x => x.ProductId == existing.ProductId);
    Assert(!inactive.IsActive && inactive.IsTemporarilyUnavailable,
        "Lifecycle inactive and temporary unavailable must persist independently.");

    RequireValue(menu.SaveItem(new MenuItemMutation(
        inactive.ProductId,
        inactive.CategoryId,
        inactive.Name,
        inactive.Description,
        inactive.UnitId,
        inactive.Sku,
        inactive.Barcode,
        inactive.SellingPriceCents,
        inactive.CostPriceCents,
        IsActive: true,
        IsTemporarilyUnavailable: false)), "reactivate menu item");

    MenuItemConfig reactivated = RequireValue(menu.GetDraft(), "read reactivated draft")
        .Items.Single(x => x.ProductId == existing.ProductId);
    Assert(reactivated.IsActive && !reactivated.IsTemporarilyUnavailable,
        "Menu item reactivation must be supported without erasing identity.");

    MenuItemConfig edited = RequireValue(menu.SaveItem(new MenuItemMutation(
        reactivated.ProductId,
        reactivated.CategoryId,
        "Legacy Latte Updated",
        reactivated.Description,
        reactivated.UnitId,
        reactivated.Sku,
        reactivated.Barcode,
        reactivated.SellingPriceCents + 500,
        reactivated.CostPriceCents,
        reactivated.IsActive,
        reactivated.IsTemporarilyUnavailable)), "edit stable menu item");

    Assert(edited.ProductId == existing.ProductId &&
           edited.Name == "Legacy Latte Updated" &&
           edited.SellingPriceCents == reactivated.SellingPriceCents + 500,
        "Editing a menu item must preserve stable identity while changing current configuration.");

    Assert(edited.CategoryId.HasValue,
        "Accepted migration-29 fixture must retain its category for C1 publication coherence proof.");
    MenuCategoryConfig editedCategory = initialDraft.Categories.Single(
        x => x.Id == edited.CategoryId.Value);

    RequireValue(menu.SaveCategory(new MenuCategoryMutation(
        editedCategory.Id,
        editedCategory.Name,
        IsActive: false)), "deactivate referenced category");

    Result<MenuPublicationSnapshot> inactiveCategoryPublication = menu.PublishCurrent();
    Assert(inactiveCategoryPublication.IsFailure &&
           inactiveCategoryPublication.ErrorDetail.Code == "Menu.PublishInvalidState",
        "An active item must not publish against an inactive/unpublished category.");

    RequireValue(menu.SaveCategory(new MenuCategoryMutation(
        editedCategory.Id,
        editedCategory.Name,
        IsActive: true)), "reactivate referenced category");

    existing = edited;

    MenuVariantConfig large = RequireValue(menu.SaveVariant(new MenuVariantMutation(
        Id: null,
        ProductId: existing.ProductId,
        Name: "Large",
        Sku: "C1-LARGE",
        Barcode: "C1-LARGE-BARCODE",
        SellingPriceCents: 14500,
        CostPriceCents: 5200,
        IsActive: true,
        IsTemporarilyUnavailable: false,
        DisplayOrder: 10)), "save Large variant");

    Result<MenuVariantConfig> duplicateSku = menu.SaveVariant(new MenuVariantMutation(
        Id: null,
        ProductId: existing.ProductId,
        Name: "Large Duplicate",
        Sku: "C1-LARGE",
        Barcode: "C1-LARGE-BARCODE-2",
        SellingPriceCents: 15000,
        CostPriceCents: null,
        IsActive: true,
        IsTemporarilyUnavailable: false,
        DisplayOrder: 11));
    Assert(duplicateSku.IsFailure && duplicateSku.ErrorDetail.Code == "Menu.DuplicateVariantSku",
        "Duplicate active variant SKU must fail deterministically.");

    MenuModifierGroupConfig milk = RequireValue(menu.SaveModifierGroup(new MenuModifierGroupMutation(
        Id: null,
        Name: "Milk Choice",
        IsRequired: true,
        MinSelections: 1,
        MaxSelections: 2,
        IsActive: true,
        DisplayOrder: 0)), "save modifier group");

    RequireValue(menu.SaveModifierOption(new MenuModifierOptionMutation(
        Id: null,
        GroupId: milk.Id,
        Name: "Oat Milk",
        PriceDeltaCents: 2500,
        IsActive: true,
        DisplayOrder: 0)), "save modifier option 1");

    RequireValue(menu.SaveModifierOption(new MenuModifierOptionMutation(
        Id: null,
        GroupId: milk.Id,
        Name: "Whole Milk",
        PriceDeltaCents: 0,
        IsActive: true,
        DisplayOrder: 1)), "save modifier option 2");

    RequireValue(menu.SaveModifierOption(new MenuModifierOptionMutation(
        Id: null,
        GroupId: milk.Id,
        Name: "C1 Negative Price Effect Proof",
        PriceDeltaCents: -500,
        IsActive: true,
        DisplayOrder: 2)), "save negative modifier price-effect proof option");

    Result<MenuModifierGroupConfig> invalidGroup = menu.SaveModifierGroup(new MenuModifierGroupMutation(
        Id: null,
        Name: "Invalid Required Group",
        IsRequired: true,
        MinSelections: 0,
        MaxSelections: 1,
        IsActive: true,
        DisplayOrder: 99));
    Assert(invalidGroup.IsFailure && invalidGroup.ErrorDetail.Code == "Menu.RequiredModifierNeedsMinimum",
        "Required modifier group with zero minimum must fail.");

    RequireSuccess(menu.SetItemModifierGroups(new MenuModifierLinkMutation(
        existing.ProductId.ToString("D"),
        new[] { milk.Id })), "link item modifier group");

    RequireSuccess(menu.SetVariantModifierGroups(new MenuModifierLinkMutation(
        large.Id,
        new[] { milk.Id })), "link variant modifier group");

    MenuPublicationSnapshot revision1 = RequireValue(menu.PublishCurrent(), "publish revision 1");

    CultureInfo originalCulture = CultureInfo.CurrentCulture;
    CultureInfo alternateHashCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
    alternateHashCulture.NumberFormat.NegativeSign = "~";

    MenuPublicationSnapshot revision1Again;
    try
    {
        CultureInfo.CurrentCulture = alternateHashCulture;
        revision1Again = RequireValue(
            menu.PublishCurrent(),
            "republish unchanged menu under alternate numeric culture");
    }
    finally
    {
        CultureInfo.CurrentCulture = originalCulture;
    }

    Assert(revision1.RevisionNumber == 1, "First menu publication must be revision 1.");
    Assert(revision1Again.RevisionId == revision1.RevisionId, "Unchanged publication must be idempotent.");
    Assert(revision1Again.ContentHash == revision1.ContentHash, "Unchanged publication hash must be deterministic.");
    Assert(revision1.ModifierOptions.Any(x => x.Name == "Oat Milk" && x.PriceDeltaCents == 2500),
        "Modifier price effect must be present in immutable publication.");
    Assert(revision1Again.RevisionId == revision1.RevisionId &&
           revision1Again.ContentHash == revision1.ContentHash &&
           revision1Again.PublicationHash == revision1.PublicationHash,
        "Publication identity/hash must be deterministic across process numeric cultures.");
    Assert(revision1.Variants.Any(x => x.VariantId == large.Id && x.Name == "Large"),
        "First-class variant must be present in immutable publication.");
    Assert(revision1.ItemModifierGroups.Any(x =>
            x.OwnerId == existing.ProductId.ToString("D") && x.GroupId == milk.Id),
        "Item modifier-group linkage must publish.");
    Assert(revision1.VariantModifierGroups.Any(x =>
            x.OwnerId == large.Id && x.GroupId == milk.Id),
        "Variant modifier-group linkage must publish.");

    AssertPublishedSnapshotIsImmutable(factory, revision1.RevisionId);

    RequireValue(menu.SaveVariant(new MenuVariantMutation(
        large.Id,
        large.ProductId,
        large.Name,
        large.Sku,
        large.Barcode,
        large.SellingPriceCents,
        large.CostPriceCents,
        large.IsActive,
        IsTemporarilyUnavailable: true,
        large.DisplayOrder)), "mark Large sold out");

    MenuPublicationSnapshot revision2 = RequireValue(menu.PublishCurrent(), "publish revision 2");
    Assert(revision2.RevisionNumber == 2, "Changed menu content must create a new revision.");
    Assert(revision2.RevisionId != revision1.RevisionId, "Changed menu publication identity must change.");
    Assert(revision2.ContentHash != revision1.ContentHash, "Changed menu content hash must change.");
    Assert(revision2.Variants.Single(x => x.VariantId == large.Id).IsTemporarilyUnavailable,
        "Variant sold-out state must publish independently from active lifecycle.");

    SqliteMenuCatalogStore reopened = new(new SqliteConnectionFactory(DbPaths.CreateAtRoot(root)));
    MenuPublicationSnapshot reopenedCurrent = RequireNotNull(
        RequireValue(reopened.GetCurrentPublication(), "reopen current publication"),
        "current publication missing after reopen");
    Assert(reopenedCurrent.RevisionId == revision2.RevisionId,
        "Current published menu must survive reopen.");

    HistoricalSnapshot after = ReadHistoricalSnapshot(factory, fixture.SaleId);
    Assert(before == after, "Menu edits/publication must not mutate historical sale/receipt snapshots.");

    RunPublicationApplicationProof(
        Path.Combine(root, "receiver"),
        revision1,
        revision2);

    SqliteBackupRecoveryService backups = new(paths);
    string backupPath = RequireValue(backups.CreateBackup(), "create C1 backup");
    var verification = RequireValue(backups.VerifyBackup(backupPath), "verify C1 backup");
    Assert(verification.IsRestorable, "Migration-30 backup must be restorable.");
    Assert(verification.BackupMigrationVersion == 30, "Backup verification must recognize migration 30.");
    Assert(verification.CriticalTablesExist, "Backup verification must recognize all C1 critical tables.");
    Assert(verification.MissingCriticalTables is { Count: 0 }, "C1 backup must not miss critical tables.");

    RequireValue(backups.RestoreVerifiedBackup(
        verification,
        SqliteBackupRecoveryService.RestoreConfirmationText), "restore verified C1 backup");

    SqliteMenuCatalogStore restored = new(new SqliteConnectionFactory(paths));
    MenuPublicationSnapshot restoredCurrent = RequireNotNull(
        RequireValue(restored.GetCurrentPublication(), "read restored publication"),
        "restored current publication missing");
    Assert(restoredCurrent.RevisionId == revision2.RevisionId &&
           restoredCurrent.PublicationHash == revision2.PublicationHash,
        "Backup/restore must preserve C1 current publication authority.");

    HistoricalSnapshot afterRestore = ReadHistoricalSnapshot(new SqliteConnectionFactory(paths), fixture.SaleId);
    Assert(before == afterRestore, "Backup/restore must preserve historical sale/receipt snapshots.");

    Console.WriteLine("PASS migration 29 -> 30, C1 edit/category coherence, domain, publication, history, backup/restore");
}

static void RunPublicationApplicationProof(
    string receiverRoot,
    MenuPublicationSnapshot revision1,
    MenuPublicationSnapshot revision2)
{
    DbPaths receiverPaths = DbPaths.CreateAtRoot(receiverRoot);
    SqliteConnectionFactory receiverFactory = new(receiverPaths);
    new MigrationRunner(
        receiverFactory,
        CafeMigrationCatalog.CreateLegacyYzaV39Migrations()
            .Where(x => x.Version <= 30))
        .RunPendingMigrations();
    new DatabaseSeeder(receiverFactory).SeedDefaultData();

    Assert(ReadMaxMigration(receiverFactory) == 30, "C1-local receiver fixture must stop at migration 30.");
    Assert(ReadMigrationCount(receiverFactory, 23) == 0, "Fresh receiver must not introduce migration 23.");

    SetDeploymentStoreId(receiverFactory, revision1.StoreId);

    SqliteMenuCatalogStore receiver = new(receiverFactory);
    MenuPublicationApplyResult first = RequireValue(receiver.ApplyPublication(revision1), "apply revision 1");
    Assert(first.Applied && !first.Idempotent, "First config application must apply.");

    MenuPublicationApplyResult second = RequireValue(receiver.ApplyPublication(revision2), "apply revision 2");
    Assert(second.Applied && second.CurrentPublication.RevisionNumber == 2,
        "Newer config revision must apply.");

    MenuPublicationApplyResult repeat = RequireValue(receiver.ApplyPublication(revision2), "reapply revision 2");
    Assert(!repeat.Applied && repeat.Idempotent, "Same publication application must be idempotent.");

    Result<MenuPublicationApplyResult> stale = receiver.ApplyPublication(revision1);
    Assert(stale.IsFailure && stale.ErrorDetail.Code == "Menu.StalePublication",
        "Stale config publication must not replace newer authority.");

    MenuPublicationSnapshot tampered = revision2 with
    {
        Items = revision2.Items
            .Select((item, index) => index == 0 ? item with { Name = item.Name + " tampered" } : item)
            .ToArray()
    };
    Result<MenuPublicationApplyResult> conflict = receiver.ApplyPublication(tampered);
    Assert(conflict.IsFailure && conflict.ErrorDetail.Code == "Menu.PublicationHashConflict",
        "Same revision identity with changed content must fail deterministic hash verification.");

    string foreignRoot = receiverRoot + "-foreign";
    DbPaths foreignPaths = DbPaths.CreateAtRoot(foreignRoot);
    SqliteConnectionFactory foreignFactory = new(foreignPaths);
    new MigrationRunner(
        foreignFactory,
        CafeMigrationCatalog.CreateLegacyYzaV39Migrations()
            .Where(x => x.Version <= 30))
        .RunPendingMigrations();
    new DatabaseSeeder(foreignFactory).SeedDefaultData();
    SqliteMenuCatalogStore foreign = new(foreignFactory);
    Result<MenuPublicationApplyResult> foreignResult = foreign.ApplyPublication(revision2);
    Assert(foreignResult.IsFailure && foreignResult.ErrorDetail.Code == "Menu.ForeignStorePublication",
        "Foreign StoreId publication must be rejected.");

    TryDeleteDirectory(foreignRoot);
    Console.WriteLine("PASS versioned config apply/idempotency/stale/hash/store binding");
}

static void RunAndroidCompositionProof(string root)
{
    MobileProductionComposition mobile = MobileProductionComposition.Create(root);
    mobile.InitializeDatabaseAsync().GetAwaiter().GetResult();

    MobileProductionReadiness readiness = mobile.InspectReadiness();
    Assert(
        readiness.MaxMigrationVersion == SalrynDatabaseMigrationCatalog.SupportedMigrationVersion,
        "Android shared composition must accept the current supported migration while preserving C1 menu authority.");
    Assert(readiness.Migration23Present, "Cafe mobile composition records reconciled kernel migration23.");
    Assert(readiness.RequiredSchemaPresent, "Android readiness must include C1 schema/triggers.");
    Assert(readiness.IsReady, "Android production-local composition must remain ready after C1.");

    MenuCatalogDraft draft = RequireValue(mobile.MenuCatalog.GetDraft(), "Android shared C1 menu read");
    Assert(draft.Categories.Count > 0, "Android shared menu authority must reach seeded catalog data.");

    Console.WriteLine("PASS Android shared composition exposes C1 menu authority");
}

static UpgradeFixture CreateAcceptedMigration29Fixture(SqliteConnectionFactory factory)
{
    using SqliteConnection connection = factory.CreateConnection();
    connection.Open();
    using (SqliteCommand pragma = connection.CreateCommand())
    {
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
    }

    string roleId = ScalarText(connection, "SELECT id FROM roles ORDER BY name LIMIT 1;");
    string unitId = ScalarText(connection, "SELECT id FROM units ORDER BY name LIMIT 1;");
    string categoryId = ScalarText(connection, "SELECT id FROM categories ORDER BY name LIMIT 1;");

    string userId = Guid.NewGuid().ToString("D");
    string productId = Guid.NewGuid().ToString("D");
    string sessionId = Guid.NewGuid().ToString("D");
    string saleId = Guid.NewGuid().ToString("D");
    string saleItemId = Guid.NewGuid().ToString("D");
    string now = DateTimeOffset.UtcNow.ToString("O");

    Execute(connection, """
        INSERT INTO users (
            id, username, display_name, password_hash, role_id, status, created_at, updated_at)
        VALUES (
            $id, 'c1-proof', 'C1 Proof', 'not-login-authority', $roleId, 1, $now, NULL);
        """,
        ("$id", userId), ("$roleId", roleId), ("$now", now));

    Execute(connection, """
        INSERT INTO products (
            id, sku, barcode, name, description, category_id, brand_id, unit_id, supplier_id,
            cost_price_cents, selling_price_cents, wholesale_price_cents, stock_on_hand_x1000,
            reorder_level_x1000, track_stock, allow_discount, tax_classification, is_active,
            notes, created_at, updated_at)
        VALUES (
            $id, 'C1-LEGACY', 'C1-LEGACY-BARCODE', 'Legacy Latte', 'migration-29 compatibility fixture',
            $categoryId, NULL, $unitId, NULL, 4500, 12000, NULL, 0, 0, 0, 1, 1, 1,
            NULL, $now, NULL);
        """,
        ("$id", productId), ("$categoryId", categoryId), ("$unitId", unitId), ("$now", now));

    Execute(connection, """
        INSERT INTO cashier_sessions (
            id, user_id, opened_at, closed_at, opening_cash_cents, expected_cash_cents,
            counted_cash_cents, variance_cents, status, notes)
        VALUES (
            $id, $userId, $now, NULL, 0, 0, NULL, 0, 1, 'C1 history fixture');
        """,
        ("$id", sessionId), ("$userId", userId), ("$now", now));

    const string receiptSnapshot = "SALRYN C1 HISTORICAL RECEIPT\nLegacy Latte\n1 x 120.00\nTOTAL 120.00";
    Execute(connection, """
        INSERT INTO sales (
            id, receipt_no, sale_date, cashier_user_id, cashier_session_id,
            subtotal_cents, discount_cents, tax_cents, total_cents, amount_paid_cents,
            change_cents, status, voided_by_user_id, void_reason, voided_at,
            receipt_snapshot, created_at)
        VALUES (
            $id, 'C1-HIST-0001', $now, $userId, $sessionId,
            12000, 0, 0, 12000, 12000, 0, 2, NULL, NULL, NULL,
            $receiptSnapshot, $now);
        """,
        ("$id", saleId), ("$now", now), ("$userId", userId),
        ("$sessionId", sessionId), ("$receiptSnapshot", receiptSnapshot));

    Execute(connection, """
        INSERT INTO sale_items (
            id, sale_id, product_id, product_name_snapshot, barcode_snapshot,
            unit_name_snapshot, qty_x1000, unit_price_cents, cost_price_cents,
            discount_cents, line_total_cents)
        VALUES (
            $id, $saleId, $productId, 'Legacy Latte', 'C1-LEGACY-BARCODE',
            'piece', 1000, 12000, 4500, 0, 12000);
        """,
        ("$id", saleItemId), ("$saleId", saleId), ("$productId", productId));

    return new UpgradeFixture(productId, categoryId, saleId);
}

static HistoricalSnapshot ReadHistoricalSnapshot(SqliteConnectionFactory factory, string saleId)
{
    using SqliteConnection connection = factory.CreateConnection();
    connection.Open();
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = """
        SELECT
            s.receipt_snapshot,
            i.product_name_snapshot,
            i.barcode_snapshot,
            i.unit_name_snapshot,
            i.unit_price_cents,
            i.line_total_cents
        FROM sales s
        INNER JOIN sale_items i ON i.sale_id = s.id
        WHERE s.id = $saleId
        LIMIT 1;
        """;
    command.Parameters.AddWithValue("$saleId", saleId);
    using SqliteDataReader reader = command.ExecuteReader();
    Assert(reader.Read(), "Historical sale fixture must remain readable.");

    return new HistoricalSnapshot(
        reader.GetString(0),
        reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.GetString(3),
        reader.GetInt64(4),
        reader.GetInt64(5));
}

static void AssertPublishedSnapshotIsImmutable(
    SqliteConnectionFactory factory,
    string revisionId)
{
    using SqliteConnection connection = factory.CreateConnection();
    connection.Open();
    try
    {
        Execute(connection, """
            UPDATE menu_revision_items
            SET name = 'MUTATED'
            WHERE revision_id = $revisionId;
            """,
            ("$revisionId", revisionId));
        throw new InvalidOperationException("Published menu snapshot update unexpectedly succeeded.");
    }
    catch (SqliteException ex) when (
        ex.Message.Contains("published menu snapshot is immutable", StringComparison.OrdinalIgnoreCase))
    {
    }
}

static void SetDeploymentStoreId(SqliteConnectionFactory factory, Guid storeId)
{
    using SqliteConnection connection = factory.CreateConnection();
    connection.Open();
    Execute(connection, """
        UPDATE deployment_identity
        SET store_id = $storeId,
            updated_at = $now;
        """,
        ("$storeId", storeId.ToString("D")),
        ("$now", DateTimeOffset.UtcNow.ToString("O")));
}

static int ReadMaxMigration(SqliteConnectionFactory factory)
{
    return checked((int)ScalarLong(factory, "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;"));
}

static int ReadMigrationCount(SqliteConnectionFactory factory, int version)
{
    return checked((int)ScalarLong(
        factory,
        "SELECT COUNT(*) FROM schema_migrations WHERE version = $version;",
        ("$version", version)));
}

static long ScalarLong(
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

static string ScalarText(SqliteConnection connection, string sql)
{
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = sql;
    return Convert.ToString(command.ExecuteScalar()) ??
        throw new InvalidOperationException("Required fixture identity was not found.");
}

static void Execute(
    SqliteConnection connection,
    string sql,
    params (string Name, object? Value)[] parameters)
{
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = sql;
    foreach ((string name, object? value) in parameters)
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    command.ExecuteNonQuery();
}

static T RequireValue<T>(Result<T> result, string operation)
{
    if (result.IsFailure)
        throw new InvalidOperationException(operation + " failed: " + result.ErrorDetail.Code + " / " + result.ErrorDetail.Message);
    return result.Value;
}

static void RequireSuccess(Result result, string operation)
{
    if (result.IsFailure)
        throw new InvalidOperationException(operation + " failed: " + result.ErrorDetail.Code + " / " + result.ErrorDetail.Message);
}

static T RequireNotNull<T>(T? value, string message) where T : class
{
    return value ?? throw new InvalidOperationException(message);
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void TryDeleteDirectory(string path)
{
    try
    {
        SqliteConnectionFactory.ClearAllPools();
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
    catch
    {
    }
}

internal sealed record UpgradeFixture(
    string ProductId,
    string CategoryId,
    string SaleId);

internal sealed record HistoricalSnapshot(
    string ReceiptSnapshot,
    string ProductNameSnapshot,
    string? BarcodeSnapshot,
    string UnitNameSnapshot,
    long UnitPriceCents,
    long LineTotalCents);

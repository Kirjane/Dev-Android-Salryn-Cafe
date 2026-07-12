using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tools.DevData;

public static class Program
{
    private const string SeedSkuPrefix = "DEV-GMHW-";
    private const int DefaultSeedCount = 1000;

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "--help" or "-h")
            {
                PrintUsage();
                return 0;
            }

            string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
            DbPaths dbPaths = DbPaths.CreateDevelopment(projectRoot);
            EnsureDevOnly(dbPaths.DatabasePath);

            return args[0] switch
            {
                "reset-db" => ResetDatabase(args, dbPaths),
                "seed-general-store" => SeedGeneralStore(args, dbPaths),
                _ => Fail("Unknown command: " + args[0])
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[Salryn DevData] ERROR: " + ex.Message);
            return 1;
        }
    }

    private static int ResetDatabase(string[] args, DbPaths dbPaths)
    {
        if (!args.Contains("--confirm-dev-reset", StringComparer.OrdinalIgnoreCase))
        {
            return Fail("reset-db requires --confirm-dev-reset. This command is development-only and refuses implicit resets.");
        }

        EnsureDevOnly(dbPaths.DatabasePath);
        Directory.CreateDirectory(dbPaths.DataPath);

        string backupDirectory = Path.Combine(dbPaths.DataPath, "manual-backups");
        Directory.CreateDirectory(backupDirectory);

        string backupPath = Path.Combine(
            backupDirectory,
            "salryn-dev-reset-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + ".db");

        Console.WriteLine("[Salryn DevData] Source DB path: " + dbPaths.DatabasePath);
        Console.WriteLine("[Salryn DevData] Backup path: " + backupPath);

        if (File.Exists(dbPaths.DatabasePath))
        {
            File.Copy(dbPaths.DatabasePath, backupPath, overwrite: false);
            File.Delete(dbPaths.DatabasePath);
            Console.WriteLine("[Salryn DevData] Active dev DB backed up and removed.");
        }
        else
        {
            File.WriteAllText(backupPath + ".empty.txt", "No active dev database existed at reset time." + Environment.NewLine);
            Console.WriteLine("[Salryn DevData] No active dev DB existed; wrote empty-reset marker.");
        }

        Console.WriteLine("[Salryn DevData] Next: start the app and complete first-run setup at /bootstrap.");
        return 0;
    }

    private static int SeedGeneralStore(string[] args, DbPaths dbPaths)
    {
        if (!args.Contains("--confirm-dev-seed", StringComparer.OrdinalIgnoreCase))
        {
            return Fail("seed-general-store requires --confirm-dev-seed. This command is development-only.");
        }

        int count = ReadIntOption(args, "--count", DefaultSeedCount);
        bool replace = args.Contains("--replace-dev-products", StringComparer.OrdinalIgnoreCase);

        if (count != DefaultSeedCount)
        {
            return Fail("Manual Proof M1 seed requires exactly 1000 products. Requested count: " + count);
        }

        EnsureDevOnly(dbPaths.DatabasePath);

        if (!File.Exists(dbPaths.DatabasePath))
        {
            return Fail("Dev database does not exist. Start the app once and complete first-run setup before seeding.");
        }

        DatabaseInitializationOrchestrator initialization = DefaultDatabaseInitializationFactory.Create(dbPaths);
        var status = initialization.CheckStatus();
        if (status.IsFailure)
        {
            return Fail(status.ErrorDetail.Code + ": " + status.ErrorDetail.Message);
        }

        if (status.Value.FirstRunStatus.IsFirstRunRequired ||
            !status.Value.FirstRunStatus.HasUsers ||
            !status.Value.FirstRunStatus.HasStoreSettings)
        {
            return Fail("Complete first-run admin setup before seeding dev products.");
        }

        IReadOnlyList<SeedProduct> products = DevProductSeedFactory.Generate(DefaultSeedCount);
        DevProductSeedFactory.Validate(products, DefaultSeedCount);

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPaths.DatabasePath
        }.ToString());
        connection.Open();
        SqlitePragma.Apply(connection);

        long existingProducts = ScalarLong(connection, "SELECT COUNT(*) FROM products;");
        if (existingProducts > 0 && !replace)
        {
            return Fail("Products already exist. Refusing to seed over existing inventory. Reset the dev DB first or pass --replace-dev-products for seeded dev rows only.");
        }

        if (replace)
        {
            long saleItems = ScalarLong(connection, "SELECT COUNT(*) FROM sale_items;");
            if (saleItems > 0)
            {
                return Fail("Refusing --replace-dev-products because sale_items exist. Reset the dev DB instead to avoid orphaning sales evidence.");
            }

            Execute(connection, null, "DELETE FROM stock_movements WHERE product_id IN (SELECT id FROM products WHERE sku LIKE 'DEV-GMHW-%');");
            Execute(connection, null, "DELETE FROM products WHERE sku LIKE 'DEV-GMHW-%';");
        }

        using var transaction = connection.BeginTransaction();
        string now = DateTimeOffset.UtcNow.ToString("O");

        foreach (string unit in products.Select(product => product.Unit).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Execute(connection, transaction, """
                INSERT OR IGNORE INTO units (id, name, abbreviation, allows_decimal_quantity, is_active)
                VALUES ($id, $name, $abbreviation, $allowsDecimalQuantity, 1);
                """, new Dictionary<string, object?>
            {
                ["$id"] = StableGuid("unit:" + unit),
                ["$name"] = UnitName(unit),
                ["$abbreviation"] = unit,
                ["$allowsDecimalQuantity"] = AllowsDecimal(unit) ? 1 : 0
            });
        }

        foreach (string category in products.Select(product => product.Category).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Execute(connection, transaction, """
                INSERT OR IGNORE INTO categories (id, name, is_active)
                VALUES ($id, $name, 1);
                """, new Dictionary<string, object?>
            {
                ["$id"] = StableGuid("category:" + category),
                ["$name"] = category
            });
        }

        foreach (string brand in products.Select(product => product.Brand).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Execute(connection, transaction, """
                INSERT OR IGNORE INTO brands (id, name, is_active)
                VALUES ($id, $name, 1);
                """, new Dictionary<string, object?>
            {
                ["$id"] = StableGuid("brand:" + brand),
                ["$name"] = brand
            });
        }

        foreach (SeedProduct product in products)
        {
            Execute(connection, transaction, """
                INSERT INTO products
                    (id, sku, barcode, name, description, category_id, brand_id, unit_id, supplier_id,
                     cost_price_cents, selling_price_cents, wholesale_price_cents, stock_on_hand_x1000,
                     reorder_level_x1000, track_stock, allow_discount, is_active, notes, created_at, updated_at)
                VALUES
                    ($id, $sku, $barcode, $name, $description, $categoryId, $brandId, $unitId, NULL,
                     $costPriceCents, $sellingPriceCents, NULL, $stockOnHandX1000,
                     $reorderLevelX1000, $trackStock, 1, $isActive, $notes, $createdAt, NULL);
                """, new Dictionary<string, object?>
            {
                ["$id"] = StableGuid("product:" + product.Sku),
                ["$sku"] = product.Sku,
                ["$barcode"] = product.Barcode,
                ["$name"] = product.Name,
                ["$description"] = product.Category + " manual-proof seed item",
                ["$categoryId"] = StableGuid("category:" + product.Category),
                ["$brandId"] = StableGuid("brand:" + product.Brand),
                ["$unitId"] = StableGuid("unit:" + product.Unit),
                ["$costPriceCents"] = product.CostPriceCents,
                ["$sellingPriceCents"] = product.SellingPriceCents,
                ["$stockOnHandX1000"] = product.StockOnHand * 1000,
                ["$reorderLevelX1000"] = product.ReorderLevel * 1000,
                ["$trackStock"] = product.TrackStock ? 1 : 0,
                ["$isActive"] = product.IsActive ? 1 : 0,
                ["$notes"] = "Manual Proof M1 deterministic dev seed; no product images.",
                ["$createdAt"] = now
            });
        }

        transaction.Commit();

        long inserted = ScalarLong(connection, "SELECT COUNT(*) FROM products WHERE sku LIKE 'DEV-GMHW-%';");
        long uniqueSkus = ScalarLong(connection, "SELECT COUNT(DISTINCT sku) FROM products WHERE sku LIKE 'DEV-GMHW-%';");
        long uniqueNames = ScalarLong(connection, "SELECT COUNT(DISTINCT name) FROM products WHERE sku LIKE 'DEV-GMHW-%';");
        long uniqueBarcodes = ScalarLong(connection, "SELECT COUNT(DISTINCT barcode) FROM products WHERE sku LIKE 'DEV-GMHW-%';");

        if (inserted != DefaultSeedCount || uniqueSkus != DefaultSeedCount || uniqueNames != DefaultSeedCount || uniqueBarcodes != DefaultSeedCount)
        {
            return Fail($"Seed verification failed. Inserted={inserted}; Unique SKUs={uniqueSkus}; Unique names={uniqueNames}; Unique barcodes={uniqueBarcodes}.");
        }

        Console.WriteLine("[Salryn DevData] DB path: " + dbPaths.DatabasePath);
        Console.WriteLine("[Salryn DevData] Seed profile: Philippine general merchandise + hardware store");
        Console.WriteLine("Seeded products: " + inserted);
        Console.WriteLine("Unique SKUs: " + uniqueSkus);
        Console.WriteLine("Unique names: " + uniqueNames);
        Console.WriteLine("Unique barcodes: " + uniqueBarcodes);
        Console.WriteLine("Categories used: " + products.Select(product => product.Category).Distinct().Count());
        Console.WriteLine("Brands/generic labels used: " + products.Select(product => product.Brand).Distinct().Count());
        Console.WriteLine("Tracked inventory items: " + products.Count(product => product.TrackStock));
        Console.WriteLine("Low stock items: " + products.Count(product => product.TrackStock && product.StockOnHand > 0 && product.StockOnHand <= product.ReorderLevel));
        Console.WriteLine("Out of stock items: " + products.Count(product => product.TrackStock && product.StockOnHand == 0));
        Console.WriteLine("[Salryn DevData] No product images, image paths, image URLs, or media fields were seeded.");
        return 0;
    }

    private static int ReadIntOption(string[] args, string name, int fallback)
    {
        int index = Array.FindIndex(args, arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return fallback;
        }

        if (index + 1 >= args.Length || !int.TryParse(args[index + 1], out int value))
        {
            throw new InvalidOperationException(name + " requires an integer value.");
        }

        return value;
    }

    private static void EnsureDevOnly(string databasePath)
    {
        string fullPath = Path.GetFullPath(databasePath);
        string marker = Path.DirectorySeparatorChar + ".salryn-dev" + Path.DirectorySeparatorChar;
        if (!fullPath.Contains(marker, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to touch non-dev database path: " + fullPath);
        }

        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrWhiteSpace(programData) && fullPath.StartsWith(Path.GetFullPath(programData), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to touch ProgramData production path: " + fullPath);
        }
    }

    private static string FindProjectRoot(string startPath)
    {
        DirectoryInfo? current = new(startPath);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not find Salryn.sln from " + startPath);
    }

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        if (parameters is not null)
        {
            foreach (var parameter in parameters)
            {
                command.Parameters.AddWithValue(parameter.Key, parameter.Value ?? DBNull.Value);
            }
        }

        command.ExecuteNonQuery();
    }

    private static string StableGuid(string key)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes("Salryn.DevData." + key));
        return new Guid(hash).ToString("D");
    }

    private static string UnitName(string abbreviation) => abbreviation switch
    {
        "pc" => "Piece",
        "box" => "Box",
        "pack" => "Pack",
        "set" => "Set",
        "pair" => "Pair",
        "kg" => "Kilogram",
        "g" => "Gram",
        "liter" => "Liter",
        "ml" => "Milliliter",
        "bottle" => "Bottle",
        "can" => "Can",
        "sachet" => "Sachet",
        "roll" => "Roll",
        "meter" => "Meter",
        "bag" => "Bag",
        "bundle" => "Bundle",
        "tube" => "Tube",
        "gal" => "Gallon",
        _ => abbreviation
    };

    private static bool AllowsDecimal(string abbreviation) => abbreviation is "kg" or "g" or "liter" or "ml" or "meter";

    private static void PrintUsage()
    {
        Console.WriteLine("Salryn.Tools.DevData");
        Console.WriteLine("  reset-db --confirm-dev-reset");
        Console.WriteLine("  seed-general-store --count 1000 --confirm-dev-seed [--replace-dev-products]");
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("[Salryn DevData] ERROR: " + message);
        return 1;
    }
}

internal sealed record SeedProduct(
    string Sku,
    string Barcode,
    string Name,
    string Category,
    string Brand,
    string Unit,
    int CostPriceCents,
    int SellingPriceCents,
    int StockOnHand,
    int ReorderLevel,
    bool TrackStock,
    bool IsActive);

internal sealed record SeedCategory(string Category, string Code, string Unit, string[] Brands, string[] Bases, string[] Specs);

internal static class DevProductSeedFactory
{
    private static readonly SeedCategory[] Categories =
    {
        new("Hardware", "HW", "pc", Brands("TibayPro", "Bohol Hardware", "Generic Hardware", "Fortress", "Matibay"), Bases("GI wire", "Concrete nails", "Wood screws", "Machine bolts", "Hinges", "Padlock", "Door knob", "L angle bracket", "Blind rivets", "Roofing screw"), Specs("#16", "2in", "1in", "1/4x2in", "3in", "40mm", "stainless", "2x2in", "1/8in", "tek screw")),
        new("Electrical", "EL", "pc", Brands("VoltSafe", "Omni-style", "BrightLite", "Generic Electrical", "CircuitPro"), Bases("LED bulb", "Electrical tape", "Circuit breaker", "Extension outlet", "Wire connector", "Switch plate", "Duplex outlet", "THHN wire", "Lamp holder", "Cable tie"), Specs("9W daylight", "black 18mm", "20A", "3-gang", "P4", "1-gang", "universal", "#12 stranded", "E27", "8in")),
        new("Plumbing", "PL", "pc", Brands("TubigPro", "PVC Master", "Generic Plumbing", "AquaLine", "BluePipe"), Bases("PVC elbow", "PVC pipe", "PVC tee", "Faucet", "P-trap", "Flexible hose", "Ball valve", "Pipe clamp", "Teflon tape", "Floor drain"), Specs("1/2in", "3/4in", "1in", "chrome", "lavatory", "12in", "brass 1/2in", "medium", "12mm roll", "4x4in")),
        new("Paint and Supplies", "PA", "liter", Brands("ColorHaus", "PrimeCoat", "Generic Paint", "WallPro", "BrushMate"), Bases("Flat latex paint", "Gloss enamel", "Paint roller", "Paint brush", "Thinner", "Primer", "Skim coat", "Masking tape", "Paint tray", "Sandpaper"), Specs("1L white", "1L black", "7in", "2in", "350ml", "1L gray", "1kg", "1in", "standard", "#120")),
        new("Tools", "TO", "pc", Brands("ToolMax", "Mekaniko", "Generic Tools", "HandyPro", "SteelGrip"), Bases("Hammer", "Screwdriver Phillips", "Screwdriver flat", "Pliers", "Adjustable wrench", "Tape measure", "Cutter knife", "Hacksaw frame", "Level bar", "Allen key set"), Specs("16oz", "#2", "6mm", "8in", "10in", "5m", "heavy duty", "12in", "18in", "metric")),
        new("Fasteners", "FA", "box", Brands("FixIt", "Generic Fasteners", "BoltHaus", "NailPro", "ScrewMaster"), Bases("Drywall screw", "Concrete nail", "Finishing nail", "Hex nut", "Flat washer", "Self-drilling screw", "U-bolt", "Anchor bolt", "Wood screw", "Staple wire"), Specs("1in box", "2in box", "1.5in box", "1/4in", "1/4in pack", "12x1in", "1/4x2in", "8mm", "2in pack", "T50")),
        new("Construction Materials", "CM", "bag", Brands("BuildRite", "Generic Materials", "MasonPro", "GravelMix", "Cemento"), Bases("Portland cement", "Tile adhesive", "Grout", "Concrete patch", "Waterproofing powder", "Wall putty", "Mortar mix", "Pebble wash", "Fine sand", "Hollow block"), Specs("40kg", "25kg", "2kg white", "5kg", "1kg", "20kg", "25kg", "25kg", "sack", "4in")),
        new("Automotive", "AU", "bottle", Brands("MotorCare", "RoadPro", "Generic Auto", "LubeMax", "DriveSafe"), Bases("Motor oil", "Brake fluid", "Coolant", "WD-40 style lubricant", "Car shampoo", "Tire black", "Spark plug", "Battery terminal", "Microfiber cloth", "Wiper blade"), Specs("1L 20W-50", "250ml", "1L green", "200ml", "500ml", "250ml", "standard", "pair", "40x40cm", "18in")),
        new("Home Cleaning", "HC", "bottle", Brands("Linaw", "CleanMate", "Generic Cleaning", "BahayFresh", "Sparkle"), Bases("Dishwashing liquid", "Bleach", "Floor cleaner", "Glass cleaner", "Toilet cleaner", "Mop head", "Scrub pad", "Trash bag", "Disinfectant spray", "All-purpose cleaner"), Specs("500ml", "1L", "1L lemon", "500ml", "500ml", "cotton", "3pc", "large 10pc", "300ml", "1L")),
        new("Kitchenware", "KW", "pc", Brands("KusinaPro", "HomeBasics", "Generic Kitchen", "CookMate", "SteelHome"), Bases("Plastic basin", "Food container", "Stainless ladle", "Frying pan", "Knife", "Chopping board", "Measuring cup", "Dish rack", "Water dipper", "Rice ladle"), Specs("medium", "1L", "medium", "24cm", "8in", "small", "500ml", "2-layer", "large", "plastic")),
        new("School and Office Supplies", "SO", "pc", Brands("Eskwela", "PaperWay", "Generic Office", "WritePro", "ClassMate"), Bases("Notebook", "Ballpen", "Bond paper", "Pencil", "Eraser", "Correction tape", "Folder", "Envelope", "Marker", "Stapler"), Specs("80 leaves", "blue", "short 500s", "#2", "white", "5mm", "long", "brown short", "black", "#10")),
        new("Personal Care", "PC", "sachet", Brands("Alaga", "FreshCare", "Generic Care", "BathMate", "SmilePro"), Bases("Toothpaste", "Shampoo", "Conditioner", "Bath soap", "Deodorant", "Cotton buds", "Alcohol", "Face towel", "Baby powder", "Hair gel"), Specs("100ml", "sachet", "sachet", "90g", "40ml", "100 tips", "500ml", "small", "100g", "50g")),
        new("Beverages", "BV", "bottle", Brands("TubigFresh", "CafePinoy", "Generic Beverage", "CoolSip", "JuiceKo"), Bases("Bottled water", "Soft drink", "Iced tea", "Instant coffee", "Powdered juice", "Energy drink", "Milk drink", "Coconut water", "Chocolate drink", "Tea bags"), Specs("1L", "1.5L", "1L", "sachet 20g", "25g", "250ml", "180ml", "330ml", "200ml", "25s")),
        new("Snacks", "SN", "pack", Brands("Meryenda", "ChippyStyle", "Generic Snacks", "CrunchPinoy", "SweetPack"), Bases("Potato chips", "Corn chips", "Crackers", "Cookies", "Peanuts", "Chocolate bar", "Wafer", "Cupcake", "Biscuit", "Cheese curls"), Specs("60g", "70g", "10s", "100g", "100g", "35g", "10s", "single", "pack", "60g")),
        new("Canned Goods", "CG", "can", Brands("LutongBahay", "FiestaCan", "Generic Canned", "UlamReady", "PinoyCan"), Bases("Sardines", "Corned beef", "Meat loaf", "Tuna flakes", "Vienna sausage", "Pork and beans", "Liver spread", "Mackerel", "Luncheon meat", "Canned corn"), Specs("155g", "150g", "170g", "180g", "130g", "230g", "85g", "155g", "340g", "425g")),
        new("Rice and Staples", "RS", "kg", Brands("AniHarvest", "PalayPrime", "Generic Staples", "Bohol Rice", "SariSari"), Bases("Rice", "Brown sugar", "White sugar", "Flour", "Cornstarch", "Salt", "Cooking oil", "Vinegar", "Soy sauce", "Pancit canton pack"), Specs("5kg", "1kg", "1kg", "1kg", "200g", "500g", "1L", "1L", "1L", "6s")),
        new("Noodles", "NO", "pack", Brands("QuickNoodle", "SabawTime", "Generic Noodles", "MeriendaCup", "PinoyNoodle"), Bases("Instant noodles", "Cup noodles", "Pancit canton", "Bihon", "Sotanghon", "Miswa", "Spaghetti pasta", "Macaroni", "Egg noodles", "Ramen pack"), Specs("chicken", "beef", "chilimy", "400g", "250g", "100g", "900g", "400g", "250g", "spicy")),
        new("Condiments", "CO", "bottle", Brands("Sawsawan", "FlavorHaus", "Generic Condiments", "LasaPinoy", "SpiceMate"), Bases("Soy sauce", "Vinegar", "Fish sauce", "Banana ketchup", "Hot sauce", "Oyster sauce", "Mayonnaise", "Tomato sauce", "Pepper", "Magic-style seasoning"), Specs("350ml", "350ml", "350ml", "320g", "150ml", "300g", "220ml", "250g", "50g", "8g x 12")),
        new("Laundry", "LA", "pack", Brands("Linis", "WashPro", "Generic Laundry", "Bango", "FoamCare"), Bases("Laundry powder", "Fabric conditioner", "Bar detergent", "Liquid detergent", "Stain remover", "Clothes pin", "Laundry net", "Hanger", "Bleach sachet", "Basin soap"), Specs("1kg", "800ml", "380g", "1L", "500ml", "12pc", "medium", "6pc", "60ml", "250g")),
        new("Pet and Garden", "PG", "pc", Brands("AlagaPet", "GreenBahay", "Generic Garden", "GrowMate", "PetPinoy"), Bases("Dog food", "Cat food", "Garden hose", "Plant pot", "Fertilizer", "Garden trowel", "Sprayer", "Plant tie", "Bird seed", "Pet shampoo"), Specs("1kg", "1kg", "10m", "8in", "1kg", "small", "1L", "roll", "500g", "250ml"))
    };

    public static IReadOnlyList<SeedProduct> Generate(int count)
    {
        List<SeedProduct> products = new(count);
        int index = 1;

        foreach (SeedCategory category in Categories)
        {
            foreach (string itemBase in category.Bases)
            {
                foreach (string spec in category.Specs)
                {
                    string brand = category.Brands[(index - 1) % category.Brands.Length];
                    string name = brand + " " + itemBase + " " + spec;
                    int cost = CostCents(category.Code, index);
                    int selling = cost + Math.Max(100, cost / 4 + (index % 7) * 25);
                    bool tracked = index % 13 != 0;
                    int reorder = tracked ? 5 + (index % 12) : 0;
                    int stock = tracked
                        ? index % 17 == 0
                            ? 0
                            : index % 11 == 0
                                ? Math.Max(1, reorder - 1)
                                : reorder + 10 + (index % 90)
                        : 0;

                    products.Add(new SeedProduct(
                        Sku: SeedSkuPrefix + index.ToString("D4"),
                        Barcode: (4_809_000_000_000L + index).ToString(),
                        Name: name,
                        Category: category.Category,
                        Brand: brand,
                        Unit: category.Unit,
                        CostPriceCents: cost,
                        SellingPriceCents: selling,
                        StockOnHand: stock,
                        ReorderLevel: reorder,
                        TrackStock: tracked,
                        IsActive: index % 97 != 0));

                    index++;
                    if (products.Count == count)
                    {
                        return products;
                    }
                }
            }
        }

        return products;
    }

    public static void Validate(IReadOnlyList<SeedProduct> products, int expectedCount)
    {
        if (products.Count != expectedCount)
        {
            throw new InvalidOperationException($"Seed generator produced {products.Count} products; expected {expectedCount}.");
        }

        RequireUnique(products.Select(product => product.Sku), "SKU");
        RequireUnique(products.Select(product => product.Name), "product name");
        RequireUnique(products.Select(product => product.Barcode), "barcode");
        RequireUnique(products.Select(product => product.Category + "|" + product.Name), "category/product name");

        if (products.Any(product => product.CostPriceCents >= product.SellingPriceCents))
        {
            throw new InvalidOperationException("Every seed product must have cost below selling price.");
        }

        if (products.Any(product => product.Name.Contains("Item ", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Seed names must not be generic Item filler.");
        }
    }

    private static void RequireUnique(IEnumerable<string> values, string label)
    {
        string? duplicate = values
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .FirstOrDefault();

        if (duplicate is not null)
        {
            throw new InvalidOperationException("Duplicate " + label + " generated: " + duplicate);
        }
    }

    private static int CostCents(string categoryCode, int index)
    {
        int basePeso = categoryCode switch
        {
            "TO" => 120 + (index % 45) * 35,
            "CM" => 80 + (index % 35) * 45,
            "AU" => 90 + (index % 40) * 30,
            "HW" or "EL" or "PL" or "PA" or "FA" => 15 + (index % 60) * 18,
            _ => 5 + (index % 95) * 6
        };

        return basePeso * 100;
    }

    private static string[] Brands(params string[] values) => values;

    private static string[] Bases(params string[] values) => values;
}

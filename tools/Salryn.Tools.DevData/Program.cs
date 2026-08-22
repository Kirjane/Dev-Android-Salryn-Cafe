using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tools.DevData;

public static class Program
{
    private const int RequiredCount = 1000;

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "--help" or "-h")
            {
                Console.WriteLine("Salryn.Tools.DevData");
                Console.WriteLine("  reset-db --confirm-dev-reset");
                Console.WriteLine("  seed-general-store --count 1000 --confirm-dev-seed [--replace-dev-products]");
                return 0;
            }

            string root = FindProjectRoot(Directory.GetCurrentDirectory());
            DbPaths paths = DbPaths.CreateDevelopment(root);
            EnsureDevOnly(paths.DatabasePath);

            return args[0] switch
            {
                "reset-db" => ResetDb(args, paths),
                "seed-general-store" => SeedGeneralStore(args, paths),
                _ => Fail("Unknown command: " + args[0])
            };
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    public static int SeedGeneralStoreForManualProof(
        DbPaths paths,
        int count,
        bool confirmDevSeed,
        bool replaceDevProducts = false)
    {
        if (!confirmDevSeed)
        {
            return Fail("seed-general-store requires --confirm-dev-seed.");
        }

        if (count != RequiredCount)
        {
            return Fail("Manual Proof M1 dev seed requires exactly 1000 products. Requested count: " + count);
        }

        return SeedGeneralStoreCore(paths, replaceDevProducts);
    }

    private static int ResetDb(string[] args, DbPaths paths)
    {
        if (!args.Contains("--confirm-dev-reset", StringComparer.OrdinalIgnoreCase))
        {
            return Fail("reset-db requires --confirm-dev-reset.");
        }

        EnsureDevOnly(paths.DatabasePath);
        Directory.CreateDirectory(paths.DataPath);
        string backupDir = Path.Combine(paths.DataPath, "manual-backups");
        Directory.CreateDirectory(backupDir);
        string backupPath = Path.Combine(
            backupDir,
            "salryn-dev-reset-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + ".db");

        Console.WriteLine("Source DB path: " + paths.DatabasePath);
        Console.WriteLine("Backup path: " + backupPath);

        if (File.Exists(paths.DatabasePath))
        {
            File.Copy(paths.DatabasePath, backupPath, overwrite: false);
            File.Delete(paths.DatabasePath);
            Console.WriteLine("Active dev DB backed up and removed.");
        }
        else
        {
            File.WriteAllText(
                backupPath + ".empty.txt",
                "No active dev database existed at reset time." + Environment.NewLine);
            Console.WriteLine("No active dev DB existed; empty-reset marker created.");
        }

        Console.WriteLine("Next: start the app and complete first-run setup at /bootstrap.");
        return 0;
    }

    private static int SeedGeneralStore(string[] args, DbPaths paths)
    {
        if (!args.Contains("--confirm-dev-seed", StringComparer.OrdinalIgnoreCase))
        {
            return Fail("seed-general-store requires --confirm-dev-seed.");
        }

        int count = ReadIntOption(args, "--count", RequiredCount);
        bool replace = args.Contains("--replace-dev-products", StringComparer.OrdinalIgnoreCase);

        return SeedGeneralStoreForManualProof(
            paths,
            count,
            confirmDevSeed: true,
            replaceDevProducts: replace);
    }

    private static int SeedGeneralStoreCore(DbPaths paths, bool replace)
    {
        EnsureDevOnly(paths.DatabasePath);

        if (!File.Exists(paths.DatabasePath))
        {
            return Fail("Dev database does not exist. Start the app and complete first-run setup before seeding.");
        }

        DatabaseInitializationOrchestrator init = DefaultDatabaseInitializationFactory.Create(paths);
        var status = init.CheckStatus();
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

        IReadOnlyList<SeedProduct> products = SeedCatalog.Generate(RequiredCount);
        SeedCatalog.Validate(products, RequiredCount);

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString());
        connection.Open();
        SqlitePragma.Apply(connection);

        long existingProducts = ScalarLong(connection, "SELECT COUNT(*) FROM products;");
        if (existingProducts > 0 && !replace)
        {
            return Fail("Products already exist. Reset the dev DB first or pass --replace-dev-products for seeded dev rows only.");
        }

        if (replace)
        {
            if (ScalarLong(connection, "SELECT COUNT(*) FROM sale_items;") > 0)
            {
                return Fail("Refusing --replace-dev-products because sale_items exist. Reset the dev DB instead.");
            }

            Execute(
                connection,
                null,
                "DELETE FROM stock_movements WHERE product_id IN (SELECT id FROM products WHERE sku LIKE 'DEV-GMHW-%');");
            Execute(connection, null, "DELETE FROM products WHERE sku LIKE 'DEV-GMHW-%';");
        }

        using var tx = connection.BeginTransaction();
        string now = DateTimeOffset.UtcNow.ToString("O");

        Dictionary<string, string> unitIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (string unit in products.Select(p => p.Unit).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            unitIds[unit] = EnsureUnit(connection, tx, unit);
        }

        Dictionary<string, string> categoryIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (string category in products.Select(p => p.Category).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            categoryIds[category] = EnsureCategory(connection, tx, category);
        }

        Dictionary<string, string> brandIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (string brand in products.Select(p => p.Brand).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            brandIds[brand] = EnsureBrand(connection, tx, brand);
        }

        foreach (SeedProduct p in products)
        {
            Execute(connection, tx, """
                INSERT INTO products
                    (id, sku, barcode, name, description, category_id, brand_id, unit_id, supplier_id,
                     cost_price_cents, selling_price_cents, wholesale_price_cents, stock_on_hand_x1000,
                     reorder_level_x1000, track_stock, allow_discount, is_active, notes, created_at, updated_at)
                VALUES
                    ($id, $sku, $barcode, $name, $description, $categoryId, $brandId, $unitId, NULL,
                     $cost, $selling, NULL, $stock, $reorder, $track, 1, $active, $notes, $createdAt, NULL);
                """,
                new Dictionary<string, object?>
                {
                    ["$id"] = StableGuid("product:" + p.Sku),
                    ["$sku"] = p.Sku,
                    ["$barcode"] = p.Barcode,
                    ["$name"] = p.Name,
                    ["$description"] = p.Category + " manual-proof seed item",
                    ["$categoryId"] = categoryIds[p.Category],
                    ["$brandId"] = brandIds[p.Brand],
                    ["$unitId"] = unitIds[p.Unit],
                    ["$cost"] = p.CostPriceCents,
                    ["$selling"] = p.SellingPriceCents,
                    ["$stock"] = p.StockOnHand * 1000,
                    ["$reorder"] = p.ReorderLevel * 1000,
                    ["$track"] = p.TrackStock ? 1 : 0,
                    ["$active"] = p.IsActive ? 1 : 0,
                    ["$notes"] = "Manual Proof M1 deterministic dev seed; no product images.",
                    ["$createdAt"] = now
                });
        }

        tx.Commit();

        long inserted = ScalarLong(connection, "SELECT COUNT(*) FROM products WHERE sku LIKE 'DEV-GMHW-%';");
        long uniqueSkus = ScalarLong(connection, "SELECT COUNT(DISTINCT sku) FROM products WHERE sku LIKE 'DEV-GMHW-%';");
        long uniqueNames = ScalarLong(connection, "SELECT COUNT(DISTINCT name) FROM products WHERE sku LIKE 'DEV-GMHW-%';");
        long uniqueBarcodes = ScalarLong(connection, "SELECT COUNT(DISTINCT barcode) FROM products WHERE sku LIKE 'DEV-GMHW-%';");
        if (inserted != RequiredCount ||
            uniqueSkus != RequiredCount ||
            uniqueNames != RequiredCount ||
            uniqueBarcodes != RequiredCount)
        {
            return Fail(
                $"Seed verification failed. Inserted={inserted}; Unique SKUs={uniqueSkus}; Unique names={uniqueNames}; Unique barcodes={uniqueBarcodes}.");
        }

        Console.WriteLine("DB path: " + paths.DatabasePath);
        Console.WriteLine("Seed profile: Philippine general merchandise + hardware store");
        Console.WriteLine("Seeded products: " + inserted);
        Console.WriteLine("Unique SKUs: " + uniqueSkus);
        Console.WriteLine("Unique names: " + uniqueNames);
        Console.WriteLine("Unique barcodes: " + uniqueBarcodes);
        Console.WriteLine("Categories used: " + products.Select(p => p.Category).Distinct().Count());
        Console.WriteLine("Brands/generic labels used: " + products.Select(p => p.Brand).Distinct().Count());
        Console.WriteLine("Tracked inventory items: " + products.Count(p => p.TrackStock));
        Console.WriteLine("Low stock items: " + products.Count(p => p.TrackStock && p.StockOnHand > 0 && p.StockOnHand <= p.ReorderLevel));
        Console.WriteLine("Out of stock items: " + products.Count(p => p.TrackStock && p.StockOnHand == 0));
        Console.WriteLine("No product images, image paths, image URLs, or media fields were seeded.");
        return 0;
    }

    private static string EnsureUnit(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string unit)
    {
        string name = UnitName(unit);
        int allowsDecimal = unit is "kg" or "g" or "liter" or "ml" or "meter" ? 1 : 0;

        Execute(
            connection,
            transaction,
            "INSERT OR IGNORE INTO units (id, name, abbreviation, allows_decimal_quantity, is_active) VALUES ($id, $name, $abbr, $decimal, 1);",
            new Dictionary<string, object?>
            {
                ["$id"] = StableGuid("unit:" + unit),
                ["$name"] = name,
                ["$abbr"] = unit,
                ["$decimal"] = allowsDecimal
            });

        string actualId = RequiredScalarString(
            connection,
            transaction,
            """
            SELECT id
            FROM units
            WHERE lower(name) = lower($name)
            LIMIT 1;
            """,
            new Dictionary<string, object?>
            {
                ["$name"] = name
            },
            "unit",
            name);

        Execute(
            connection,
            transaction,
            "UPDATE units SET is_active = 1, abbreviation = COALESCE(abbreviation, $abbr), allows_decimal_quantity = $decimal WHERE id = $id;",
            new Dictionary<string, object?>
            {
                ["$id"] = actualId,
                ["$abbr"] = unit,
                ["$decimal"] = allowsDecimal
            });

        return actualId;
    }

    private static string EnsureCategory(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string category)
    {
        Execute(
            connection,
            transaction,
            "INSERT OR IGNORE INTO categories (id, name, is_active) VALUES ($id, $name, 1);",
            new Dictionary<string, object?>
            {
                ["$id"] = StableGuid("category:" + category),
                ["$name"] = category
            });

        string actualId = RequiredScalarString(
            connection,
            transaction,
            """
            SELECT id
            FROM categories
            WHERE lower(name) = lower($name)
            LIMIT 1;
            """,
            new Dictionary<string, object?>
            {
                ["$name"] = category
            },
            "category",
            category);

        Execute(
            connection,
            transaction,
            "UPDATE categories SET is_active = 1 WHERE id = $id;",
            new Dictionary<string, object?>
            {
                ["$id"] = actualId
            });

        return actualId;
    }

    private static string EnsureBrand(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string brand)
    {
        Execute(
            connection,
            transaction,
            "INSERT OR IGNORE INTO brands (id, name, is_active) VALUES ($id, $name, 1);",
            new Dictionary<string, object?>
            {
                ["$id"] = StableGuid("brand:" + brand),
                ["$name"] = brand
            });

        string actualId = RequiredScalarString(
            connection,
            transaction,
            """
            SELECT id
            FROM brands
            WHERE lower(name) = lower($name)
            LIMIT 1;
            """,
            new Dictionary<string, object?>
            {
                ["$name"] = brand
            },
            "brand",
            brand);

        Execute(
            connection,
            transaction,
            "UPDATE brands SET is_active = 1 WHERE id = $id;",
            new Dictionary<string, object?>
            {
                ["$id"] = actualId
            });

        return actualId;
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
        if (!string.IsNullOrWhiteSpace(programData) &&
            fullPath.StartsWith(Path.GetFullPath(programData), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to touch ProgramData production path: " + fullPath);
        }
    }

    private static string FindProjectRoot(string start)
    {
        DirectoryInfo? current = new(start);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not find Salryn.sln from " + start);
    }

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string RequiredScalarString(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        IReadOnlyDictionary<string, object?> parameters,
        string label,
        string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var p in parameters)
        {
            command.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);
        }

        object? result = command.ExecuteScalar();
        string? actual = result?.ToString();
        if (string.IsNullOrWhiteSpace(actual))
        {
            throw new InvalidOperationException("Unable to resolve seeded " + label + " id for: " + value);
        }

        return actual;
    }

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        if (parameters is not null)
        {
            foreach (var p in parameters)
            {
                command.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);
            }
        }

        command.ExecuteNonQuery();
    }

    private static string StableGuid(string key) =>
        new Guid(MD5.HashData(Encoding.UTF8.GetBytes("Salryn.DevData." + key))).ToString("D");

    private static string UnitName(string u) => u switch
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
        _ => u
    };

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

internal sealed record SeedCategory(
    string Category,
    string Code,
    string Unit,
    string[] Brands,
    string[] Bases,
    string[] Specs);

internal static class SeedCatalog
{
    private const string SeedSkuPrefix = "DEV-GMHW-";
    private static readonly string[] Specs = "1/2in|3/4in|1in|2in|3in|small|medium|large|500ml|1L".Split('|');
    private static readonly SeedCategory[] Categories =
    {
        C("Hardware", "HW", "pc", "TibayPro|Bohol Hardware|Generic Hardware|Fortress|Matibay", "GI wire|Concrete nails|Wood screws|Machine bolts|Hinges|Padlock|Door knob|L angle bracket|Blind rivets|Roofing screw"),
        C("Electrical", "EL", "pc", "VoltSafe|Omni-style|BrightLite|Generic Electrical|CircuitPro", "LED bulb|Electrical tape|Circuit breaker|Extension outlet|Wire connector|Switch plate|Duplex outlet|THHN wire|Lamp holder|Cable tie"),
        C("Plumbing", "PL", "pc", "TubigPro|PVC Master|Generic Plumbing|AquaLine|BluePipe", "PVC elbow|PVC pipe|PVC tee|Faucet|P-trap|Flexible hose|Ball valve|Pipe clamp|Teflon tape|Floor drain"),
        C("Paint and Supplies", "PA", "liter", "ColorHaus|PrimeCoat|Generic Paint|WallPro|BrushMate", "Flat latex paint|Gloss enamel|Paint roller|Paint brush|Thinner|Primer|Skim coat|Masking tape|Paint tray|Sandpaper"),
        C("Tools", "TO", "pc", "ToolMax|Mekaniko|Generic Tools|HandyPro|SteelGrip", "Hammer|Screwdriver Phillips|Screwdriver flat|Pliers|Adjustable wrench|Tape measure|Cutter knife|Hacksaw frame|Level bar|Allen key set"),
        C("Fasteners", "FA", "box", "FixIt|Generic Fasteners|BoltHaus|NailPro|ScrewMaster", "Drywall screw|Concrete nail|Finishing nail|Hex nut|Flat washer|Self-drilling screw|U-bolt|Anchor bolt|Wood screw|Staple wire"),
        C("Construction Materials", "CM", "bag", "BuildRite|Generic Materials|MasonPro|GravelMix|Cemento", "Portland cement|Tile adhesive|Grout|Concrete patch|Waterproofing powder|Wall putty|Mortar mix|Pebble wash|Fine sand|Hollow block"),
        C("Automotive", "AU", "bottle", "MotorCare|RoadPro|Generic Auto|LubeMax|DriveSafe", "Motor oil|Brake fluid|Coolant|WD-40 style lubricant|Car shampoo|Tire black|Spark plug|Battery terminal|Microfiber cloth|Wiper blade"),
        C("Home Cleaning", "HC", "bottle", "Linaw|CleanMate|Generic Cleaning|BahayFresh|Sparkle", "Dishwashing liquid|Bleach|Floor cleaner|Glass cleaner|Toilet cleaner|Mop head|Scrub pad|Trash bag|Disinfectant spray|All-purpose cleaner"),
        C("Kitchenware", "KW", "pc", "KusinaPro|HomeBasics|Generic Kitchen|CookMate|SteelHome", "Plastic basin|Food container|Stainless ladle|Frying pan|Knife|Chopping board|Measuring cup|Dish rack|Water dipper|Rice ladle"),
        C("School and Office Supplies", "SO", "pc", "Eskwela|PaperWay|Generic Office|WritePro|ClassMate", "Notebook|Ballpen|Bond paper|Pencil|Eraser|Correction tape|Folder|Envelope|Marker|Stapler"),
        C("Personal Care", "PC", "sachet", "Alaga|FreshCare|Generic Care|BathMate|SmilePro", "Toothpaste|Shampoo|Conditioner|Bath soap|Deodorant|Cotton buds|Alcohol|Face towel|Baby powder|Hair gel"),
        C("Beverages", "BV", "bottle", "TubigFresh|CafePinoy|Generic Beverage|CoolSip|JuiceKo", "Bottled water|Soft drink|Iced tea|Instant coffee|Powdered juice|Energy drink|Milk drink|Coconut water|Chocolate drink|Tea bags"),
        C("Snacks", "SN", "pack", "Meryenda|ChippyStyle|Generic Snacks|CrunchPinoy|SweetPack", "Potato chips|Corn chips|Crackers|Cookies|Peanuts|Chocolate bar|Wafer|Cupcake|Biscuit|Cheese curls"),
        C("Canned Goods", "CG", "can", "LutongBahay|FiestaCan|Generic Canned|UlamReady|PinoyCan", "Sardines|Corned beef|Meat loaf|Tuna flakes|Vienna sausage|Pork and beans|Liver spread|Mackerel|Luncheon meat|Canned corn"),
        C("Rice and Staples", "RS", "kg", "AniHarvest|PalayPrime|Generic Staples|Bohol Rice|SariSari", "Rice|Brown sugar|White sugar|Flour|Cornstarch|Salt|Cooking oil|Vinegar|Soy sauce|Pancit canton pack"),
        C("Noodles", "NO", "pack", "QuickNoodle|SabawTime|Generic Noodles|MeriendaCup|PinoyNoodle", "Instant noodles|Cup noodles|Pancit canton|Bihon|Sotanghon|Miswa|Spaghetti pasta|Macaroni|Egg noodles|Ramen pack"),
        C("Condiments", "CO", "bottle", "Sawsawan|FlavorHaus|Generic Condiments|LasaPinoy|SpiceMate", "Soy sauce|Vinegar|Fish sauce|Banana ketchup|Hot sauce|Oyster sauce|Mayonnaise|Tomato sauce|Pepper|Magic-style seasoning"),
        C("Laundry", "LA", "pack", "Linis|WashPro|Generic Laundry|Bango|FoamCare", "Laundry powder|Fabric conditioner|Bar detergent|Liquid detergent|Stain remover|Clothes pin|Laundry net|Hanger|Bleach sachet|Basin soap"),
        C("Pet and Garden", "PG", "pc", "AlagaPet|GreenBahay|Generic Garden|GrowMate|PetPinoy", "Dog food|Cat food|Garden hose|Plant pot|Fertilizer|Garden trowel|Sprayer|Plant tie|Bird seed|Pet shampoo")
    };

    public static IReadOnlyList<SeedProduct> Generate(int count)
    {
        List<SeedProduct> products = new(count);
        int i = 1;
        foreach (SeedCategory c in Categories)
        {
            foreach (string b in c.Bases)
            {
                foreach (string s in c.Specs)
                {
                    string brand = c.Brands[(i - 1) % c.Brands.Length];
                    int cost = Cost(c.Code, i);
                    bool tracked = i % 13 != 0;
                    int reorder = tracked ? 5 + i % 12 : 0;
                    int stock = tracked
                        ? i % 17 == 0
                            ? 0
                            : i % 11 == 0
                                ? Math.Max(1, reorder - 1)
                                : reorder + 10 + i % 90
                        : 0;

                    products.Add(new SeedProduct(
                        SeedSkuPrefix + i.ToString("D4"),
                        (4_809_000_000_000L + i).ToString(),
                        brand + " " + b + " " + s,
                        c.Category,
                        brand,
                        c.Unit,
                        cost,
                        cost + Math.Max(100, cost / 4 + (i % 7) * 25),
                        stock,
                        reorder,
                        tracked,
                        i % 97 != 0));

                    if (products.Count == count)
                    {
                        return products;
                    }

                    i++;
                }
            }
        }

        return products;
    }

    public static void Validate(IReadOnlyList<SeedProduct> products, int expected)
    {
        if (products.Count != expected)
        {
            throw new InvalidOperationException($"Seed generator produced {products.Count}; expected {expected}.");
        }

        Unique(products.Select(p => p.Sku), "SKU");
        Unique(products.Select(p => p.Name), "product name");
        Unique(products.Select(p => p.Barcode), "barcode");
        Unique(products.Select(p => p.Category + "|" + p.Name), "category/name");
        if (products.Any(p => p.CostPriceCents >= p.SellingPriceCents))
        {
            throw new InvalidOperationException("Every seed product must have cost below selling price.");
        }

        if (products.Any(p => p.Name.Contains("Item ", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Generic Item filler is forbidden.");
        }
    }

    private static void Unique(IEnumerable<string> values, string label)
    {
        string? duplicate = values
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .FirstOrDefault();
        if (duplicate is not null)
        {
            throw new InvalidOperationException("Duplicate " + label + ": " + duplicate);
        }
    }

    private static int Cost(string code, int i) => (code switch
    {
        "TO" => 120 + i % 45 * 35,
        "CM" => 80 + i % 35 * 45,
        "AU" => 90 + i % 40 * 30,
        "HW" or "EL" or "PL" or "PA" or "FA" => 15 + i % 60 * 18,
        _ => 5 + i % 95 * 6
    }) * 100;

    private static SeedCategory C(string category, string code, string unit, string brands, string bases) =>
        new(category, code, unit, brands.Split('|'), bases.Split('|'), Specs);
}

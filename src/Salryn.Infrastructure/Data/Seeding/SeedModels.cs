namespace Salryn.Infrastructure.Data.Seeding;

public sealed record SeedRole(
    string Id,
    string Name,
    bool IsSystemRole);

public sealed record SeedPermission(
    string RoleId,
    string Permission);

public sealed record SeedUnit(
    string Id,
    string Name,
    string? Abbreviation,
    bool AllowsDecimalQuantity);

public sealed record SeedCategory(
    string Id,
    string Name);

public sealed record SeedBrand(
    string Id,
    string Name);

public sealed record SeedReceiptSequence(
    string Id,
    string Prefix,
    int Year,
    int CurrentNumber,
    int PaddingDigits);

public sealed record FirstRunStoreDefaults(
    string CurrencyLabel,
    int ReceiptWidthMm,
    bool TaxModeEnabled,
    bool ShiftModeEnabled,
    bool AllowNegativeStock);

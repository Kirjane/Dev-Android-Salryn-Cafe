namespace Salryn.Core.Models;

public sealed record MenuCategoryConfig(
    Guid Id,
    string Name,
    bool IsActive);

public sealed record MenuItemConfig(
    Guid ProductId,
    Guid? CategoryId,
    string Name,
    string? Description,
    Guid UnitId,
    string? Sku,
    string? Barcode,
    long SellingPriceCents,
    long? CostPriceCents,
    bool IsActive,
    bool IsTemporarilyUnavailable);

public sealed record MenuVariantConfig(
    string Id,
    Guid ProductId,
    string Name,
    string? Sku,
    string? Barcode,
    long SellingPriceCents,
    long? CostPriceCents,
    bool IsActive,
    bool IsTemporarilyUnavailable,
    int DisplayOrder);

public sealed record MenuModifierGroupConfig(
    string Id,
    string Name,
    bool IsRequired,
    int MinSelections,
    int MaxSelections,
    bool IsActive,
    int DisplayOrder);

public sealed record MenuModifierOptionConfig(
    string Id,
    string GroupId,
    string Name,
    long PriceDeltaCents,
    bool IsActive,
    int DisplayOrder);

public sealed record MenuModifierLinkConfig(
    string OwnerId,
    string GroupId,
    int DisplayOrder);

public sealed record MenuPublicationSummary(
    string RevisionId,
    Guid StoreId,
    int RevisionNumber,
    string ContentHash,
    string PublicationHash,
    string Status,
    DateTimeOffset PublishedAtUtc);

public sealed record MenuCatalogDraft(
    IReadOnlyList<MenuCategoryConfig> Categories,
    IReadOnlyList<MenuItemConfig> Items,
    IReadOnlyList<MenuVariantConfig> Variants,
    IReadOnlyList<MenuModifierGroupConfig> ModifierGroups,
    IReadOnlyList<MenuModifierOptionConfig> ModifierOptions,
    IReadOnlyList<MenuModifierLinkConfig> ItemModifierGroups,
    IReadOnlyList<MenuModifierLinkConfig> VariantModifierGroups,
    MenuPublicationSummary? CurrentPublication);

public sealed record MenuPublicationCategorySnapshot(
    Guid CategoryId,
    string Name,
    int DisplayOrder);

public sealed record MenuPublicationItemSnapshot(
    Guid ProductId,
    Guid? CategoryId,
    string Name,
    string? Description,
    Guid UnitId,
    string? UnitName,
    string? Sku,
    string? Barcode,
    long SellingPriceCents,
    long? CostPriceCents,
    bool IsTemporarilyUnavailable,
    int DisplayOrder);

public sealed record MenuPublicationVariantSnapshot(
    string VariantId,
    Guid ProductId,
    string Name,
    string? Sku,
    string? Barcode,
    long SellingPriceCents,
    long? CostPriceCents,
    bool IsTemporarilyUnavailable,
    int DisplayOrder);

public sealed record MenuPublicationModifierGroupSnapshot(
    string GroupId,
    string Name,
    bool IsRequired,
    int MinSelections,
    int MaxSelections,
    int DisplayOrder);

public sealed record MenuPublicationModifierOptionSnapshot(
    string OptionId,
    string GroupId,
    string Name,
    long PriceDeltaCents,
    int DisplayOrder);

public sealed record MenuPublicationLinkSnapshot(
    string OwnerId,
    string GroupId,
    int DisplayOrder);

public sealed record MenuPublicationSnapshot(
    string RevisionId,
    Guid StoreId,
    int RevisionNumber,
    string ContentHash,
    string PublicationHash,
    DateTimeOffset PublishedAtUtc,
    IReadOnlyList<MenuPublicationCategorySnapshot> Categories,
    IReadOnlyList<MenuPublicationItemSnapshot> Items,
    IReadOnlyList<MenuPublicationVariantSnapshot> Variants,
    IReadOnlyList<MenuPublicationModifierGroupSnapshot> ModifierGroups,
    IReadOnlyList<MenuPublicationModifierOptionSnapshot> ModifierOptions,
    IReadOnlyList<MenuPublicationLinkSnapshot> ItemModifierGroups,
    IReadOnlyList<MenuPublicationLinkSnapshot> VariantModifierGroups);

public sealed record MenuPublicationApplyResult(
    bool Applied,
    bool Idempotent,
    MenuPublicationSummary CurrentPublication);

public sealed record MenuCategoryMutation(
    Guid? Id,
    string Name,
    bool IsActive);

public sealed record MenuItemMutation(
    Guid? ProductId,
    Guid? CategoryId,
    string Name,
    string? Description,
    Guid UnitId,
    string? Sku,
    string? Barcode,
    long SellingPriceCents,
    long? CostPriceCents,
    bool IsActive,
    bool IsTemporarilyUnavailable);

public sealed record MenuVariantMutation(
    string? Id,
    Guid ProductId,
    string Name,
    string? Sku,
    string? Barcode,
    long SellingPriceCents,
    long? CostPriceCents,
    bool IsActive,
    bool IsTemporarilyUnavailable,
    int DisplayOrder);

public sealed record MenuModifierGroupMutation(
    string? Id,
    string Name,
    bool IsRequired,
    int MinSelections,
    int MaxSelections,
    bool IsActive,
    int DisplayOrder);

public sealed record MenuModifierOptionMutation(
    string? Id,
    string GroupId,
    string Name,
    long PriceDeltaCents,
    bool IsActive,
    int DisplayOrder);

public sealed record MenuModifierLinkMutation(
    string OwnerId,
    IReadOnlyList<string> GroupIds);

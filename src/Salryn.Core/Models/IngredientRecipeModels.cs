namespace Salryn.Core.Models;

public static class IngredientStockUnits
{
    public const string Gram = "Gram";
    public const string Milliliter = "Milliliter";
    public const string Piece = "Piece";

    public static bool IsCanonical(string value) =>
        string.Equals(value, Gram, StringComparison.Ordinal) ||
        string.Equals(value, Milliliter, StringComparison.Ordinal) ||
        string.Equals(value, Piece, StringComparison.Ordinal);
}

public static class RecipeInputUnits
{
    public const string Gram = "Gram";
    public const string Kilogram = "Kilogram";
    public const string Milliliter = "Milliliter";
    public const string Liter = "Liter";
    public const string Piece = "Piece";
}

public static class RecipeBindingTypes
{
    public const string Item = "Item";
    public const string Variant = "Variant";
    public const string ModifierOption = "ModifierOption";
}

public static class IngredientMovementKinds
{
    public const string RecipeConsumption = "RecipeConsumption";
    public const string RecipeConsumptionReversal = "RecipeConsumptionReversal";
}

public static class IngredientConsumptionResults
{
    public const string Applied = "APPLIED";
    public const string NoRecipeApplied = "NO_RECIPE_APPLIED";
}

public sealed record IngredientConfig(
    Guid Id,
    string Name,
    string StockUnit,
    long StockOnHandX1000,
    long ReorderLevelX1000,
    bool IsActive,
    string? Notes);

public sealed record IngredientMutation(
    Guid? Id,
    string Name,
    string StockUnit,
    long ReorderLevelX1000,
    bool IsActive,
    string? Notes);

public sealed record RecipeConfig(
    string Id,
    string Name,
    long YieldX1000,
    bool IsActive);

public sealed record RecipeMutation(
    string? Id,
    string Name,
    long YieldX1000,
    bool IsActive);

public sealed record RecipeLineConfig(
    string RecipeId,
    Guid IngredientId,
    string InputUnit,
    long DeclaredQuantityX1000,
    long NormalizedQuantityX1000,
    int DisplayOrder);

public sealed record RecipeLineMutation(
    string RecipeId,
    Guid IngredientId,
    string InputUnit,
    long DeclaredQuantityX1000,
    int DisplayOrder);

public sealed record RecipeLineUpdateMutation(
    string RecipeId,
    Guid IngredientId,
    string InputUnit,
    long DeclaredQuantityX1000,
    int DisplayOrder);

public sealed record RecipeBindingConfig(
    string BindingType,
    string BindingId,
    string RecipeId);

public sealed record RecipeBookPublicationSummary(
    string RevisionId,
    Guid StoreId,
    int RevisionNumber,
    string ContentHash,
    string PublicationHash,
    string Status,
    DateTimeOffset PublishedAtUtc);

public sealed record IngredientRecipeDraft(
    IReadOnlyList<IngredientConfig> Ingredients,
    IReadOnlyList<RecipeConfig> Recipes,
    IReadOnlyList<RecipeLineConfig> Lines,
    IReadOnlyList<RecipeBindingConfig> ItemBindings,
    IReadOnlyList<RecipeBindingConfig> VariantBindings,
    IReadOnlyList<RecipeBindingConfig> ModifierOptionBindings,
    RecipeBookPublicationSummary? CurrentPublication);

public sealed record RecipeBookIngredientSnapshot(
    Guid IngredientId,
    string Name,
    string StockUnit,
    long ReorderLevelX1000,
    int DisplayOrder);

public sealed record RecipeBookRecipeSnapshot(
    string RecipeId,
    string RecipeRevisionId,
    string Name,
    long YieldX1000,
    int DisplayOrder);

public sealed record RecipeBookLineSnapshot(
    string RecipeRevisionId,
    string RecipeId,
    Guid IngredientId,
    string StockUnit,
    long NormalizedQuantityX1000,
    int DisplayOrder);

public sealed record RecipeBookBindingSnapshot(
    string BindingType,
    string BindingId,
    string RecipeId,
    string RecipeRevisionId);

public sealed record RecipeBookPublicationSnapshot(
    string RevisionId,
    Guid StoreId,
    int RevisionNumber,
    string ContentHash,
    string PublicationHash,
    DateTimeOffset PublishedAtUtc,
    IReadOnlyList<RecipeBookIngredientSnapshot> Ingredients,
    IReadOnlyList<RecipeBookRecipeSnapshot> Recipes,
    IReadOnlyList<RecipeBookLineSnapshot> Lines,
    IReadOnlyList<RecipeBookBindingSnapshot> ItemBindings,
    IReadOnlyList<RecipeBookBindingSnapshot> VariantBindings,
    IReadOnlyList<RecipeBookBindingSnapshot> ModifierOptionBindings);

public sealed record RecipeBookPublicationApplyResult(
    bool Applied,
    bool Idempotent,
    RecipeBookPublicationSummary CurrentPublication);

public sealed record SaleIngredientConsumptionLineEvidence(
    string Id,
    Guid SaleId,
    Guid SaleItemId,
    Guid IngredientId,
    string RecipeId,
    string RecipeRevisionId,
    string SourceBindingType,
    string SourceBindingId,
    long QuantityConsumedX1000,
    string StockUnit);

public sealed record SaleIngredientConsumptionDecision(
    Guid SaleId,
    Guid? SourceSaleOperationId,
    string? RecipeBookRevisionId,
    string DecisionHash,
    string Result,
    int LineCount,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<SaleIngredientConsumptionLineEvidence> Lines);

public sealed record IngredientStockMovementEvidence(
    string Id,
    Guid IngredientId,
    Guid SaleId,
    Guid? SaleItemId,
    string? ConsumptionLineId,
    string? OriginalConsumptionLineId,
    string MovementKind,
    long QuantityDeltaX1000,
    Guid ActorUserId,
    DateTimeOffset CreatedAtUtc);

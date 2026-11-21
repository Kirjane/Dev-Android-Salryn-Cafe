// Synthetic fixture recovered from Cafe Yza 4241ea1e; production uses shared canonical persistence.
namespace Salryn.Cafe.I6ATests;

internal static class C4ProofIds
{
    public static readonly Guid StoreId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    public static readonly Guid DeviceId = Guid.Parse("40000000-0000-0000-0000-000000000002");
    public static readonly Guid ActorUserId = Guid.Parse("40000000-0000-0000-0000-000000000003");
    public static readonly Guid ProductId = Guid.Parse("40000000-0000-0000-0000-000000000004");
    public static readonly Guid OrderId = Guid.Parse("40000000-0000-0000-0000-000000000005");
    public static readonly Guid CancelOrderId = Guid.Parse("40000000-0000-0000-0000-000000000006");
    public static readonly Guid OrderLineId = Guid.Parse("40000000-0000-0000-0000-000000000007");
    public static readonly Guid CancelOrderLineId = Guid.Parse("40000000-0000-0000-0000-000000000008");
    public static readonly Guid CashierSessionId = Guid.Parse("40000000-0000-0000-0000-000000000009");
    public static readonly Guid ItemIngredientId = Guid.Parse("40000000-0000-0000-0000-000000000010");
    public static readonly Guid VariantIngredientId = Guid.Parse("40000000-0000-0000-0000-000000000011");
    public static readonly Guid ModifierIngredientId = Guid.Parse("40000000-0000-0000-0000-000000000012");
    public static readonly Guid DeviceRemote = Guid.Parse("40000000-0000-0000-0000-000000000013");

    public const string MenuRevisionId = "c4-proof-menu-r1";
    public const string RecipeBookRevisionId = "c4-proof-recipe-book-r1";
    public const string ItemRecipeId = "c4-proof-item-recipe";
    public const string ItemRecipeRevisionId = "c4-proof-item-recipe-r1";
    public const string VariantRecipeId = "c4-proof-variant-recipe";
    public const string VariantRecipeRevisionId = "c4-proof-variant-recipe-r1";
    public const string ModifierRecipeId = "c4-proof-modifier-recipe";
    public const string ModifierRecipeRevisionId = "c4-proof-modifier-recipe-r1";
    public const string VariantId = "c4-proof-variant";
    public const string ModifierGroupId = "c4-proof-size-group";
    public const string ModifierOptionId = "c4-proof-extra-option";
}

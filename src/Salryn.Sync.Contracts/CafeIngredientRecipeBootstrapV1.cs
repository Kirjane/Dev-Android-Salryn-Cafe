using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Salryn.Sync.Contracts;

public sealed record CafeIngredientBootstrapIngredientV1(
    Guid Id,
    string Name,
    string StockUnit,
    long StockOnHandX1000,
    long ReorderLevelX1000,
    bool IsActive,
    string? Notes);

public sealed record CafeRecipeBookIngredientV1(
    Guid IngredientId,
    string Name,
    string StockUnit,
    long ReorderLevelX1000,
    int DisplayOrder);

public sealed record CafeRecipeBookRecipeV1(
    string RecipeId,
    string RecipeRevisionId,
    string Name,
    long YieldX1000,
    int DisplayOrder);

public sealed record CafeRecipeBookLineV1(
    string RecipeRevisionId,
    string RecipeId,
    Guid IngredientId,
    string StockUnit,
    long NormalizedQuantityX1000,
    int DisplayOrder);

public sealed record CafeRecipeBookBindingV1(
    string BindingType,
    string BindingId,
    string RecipeId,
    string RecipeRevisionId);

public sealed record CafeRecipeBookPublicationV1(
    string RevisionId,
    Guid StoreId,
    int RevisionNumber,
    string ContentHash,
    string PublicationHash,
    DateTimeOffset PublishedAtUtc,
    IReadOnlyList<CafeRecipeBookIngredientV1> Ingredients,
    IReadOnlyList<CafeRecipeBookRecipeV1> Recipes,
    IReadOnlyList<CafeRecipeBookLineV1> Lines,
    IReadOnlyList<CafeRecipeBookBindingV1> Bindings);

public sealed record CafeIngredientRecipeBootstrapV1(
    Guid CentralInstanceId,
    Guid StoreId,
    long BaselineServerSequence,
    Guid SnapshotId,
    string SnapshotHash,
    IReadOnlyList<CafeIngredientBootstrapIngredientV1> Ingredients,
    CafeRecipeBookPublicationV1? RecipeBook);

public static class CafeIngredientRecipeBootstrapProtocolV1
{
    public static CafeIngredientRecipeBootstrapV1 Create(
        Guid centralInstanceId,
        Guid storeId,
        long baselineServerSequence,
        IReadOnlyList<CafeIngredientBootstrapIngredientV1> ingredients,
        CafeRecipeBookPublicationV1? recipeBook)
    {
        if (centralInstanceId == Guid.Empty || storeId == Guid.Empty || baselineServerSequence < 0)
            throw new ArgumentException("Cafe bootstrap requires central/store identity and non-negative baseline.");
        ArgumentNullException.ThrowIfNull(ingredients);

        byte[] canonical = CanonicalBytes(
            centralInstanceId,
            storeId,
            baselineServerSequence,
            ingredients,
            recipeBook);
        byte[] hash = SHA256.HashData(canonical);
        string snapshotHash = Convert.ToHexString(hash).ToLowerInvariant();
        byte[] idBytes = hash[..16];
        idBytes[6] = (byte)((idBytes[6] & 0x0F) | 0x50);
        idBytes[8] = (byte)((idBytes[8] & 0x3F) | 0x80);

        return new CafeIngredientRecipeBootstrapV1(
            centralInstanceId,
            storeId,
            baselineServerSequence,
            new Guid(idBytes),
            snapshotHash,
            ingredients
                .OrderBy(x => x.Id)
                .ToArray(),
            Normalize(recipeBook));
    }

    public static bool Validate(CafeIngredientRecipeBootstrapV1 snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        CafeIngredientRecipeBootstrapV1 expected = Create(
            snapshot.CentralInstanceId,
            snapshot.StoreId,
            snapshot.BaselineServerSequence,
            snapshot.Ingredients,
            snapshot.RecipeBook);
        return expected.SnapshotId == snapshot.SnapshotId &&
            string.Equals(expected.SnapshotHash, snapshot.SnapshotHash, StringComparison.Ordinal);
    }

    private static CafeRecipeBookPublicationV1? Normalize(CafeRecipeBookPublicationV1? book)
    {
        if (book is null) return null;
        return book with
        {
            Ingredients = book.Ingredients.OrderBy(x => x.DisplayOrder).ThenBy(x => x.IngredientId).ToArray(),
            Recipes = book.Recipes.OrderBy(x => x.DisplayOrder).ThenBy(x => x.RecipeId, StringComparer.Ordinal).ToArray(),
            Lines = book.Lines.OrderBy(x => x.RecipeId, StringComparer.Ordinal).ThenBy(x => x.DisplayOrder).ThenBy(x => x.IngredientId).ToArray(),
            Bindings = book.Bindings.OrderBy(x => x.BindingType, StringComparer.Ordinal).ThenBy(x => x.BindingId, StringComparer.Ordinal).ToArray()
        };
    }

    private static byte[] CanonicalBytes(
        Guid centralInstanceId,
        Guid storeId,
        long baselineServerSequence,
        IReadOnlyList<CafeIngredientBootstrapIngredientV1> ingredients,
        CafeRecipeBookPublicationV1? recipeBook)
    {
        using MemoryStream stream = new();
        WriteGuid(stream, centralInstanceId);
        WriteGuid(stream, storeId);
        WriteInt64(stream, baselineServerSequence);

        CafeIngredientBootstrapIngredientV1[] orderedIngredients =
            ingredients.OrderBy(x => x.Id).ToArray();
        WriteInt32(stream, orderedIngredients.Length);
        foreach (CafeIngredientBootstrapIngredientV1 x in orderedIngredients)
        {
            WriteGuid(stream, x.Id);
            WriteString(stream, x.Name);
            WriteString(stream, x.StockUnit);
            WriteInt64(stream, x.StockOnHandX1000);
            WriteInt64(stream, x.ReorderLevelX1000);
            WriteInt32(stream, x.IsActive ? 1 : 0);
            WriteString(stream, x.Notes);
        }

        WriteInt32(stream, recipeBook is null ? 0 : 1);
        if (recipeBook is not null)
        {
            CafeRecipeBookPublicationV1 b = Normalize(recipeBook)!;
            WriteString(stream, b.RevisionId);
            WriteGuid(stream, b.StoreId);
            WriteInt32(stream, b.RevisionNumber);
            WriteString(stream, b.ContentHash);
            WriteString(stream, b.PublicationHash);
            WriteString(stream, b.PublishedAtUtc.ToUniversalTime().ToString("O"));

            WriteInt32(stream, b.Ingredients.Count);
            foreach (CafeRecipeBookIngredientV1 x in b.Ingredients)
            {
                WriteGuid(stream, x.IngredientId); WriteString(stream, x.Name);
                WriteString(stream, x.StockUnit); WriteInt64(stream, x.ReorderLevelX1000);
                WriteInt32(stream, x.DisplayOrder);
            }

            WriteInt32(stream, b.Recipes.Count);
            foreach (CafeRecipeBookRecipeV1 x in b.Recipes)
            {
                WriteString(stream, x.RecipeId); WriteString(stream, x.RecipeRevisionId);
                WriteString(stream, x.Name); WriteInt64(stream, x.YieldX1000);
                WriteInt32(stream, x.DisplayOrder);
            }

            WriteInt32(stream, b.Lines.Count);
            foreach (CafeRecipeBookLineV1 x in b.Lines)
            {
                WriteString(stream, x.RecipeRevisionId); WriteString(stream, x.RecipeId);
                WriteGuid(stream, x.IngredientId); WriteString(stream, x.StockUnit);
                WriteInt64(stream, x.NormalizedQuantityX1000); WriteInt32(stream, x.DisplayOrder);
            }

            WriteInt32(stream, b.Bindings.Count);
            foreach (CafeRecipeBookBindingV1 x in b.Bindings)
            {
                WriteString(stream, x.BindingType); WriteString(stream, x.BindingId);
                WriteString(stream, x.RecipeId); WriteString(stream, x.RecipeRevisionId);
            }
        }

        return stream.ToArray();
    }

    private static void WriteGuid(Stream stream, Guid value) => WriteString(stream, value.ToString("D"));
    private static void WriteString(Stream stream, string? value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        WriteInt32(stream, bytes.Length);
        stream.Write(bytes);
    }
    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }
    private static void WriteInt64(Stream stream, long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        stream.Write(bytes);
    }
}

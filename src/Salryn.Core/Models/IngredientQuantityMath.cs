using Salryn.Core.Common;

namespace Salryn.Core.Models;

public static class IngredientQuantityMath
{
    public static Result<long> NormalizeToStockX1000(
        string stockUnit,
        string inputUnit,
        long declaredQuantityX1000)
    {
        if (!IngredientStockUnits.IsCanonical(stockUnit))
        {
            return Result<long>.Fail(Error.Validation(
                "Ingredients.InvalidStockUnit",
                "Ingredient stock unit must be Gram, Milliliter, or Piece."));
        }

        if (declaredQuantityX1000 <= 0)
        {
            return Result<long>.Fail(Error.Validation(
                "Recipes.QuantityRequired",
                "Recipe quantity must be greater than zero."));
        }

        try
        {
            Int128 factor = ResolveFactor(stockUnit, inputUnit);
            Int128 normalized = checked((Int128)declaredQuantityX1000 * factor);
            if (normalized <= 0 || normalized > long.MaxValue)
            {
                throw new OverflowException();
            }

            return Result<long>.Success((long)normalized);
        }
        catch (InvalidOperationException ex)
        {
            return Result<long>.Fail(Error.Validation(
                "Recipes.UnitFamilyMismatch",
                ex.Message));
        }
        catch (OverflowException)
        {
            return Result<long>.Fail(Error.Validation(
                "Recipes.QuantityOverflow",
                "Recipe quantity conversion exceeds supported x1000 range."));
        }
    }

    public static Result<long> ScaleForSoldQuantity(
        long normalizedRecipeLineX1000,
        long soldQuantityX1000,
        long recipeYieldX1000)
    {
        if (normalizedRecipeLineX1000 <= 0 || soldQuantityX1000 <= 0 || recipeYieldX1000 <= 0)
        {
            return Result<long>.Fail(Error.Validation(
                "Recipes.InvalidScaleInput",
                "Recipe line, sold quantity, and yield must all be greater than zero."));
        }

        try
        {
            Int128 numerator = checked((Int128)normalizedRecipeLineX1000 * soldQuantityX1000);
            Int128 rounded = DivideRoundHalfAwayFromZero(numerator, recipeYieldX1000);
            if (rounded <= 0)
            {
                return Result<long>.Fail(Error.Validation(
                    "Recipes.ZeroConsumption",
                    "Active recipe line rounds to zero consumption."));
            }

            if (rounded > long.MaxValue)
            {
                throw new OverflowException();
            }

            return Result<long>.Success((long)rounded);
        }
        catch (OverflowException)
        {
            return Result<long>.Fail(Error.Validation(
                "Recipes.QuantityOverflow",
                "Recipe consumption calculation exceeds supported x1000 range."));
        }
    }

    private static Int128 ResolveFactor(string stockUnit, string inputUnit)
    {
        if (string.Equals(stockUnit, IngredientStockUnits.Gram, StringComparison.Ordinal))
        {
            if (string.Equals(inputUnit, RecipeInputUnits.Gram, StringComparison.Ordinal)) return 1;
            if (string.Equals(inputUnit, RecipeInputUnits.Kilogram, StringComparison.Ordinal)) return 1000;
            throw new InvalidOperationException("Mass ingredient accepts only Gram or Kilogram recipe input.");
        }

        if (string.Equals(stockUnit, IngredientStockUnits.Milliliter, StringComparison.Ordinal))
        {
            if (string.Equals(inputUnit, RecipeInputUnits.Milliliter, StringComparison.Ordinal)) return 1;
            if (string.Equals(inputUnit, RecipeInputUnits.Liter, StringComparison.Ordinal)) return 1000;
            throw new InvalidOperationException("Volume ingredient accepts only Milliliter or Liter recipe input.");
        }

        if (string.Equals(inputUnit, RecipeInputUnits.Piece, StringComparison.Ordinal)) return 1;
        throw new InvalidOperationException("Piece ingredient accepts only Piece recipe input.");
    }

    private static Int128 DivideRoundHalfAwayFromZero(Int128 numerator, Int128 denominator)
    {
        Int128 quotient = numerator / denominator;
        Int128 remainder = numerator % denominator;
        if (remainder == 0)
        {
            return quotient;
        }

        Int128 absoluteRemainder = Int128.Abs(remainder);
        Int128 absoluteDenominator = Int128.Abs(denominator);
        if (checked(absoluteRemainder * 2) >= absoluteDenominator)
        {
            quotient += numerator >= 0 == denominator >= 0 ? 1 : -1;
        }

        return quotient;
    }
}

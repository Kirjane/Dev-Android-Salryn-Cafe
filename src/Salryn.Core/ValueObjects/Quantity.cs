using Salryn.Core.Common;

namespace Salryn.Core.ValueObjects;

public readonly struct Quantity : IEquatable<Quantity>, IComparable<Quantity>
{
    private Quantity(long valueX1000)
    {
        ValueX1000 = valueX1000;
    }

    public long ValueX1000 { get; }

    public static Quantity Zero => new Quantity(0);

    public static Result<Quantity> FromX1000(long valueX1000)
    {
        if (valueX1000 < 0)
        {
            return Result<Quantity>.Fail("Quantity cannot be negative.");
        }

        return Result<Quantity>.Success(new Quantity(valueX1000));
    }

    public static Result<Quantity> FromUnits(decimal units)
    {
        if (units < 0)
        {
            return Result<Quantity>.Fail("Quantity cannot be negative.");
        }

        var scaled = checked((long)Math.Round(units * 1000m, 0, MidpointRounding.AwayFromZero));
        return Result<Quantity>.Success(new Quantity(scaled));
    }

    public decimal ToUnits()
    {
        return ValueX1000 / 1000m;
    }

    public int CompareTo(Quantity other)
    {
        return ValueX1000.CompareTo(other.ValueX1000);
    }

    public bool Equals(Quantity other)
    {
        return ValueX1000 == other.ValueX1000;
    }

    public override bool Equals(object? obj)
    {
        return obj is Quantity other && Equals(other);
    }

    public override int GetHashCode()
    {
        return ValueX1000.GetHashCode();
    }

    public override string ToString()
    {
        return ToUnits().ToString("0.###");
    }

    public static Quantity operator +(Quantity left, Quantity right)
    {
        return new Quantity(checked(left.ValueX1000 + right.ValueX1000));
    }

    public static Quantity operator -(Quantity left, Quantity right)
    {
        var result = checked(left.ValueX1000 - right.ValueX1000);

        if (result < 0)
        {
            throw new InvalidOperationException("Quantity result cannot be negative.");
        }

        return new Quantity(result);
    }

    public static bool operator ==(Quantity left, Quantity right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Quantity left, Quantity right)
    {
        return !left.Equals(right);
    }

    public static bool operator >(Quantity left, Quantity right)
    {
        return left.ValueX1000 > right.ValueX1000;
    }

    public static bool operator <(Quantity left, Quantity right)
    {
        return left.ValueX1000 < right.ValueX1000;
    }

    public static bool operator >=(Quantity left, Quantity right)
    {
        return left.ValueX1000 >= right.ValueX1000;
    }

    public static bool operator <=(Quantity left, Quantity right)
    {
        return left.ValueX1000 <= right.ValueX1000;
    }
}

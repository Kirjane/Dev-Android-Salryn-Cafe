namespace Salryn.Core.ValueObjects;

public readonly struct Money : IEquatable<Money>, IComparable<Money>
{
    private Money(long cents)
    {
        Cents = cents;
    }

    public long Cents { get; }

    public static Money Zero => new Money(0);

    public static Money FromCents(long cents)
    {
        return new Money(cents);
    }

    public static Money FromPesos(decimal amount)
    {
        var cents = checked((long)Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
        return new Money(cents);
    }

    public decimal ToPesos()
    {
        return Cents / 100m;
    }

    public int CompareTo(Money other)
    {
        return Cents.CompareTo(other.Cents);
    }

    public bool Equals(Money other)
    {
        return Cents == other.Cents;
    }

    public override bool Equals(object? obj)
    {
        return obj is Money other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Cents.GetHashCode();
    }

    public override string ToString()
    {
        return $"PHP {ToPesos():0.00}";
    }

    public static Money operator +(Money left, Money right)
    {
        return new Money(checked(left.Cents + right.Cents));
    }

    public static Money operator -(Money left, Money right)
    {
        return new Money(checked(left.Cents - right.Cents));
    }

    public static Money operator *(Money money, int multiplier)
    {
        return new Money(checked(money.Cents * multiplier));
    }

    public static bool operator ==(Money left, Money right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Money left, Money right)
    {
        return !left.Equals(right);
    }

    public static bool operator >(Money left, Money right)
    {
        return left.Cents > right.Cents;
    }

    public static bool operator <(Money left, Money right)
    {
        return left.Cents < right.Cents;
    }

    public static bool operator >=(Money left, Money right)
    {
        return left.Cents >= right.Cents;
    }

    public static bool operator <=(Money left, Money right)
    {
        return left.Cents <= right.Cents;
    }
}

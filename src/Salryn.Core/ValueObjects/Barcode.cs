using Salryn.Core.Common;

namespace Salryn.Core.ValueObjects;

public readonly struct Barcode : IEquatable<Barcode>
{
    private Barcode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<Barcode> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Result<Barcode>.Fail("Barcode is required.");
        }

        var normalized = raw
            .Trim()
            .Replace("\r", string.Empty)
            .Replace("\n", string.Empty)
            .Replace("\t", string.Empty);

        if (normalized.Length == 0)
        {
            return Result<Barcode>.Fail("Barcode is required.");
        }

        if (normalized.Length > 64)
        {
            return Result<Barcode>.Fail("Barcode is too long.");
        }

        return Result<Barcode>.Success(new Barcode(normalized));
    }

    public bool Equals(Barcode other)
    {
        return string.Equals(Value, other.Value, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is Barcode other && Equals(other);
    }

    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(Value);
    }

    public override string ToString()
    {
        return Value;
    }

    public static bool operator ==(Barcode left, Barcode right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Barcode left, Barcode right)
    {
        return !left.Equals(right);
    }
}

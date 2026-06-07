using Salryn.Core.Common;

namespace Salryn.Core.ValueObjects;

public readonly struct ReceiptNumber : IEquatable<ReceiptNumber>
{
    private ReceiptNumber(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<ReceiptNumber> Create(string prefix, int year, long number, int paddingDigits = 6)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return Result<ReceiptNumber>.Fail("Receipt prefix is required.");
        }

        var normalizedPrefix = prefix.Trim().ToUpperInvariant();

        if (normalizedPrefix.Length > 12)
        {
            return Result<ReceiptNumber>.Fail("Receipt prefix is too long.");
        }

        if (year < 2000 || year > 9999)
        {
            return Result<ReceiptNumber>.Fail("Receipt year is invalid.");
        }

        if (number <= 0)
        {
            return Result<ReceiptNumber>.Fail("Receipt number must be greater than zero.");
        }

        if (paddingDigits < 1 || paddingDigits > 12)
        {
            return Result<ReceiptNumber>.Fail("Receipt padding digits is invalid.");
        }

        foreach (var ch in normalizedPrefix)
        {
            if (!char.IsLetterOrDigit(ch))
            {
                return Result<ReceiptNumber>.Fail("Receipt prefix must use letters and numbers only.");
            }
        }

        var formatted = $"{normalizedPrefix}-{year}-{number.ToString().PadLeft(paddingDigits, '0')}";
        return Result<ReceiptNumber>.Success(new ReceiptNumber(formatted));
    }

    public static Result<ReceiptNumber> FromExisting(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<ReceiptNumber>.Fail("Receipt number is required.");
        }

        var normalized = value.Trim().ToUpperInvariant();

        if (normalized.Length > 64)
        {
            return Result<ReceiptNumber>.Fail("Receipt number is too long.");
        }

        return Result<ReceiptNumber>.Success(new ReceiptNumber(normalized));
    }

    public bool Equals(ReceiptNumber other)
    {
        return string.Equals(Value, other.Value, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is ReceiptNumber other && Equals(other);
    }

    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(Value);
    }

    public override string ToString()
    {
        return Value;
    }

    public static bool operator ==(ReceiptNumber left, ReceiptNumber right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ReceiptNumber left, ReceiptNumber right)
    {
        return !left.Equals(right);
    }
}

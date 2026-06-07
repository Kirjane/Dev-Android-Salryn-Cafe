using Microsoft.Data.Sqlite;
using Salryn.Core.ValueObjects;

namespace Salryn.Infrastructure.Data.Repositories;

internal static class SqliteDataReaderExtensions
{
    public static Guid GetGuidValue(this SqliteDataReader reader, string columnName)
    {
        return Guid.Parse(reader.GetString(reader.GetOrdinal(columnName)));
    }

    public static string GetStringValue(this SqliteDataReader reader, string columnName)
    {
        return reader.GetString(reader.GetOrdinal(columnName));
    }

    public static string? GetNullableString(this SqliteDataReader reader, string columnName)
    {
        int ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    public static int GetInt32Value(this SqliteDataReader reader, string columnName)
    {
        return Convert.ToInt32(reader.GetValue(reader.GetOrdinal(columnName)));
    }

    public static long GetInt64Value(this SqliteDataReader reader, string columnName)
    {
        return Convert.ToInt64(reader.GetValue(reader.GetOrdinal(columnName)));
    }

    public static bool GetBoolValue(this SqliteDataReader reader, string columnName)
    {
        return GetInt32Value(reader, columnName) != 0;
    }

    public static DateTimeOffset GetDateTimeOffsetValue(this SqliteDataReader reader, string columnName)
    {
        return DateTimeOffset.Parse(GetStringValue(reader, columnName));
    }

    public static DateTimeOffset? GetNullableDateTimeOffsetValue(this SqliteDataReader reader, string columnName)
    {
        string? value = GetNullableString(reader, columnName);
        return string.IsNullOrWhiteSpace(value) ? null : DateTimeOffset.Parse(value);
    }

    public static TEnum GetEnumValue<TEnum>(this SqliteDataReader reader, string columnName)
        where TEnum : struct, Enum
    {
        int value = GetInt32Value(reader, columnName);
        return (TEnum)Enum.ToObject(typeof(TEnum), value);
    }

    public static Money GetMoneyValue(this SqliteDataReader reader, string columnName)
    {
        return Money.FromCents(GetInt64Value(reader, columnName));
    }

    public static Money? GetNullableMoneyValue(this SqliteDataReader reader, string columnName)
    {
        int ordinal = reader.GetOrdinal(columnName);

        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return Money.FromCents(Convert.ToInt64(reader.GetValue(ordinal)));
    }

    public static Quantity GetQuantityValue(this SqliteDataReader reader, string columnName)
    {
        var result = Quantity.FromX1000(GetInt64Value(reader, columnName));

        if (result.IsFailure)
        {
            throw new InvalidOperationException(result.Error);
        }

        return result.Value;
    }
}

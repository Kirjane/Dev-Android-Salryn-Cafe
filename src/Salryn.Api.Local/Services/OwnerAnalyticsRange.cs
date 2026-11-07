using Salryn.Core.Common;

namespace Salryn.Api.Local.Services;

public sealed record OwnerAnalyticsRange(
    DateTimeOffset AsOfUtc,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    DateTimeOffset PriorStartUtc,
    DateTimeOffset PriorEndUtc,
    DateOnly StartStoreDate,
    DateOnly EndStoreDateExclusive)
{
    public const int MaximumDays = 366;
    public const string TimeZoneId = "Asia/Manila";
    public static readonly TimeZoneInfo StoreTimeZone = TimeZoneInfo.CreateCustomTimeZone(
        TimeZoneId, TimeSpan.FromHours(8), "Philippine Standard Time", "Philippine Standard Time");

    public static Result<OwnerAnalyticsRange> Create(
        DateOnly? from,
        DateOnly? toInclusive,
        DateTimeOffset? asOfUtc = null)
    {
        DateTimeOffset asOf = (asOfUtc ?? TimeProvider.System.GetUtcNow()).ToUniversalTime();
        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(asOf, StoreTimeZone).DateTime);
        DateOnly start = from ?? today.AddDays(-29);
        DateOnly endInclusive = toInclusive ?? today;
        if (endInclusive < start)
            return Result<OwnerAnalyticsRange>.Fail(Error.Validation("OwnerAnalytics.InvalidRange", "The end date must not be before the start date."));
        int days = endInclusive.DayNumber - start.DayNumber + 1;
        if (days > MaximumDays)
            return Result<OwnerAnalyticsRange>.Fail(Error.Validation("OwnerAnalytics.RangeTooLarge", $"Date range cannot exceed {MaximumDays} store days."));

        DateOnly endExclusive = endInclusive.AddDays(1);
        DateTimeOffset startLocal = new(start.Year, start.Month, start.Day, 0, 0, 0, StoreTimeZone.BaseUtcOffset);
        DateTimeOffset endLocal = new(endExclusive.Year, endExclusive.Month, endExclusive.Day, 0, 0, 0, StoreTimeZone.BaseUtcOffset);
        TimeSpan duration = endLocal - startLocal;
        return Result<OwnerAnalyticsRange>.Success(new(
            asOf,
            startLocal.ToUniversalTime(),
            endLocal.ToUniversalTime(),
            startLocal.Subtract(duration).ToUniversalTime(),
            startLocal.ToUniversalTime(),
            start,
            endExclusive));
    }
}

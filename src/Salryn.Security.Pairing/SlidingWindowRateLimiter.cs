using System.Collections.Concurrent;

namespace Salryn.Security.Pairing;

/// <summary>
/// Sliding-window brute-force rate limiter with progressive lockout for join-code,
/// activation, challenge, and login endpoints.
/// </summary>
public sealed class SlidingWindowRateLimiter
{
    private readonly int _maxFailures;
    private readonly TimeSpan _window;
    private readonly TimeSpan _lockoutDuration;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.OrdinalIgnoreCase);

    public SlidingWindowRateLimiter(
        int maxFailures = 5,
        TimeSpan? window = null,
        TimeSpan? lockoutDuration = null,
        TimeProvider? timeProvider = null)
    {
        if (maxFailures <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFailures));
        }

        _maxFailures = maxFailures;
        _window = window ?? TimeSpan.FromMinutes(5);
        _lockoutDuration = lockoutDuration ?? TimeSpan.FromMinutes(15);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public PairingResult CheckAllowed(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return PairingResult.Success();
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (!_buckets.TryGetValue(key, out Bucket? bucket))
        {
            return PairingResult.Success();
        }

        lock (bucket)
        {
            Prune(bucket, now);
            if (bucket.LockedUntilUtc.HasValue && bucket.LockedUntilUtc.Value > now)
            {
                return PairingResult.Fail(
                    PairingErrorCodes.RateLimitExceeded,
                    $"Too many failed attempts. Retry after {bucket.LockedUntilUtc.Value:O}.",
                    httpStatusCode: 429);
            }

            if (bucket.LockedUntilUtc.HasValue && bucket.LockedUntilUtc.Value <= now)
            {
                bucket.LockedUntilUtc = null;
            }
        }

        return PairingResult.Success();
    }

    public void RecordFailure(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        Bucket bucket = _buckets.GetOrAdd(key, _ => new Bucket());

        lock (bucket)
        {
            Prune(bucket, now);
            bucket.Failures.Enqueue(now);

            if (bucket.Failures.Count >= _maxFailures)
            {
                bucket.LockoutCount++;
                long multiplier = Math.Min(4, bucket.LockoutCount);
                bucket.LockedUntilUtc = now.AddTicks(_lockoutDuration.Ticks * multiplier);
                bucket.Failures.Clear();
            }
        }
    }

    public void RecordSuccess(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        _buckets.TryRemove(key, out _);
    }

    private void Prune(Bucket bucket, DateTimeOffset now)
    {
        DateTimeOffset cutoff = now - _window;
        while (bucket.Failures.Count > 0 && bucket.Failures.Peek() < cutoff)
        {
            bucket.Failures.Dequeue();
        }
    }

    private sealed class Bucket
    {
        public Queue<DateTimeOffset> Failures { get; } = new();
        public DateTimeOffset? LockedUntilUtc { get; set; }
        public int LockoutCount { get; set; }
    }
}

using System.Threading;

namespace Salryn.Api.Local.Services;

/// <summary>
/// Host-local receipt numbering for standard host console checkout. This is not the BIR official
/// document sequence path and must not be presented as legal/RMO certification output.
/// </summary>
public sealed class HostOperatorReceiptNumberAllocator
{
    private readonly string _prefix;
    private readonly long _epoch;
    private readonly string _instanceToken;
    private long _sequence;

    public HostOperatorReceiptNumberAllocator(string prefix = "POS")
    {
        _prefix = string.IsNullOrWhiteSpace(prefix) ? "POS" : prefix.Trim();
        _epoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _instanceToken = Guid.NewGuid().ToString("N")[..6];
        _sequence = 0;
    }

    public string AllocateNext(Guid? cashierSessionId = null)
    {
        long next = Interlocked.Increment(ref _sequence);
        string sessionPart = cashierSessionId is null || cashierSessionId == Guid.Empty
            ? "NOSHIFT"
            : cashierSessionId.Value.ToString("N")[..8].ToUpperInvariant();

        return $"{_prefix}-{_epoch}-{sessionPart}-{next:D6}";
    }
}
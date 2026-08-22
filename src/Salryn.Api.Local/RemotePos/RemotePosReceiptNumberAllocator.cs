using System.Threading;

namespace Salryn.Api.Local.RemotePos;

/// <summary>
/// LAN-M16 host-owned document numbering for remote POS sales. The remote client NEVER supplies
/// the receipt/document number — the host allocates it. Numbers are monotonic within the host
/// process and are ultimately protected by the <c>sales.receipt_no UNIQUE</c> constraint, which
/// fails a duplicate write closed so a collision can never double-post a sale.
///
/// Scope note (documented limitation): this is the host-owned numbering seam for the standard
/// host-side sale completion path. The strict BIR official document sequence + electronic journal
/// remain the host-local official completion path (<c>official.checkout.complete</c>), which the
/// remote runtime does not invoke and cannot use to forge an official number.
/// </summary>
public sealed class RemotePosReceiptNumberAllocator
{
    private readonly string _prefix;
    private readonly long _epoch;
    private readonly string _instanceToken;
    private long _sequence;

    public RemotePosReceiptNumberAllocator(string prefix = "RPOS")
    {
        _prefix = string.IsNullOrWhiteSpace(prefix) ? "RPOS" : prefix.Trim();

        // Kirch: why - seed from UTC seconds so numbers stay monotonic and do not collide with a
        // prior host session, plus a per-instance random token so a host restart (or a second
        // allocator instance) within the same second can never mint a colliding number before the
        // sales.receipt_no UNIQUE constraint is even consulted.
        _epoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _instanceToken = Guid.NewGuid().ToString("N")[..6];
        _sequence = 0;
    }

    public string AllocateNext()
    {
        long next = Interlocked.Increment(ref _sequence);
        return $"{_prefix}-{_epoch}-{_instanceToken}-{next:D6}";
    }
}

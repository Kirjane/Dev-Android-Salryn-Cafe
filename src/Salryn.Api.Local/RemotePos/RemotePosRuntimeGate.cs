namespace Salryn.Api.Local.RemotePos;

/// <summary>
/// LAN-M16: host-local, process-scoped enablement flag for the remote POS command runtime.
/// The remote POS write surface is disabled by default and must be explicitly enabled by the
/// local POS-host operator (loopback + operator session, via /api/lan-control). This flag is
/// deliberately separate from the read-only <c>RemoteTransportEnabled</c> config: enabling the
/// read-only bridge never enables remote POS writes. The flag resets to disabled on host
/// restart, which is fail-safe (remote write commands are denied until re-enabled).
/// </summary>
public sealed class RemotePosRuntimeGate
{
    private readonly object _sync = new();
    private bool _enabled;
    private DateTimeOffset? _enabledAtUtc;
    private Guid? _enabledByUserId;

    public bool IsEnabled
    {
        get
        {
            lock (_sync)
            {
                return _enabled;
            }
        }
    }

    public DateTimeOffset? EnabledAtUtc
    {
        get
        {
            lock (_sync)
            {
                return _enabledAtUtc;
            }
        }
    }

    public void Enable(Guid operatorUserId)
    {
        lock (_sync)
        {
            _enabled = true;
            _enabledAtUtc = DateTimeOffset.UtcNow;
            _enabledByUserId = operatorUserId;
        }
    }

    public void Disable()
    {
        lock (_sync)
        {
            _enabled = false;
            _enabledAtUtc = null;
            _enabledByUserId = null;
        }
    }
}

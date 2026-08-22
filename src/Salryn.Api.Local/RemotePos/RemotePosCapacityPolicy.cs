namespace Salryn.Api.Local.RemotePos;

public sealed class RemotePosCapacityPolicy
{
    public const int DefaultMaxPairedTerminals = 5;
    public const int DefaultMaxActiveSessions = 3;
    public const int HardCap = 10;

    public RemotePosCapacityPolicy(
        int maxPairedTerminals = DefaultMaxPairedTerminals,
        int maxActiveSessions = DefaultMaxActiveSessions)
    {
        MaxPairedTerminals = ClampToHardCap(maxPairedTerminals);
        MaxActiveSessions = ClampToHardCap(maxActiveSessions);
    }

    public int MaxPairedTerminals { get; }

    public int MaxActiveSessions { get; }

    private static int ClampToHardCap(int value)
    {
        if (value <= 0)
        {
            return 1;
        }

        return Math.Min(value, HardCap);
    }
}

public sealed record RemotePosCapacityStatusResponse(
    int MaxPairedTerminals,
    int MaxActiveSessions,
    int HardCap,
    int CurrentPairedCount,
    int CurrentActiveSessions,
    int RemainingPairingCapacity,
    int RemainingActiveSessionCapacity,
    bool HostLanEntitled = false,
    string? HostLicenseId = null,
    string? StoreId = null,
    string? LicenseMessage = null,
    string? LicenseContactEmail = null);

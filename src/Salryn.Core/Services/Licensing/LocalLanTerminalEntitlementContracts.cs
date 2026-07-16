namespace Salryn.Core.Services.Licensing;

/// <summary>
/// Host-side LAN availability derived from the local Host license.
/// Commercial terminal entitlement comes from one signed Terminal license per device;
/// the numeric limits here are abuse/safety caps only.
/// </summary>
public sealed record LocalLanTerminalEntitlementStatus(
    bool HostOperationalLanAllowed,
    int AllowedLanTerminals,
    int EffectiveMaxPairedTerminals,
    int EffectiveMaxActiveSessions,
    LicenseStatus LicenseStatus,
    LicenseType? LicenseType,
    string? HostLicenseId,
    string? StoreId,
    string Message,
    string ContactEmail);

public sealed record ValidatedTerminalLicenseProof(
    string LicenseId,
    string CustomerName,
    string StoreId,
    string HostLicenseId,
    string TerminalId,
    string MachineCode,
    DateTimeOffset? ExpiresAt);

public static class LocalLanTerminalEntitlementCodes
{
    public const string HostLicenseRequired = "LanEntitlement.HostLicenseRequired";
    public const string WrongLicenseType = "LanEntitlement.WrongLicenseType";
    public const string NoTerminalsPurchased = "LanEntitlement.NoTerminalsPurchased";
    public const string TerminalLimitReached = "LanEntitlement.TerminalLimitReached";
    public const string TerminalProofInvalid = "LanEntitlement.TerminalProofInvalid";
    public const string TerminalStoreMismatch = "LanEntitlement.TerminalStoreMismatch";
    public const string TerminalHostMismatch = "LanEntitlement.TerminalHostMismatch";
    public const string TerminalMachineCodeRequired = "LanEntitlement.TerminalMachineCodeRequired";
    public const string TerminalMachineMismatch = "LanEntitlement.TerminalMachineMismatch";
    public const string TerminalLicenseAlreadyPaired = "LanEntitlement.TerminalLicenseAlreadyPaired";
    public const string HostLicenseAsTerminal = "LanEntitlement.HostLicenseAsTerminal";
}

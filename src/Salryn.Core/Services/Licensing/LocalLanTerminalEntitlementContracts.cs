namespace Salryn.Core.Services.Licensing;

/// <summary>
/// Host-side commercial LAN terminal entitlement derived from the local Host license.
/// Abuse/capacity hard caps remain separate; this is the paid terminal add-on limit.
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

public static class LocalLanTerminalEntitlementCodes
{
    public const string HostLicenseRequired = "LanEntitlement.HostLicenseRequired";
    public const string WrongLicenseType = "LanEntitlement.WrongLicenseType";
    public const string NoTerminalsPurchased = "LanEntitlement.NoTerminalsPurchased";
    public const string TerminalLimitReached = "LanEntitlement.TerminalLimitReached";
    public const string TerminalProofInvalid = "LanEntitlement.TerminalProofInvalid";
    public const string TerminalStoreMismatch = "LanEntitlement.TerminalStoreMismatch";
    public const string TerminalHostMismatch = "LanEntitlement.TerminalHostMismatch";
    public const string HostLicenseAsTerminal = "LanEntitlement.HostLicenseAsTerminal";
}

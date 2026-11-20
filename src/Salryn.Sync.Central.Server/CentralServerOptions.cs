namespace Salryn.Sync.Central.Server;

public sealed class CentralServerOptions
{
    public const string CanonicalProductId = "Salryn.Cafe";
    public const int CentralSchemaVersion = 3;

    public string DatabasePath { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "salryn-cafe-central.db");
    public bool RequireHttps { get; set; } = true;
    public bool TrustForwardedProto { get; set; } = true;
    public HashSet<string> TrustedProxyAddresses { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "127.0.0.1",
        "::1"
    };
    public int JoinCodeTtlMinutes { get; set; } = 10;
    public int ChallengeTtlSeconds { get; set; } = 120;
    public int DeviceTokenTtlMinutes { get; set; } = 30;
    public int OfflineLeaseHours { get; set; } = 24;
    public int AdminSessionTtlMinutes { get; set; } = 480;
    public int MaxAdminFailedAttempts { get; set; } = 5;
    public int AdminLockoutMinutes { get; set; } = 5;
    public int MaxPushBatchSize { get; set; } = 200;
    public int MaxPullPageSize { get; set; } = 200;
    public int MaxQuarantinePerDevice { get; set; } = 500;
    public int DeviceEndpointRateLimitPerMinute { get; set; } = 60;

    /// <summary>
    /// Optional bootstrap admin credentials for initial setup or automated tests.
    /// Never ship live credentials in repository configs.
    /// </summary>
    public BootstrapAdminOptions? BootstrapAdmin { get; set; }
}

public sealed class BootstrapAdminOptions
{
    public Guid TenantId { get; set; }
    public Guid StoreId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = "Central Owner";
    public string Role { get; set; } = "Owner";
    public string Password { get; set; } = string.Empty;
}

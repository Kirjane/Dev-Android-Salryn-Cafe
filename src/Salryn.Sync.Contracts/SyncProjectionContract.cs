namespace Salryn.Sync.Contracts;

public static class SyncProjectionContract
{
    public const string Applied = "Applied";
    public const string ReconciliationRequired = "ReconciliationRequired";

    public const string BootstrapAuditAction = "Sync.Bootstrap.Applied";
    public const string BootstrapAuditEntityType = "SyncRemoteState";

    public const string ProjectionAppliedCode = "Sync.ProjectionApplied";
    public const string OriginEchoResolvedCode = "Sync.OriginEchoResolved";
}

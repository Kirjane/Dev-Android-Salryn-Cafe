using Salryn.Core.Common;

namespace Salryn.Core.Deployment;

public sealed class SalrynStoreHostAuthorityGuard
{
    private readonly Func<DeploymentIdentitySnapshot?> _snapshotProvider;

    public SalrynStoreHostAuthorityGuard(Func<DeploymentIdentitySnapshot?> snapshotProvider)
    {
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
    }

    public Result RequireDatabaseAuthority()
    {
        return RequireAuthority(
            snapshot => snapshot.Authority.IsDatabaseAuthority,
            "StoreHostAuthority.DatabaseRequired",
            "This machine is not the Store Host database authority.");
    }

    public Result RequireOfficialSequenceAuthority()
    {
        return RequireAuthority(
            snapshot => snapshot.Authority.IsOfficialSequenceAuthority,
            "StoreHostAuthority.OfficialSequenceRequired",
            "This machine is not the Store Host official sequence authority.");
    }

    public Result RequireJournalAuthority()
    {
        return RequireAuthority(
            snapshot => snapshot.Authority.IsJournalAuthority,
            "StoreHostAuthority.JournalRequired",
            "This machine is not the Store Host journal authority.");
    }

    public Result RequireBackupAuthority()
    {
        return RequireAuthority(
            snapshot => snapshot.Authority.IsBackupAuthority,
            "StoreHostAuthority.BackupRequired",
            "This machine is not the Store Host backup authority.");
    }

    private Result RequireAuthority(
        Func<DeploymentIdentitySnapshot, bool> predicate,
        string errorCode,
        string errorMessage)
    {
        DeploymentIdentitySnapshot? snapshot = _snapshotProvider();

        if (snapshot is null)
        {
            return Result.Success();
        }

        if (!predicate(snapshot))
        {
            return Result.Fail(Error.Forbidden(errorCode, errorMessage));
        }

        return Result.Success();
    }
}

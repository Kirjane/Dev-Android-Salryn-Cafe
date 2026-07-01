using Salryn.Core.Common;
using Salryn.Core.Deployment;

namespace Salryn.Core.Host;

public static class SalrynHostCommandLoopbackRules
{
    public const string LoopbackHost = "127.0.0.1";
    public const string LoopbackHostName = "localhost";

    public static bool IsLoopbackOrigin(string? originHost)
    {
        if (string.IsNullOrWhiteSpace(originHost))
        {
            return false;
        }

        string normalized = originHost.Trim();

        if (normalized.Equals(LoopbackHost, StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(LoopbackHostName, StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("::1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalized.StartsWith("127.", StringComparison.Ordinal);
    }

    public static bool IsForbiddenBindAddress(string? bindAddress)
    {
        if (string.IsNullOrWhiteSpace(bindAddress))
        {
            return false;
        }

        string normalized = bindAddress.Trim();

        return normalized.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("*", StringComparison.Ordinal)
            || normalized.Equals("::", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("[::]", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class SalrynHostCommandAuthorityGuard
{
    private readonly SalrynStoreHostAuthorityGuard _storeHostAuthority;

    public SalrynHostCommandAuthorityGuard(SalrynStoreHostAuthorityGuard storeHostAuthority)
    {
        _storeHostAuthority = storeHostAuthority ?? throw new ArgumentNullException(nameof(storeHostAuthority));
    }

    public Result RequireLocalLoopbackAllowed(string? originHost, DeploymentIdentitySnapshot? identity)
    {
        if (!SalrynHostCommandLoopbackRules.IsLoopbackOrigin(originHost))
        {
            return Result.Fail(Error.Forbidden(
                "HostCommand.RemoteOriginRejected",
                "Host commands are accepted from loopback only in LAN-M3."));
        }

        if (identity?.Authority.IsClientOnly == true)
        {
            return Result.Fail(Error.Forbidden(
                "HostCommand.ClientTerminalLoopbackDisabled",
                "Client terminal loopback host commands are disabled until remote LAN transport exists."));
        }

        return Result.Success();
    }

    public Result RequireAuthorities(HostCommandAuthorityRequirements requirements)
    {
        if (requirements.HasFlag(HostCommandAuthorityRequirements.Database))
        {
            Result database = _storeHostAuthority.RequireDatabaseAuthority();

            if (database.IsFailure)
            {
                return database;
            }
        }

        if (requirements.HasFlag(HostCommandAuthorityRequirements.OfficialSequence))
        {
            Result sequence = _storeHostAuthority.RequireOfficialSequenceAuthority();

            if (sequence.IsFailure)
            {
                return sequence;
            }
        }

        if (requirements.HasFlag(HostCommandAuthorityRequirements.Journal))
        {
            Result journal = _storeHostAuthority.RequireJournalAuthority();

            if (journal.IsFailure)
            {
                return journal;
            }
        }

        if (requirements.HasFlag(HostCommandAuthorityRequirements.Backup))
        {
            Result backup = _storeHostAuthority.RequireBackupAuthority();

            if (backup.IsFailure)
            {
                return backup;
            }
        }

        return Result.Success();
    }
}

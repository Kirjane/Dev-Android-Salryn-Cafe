using Salryn.Core.Common;
using Salryn.Core.Host;

namespace Salryn.Host.Local.HostCommands.Handlers;

public sealed class BackupAuthorityHostCommandHandler : IHostCommandHandler
{
    private readonly IBackupAuthorityHostCommandExecutor _executor;

    public BackupAuthorityHostCommandHandler(IBackupAuthorityHostCommandExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public string CommandName => SalrynHostCommandNames.BackupAuthorityProbe;

    public HostCommandAuthorityRequirements AuthorityRequirements => HostCommandAuthorityRequirements.Backup;

    public bool RequiresPayload => false;

    public async Task<SalrynHostCommandResult<object?>> HandleAsync(
        SalrynHostCommandEnvelope<object?> envelope,
        CancellationToken cancellationToken = default)
    {
        Result<BackupAuthorityProbeHostCommandResponse> result = await _executor
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return SalrynHostCommandResult<object?>.Failure(envelope, result.ErrorDetail);
        }

        return SalrynHostCommandResult<object?>.Success(result.Value, envelope);
    }
}

using Salryn.App.WinForms.Composition;
using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Host;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;

internal static class HostCommandLoopbackSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== HOST COMMAND LOOPBACK SMOKE CHECKS =====");

        string tempRoot = Path.Combine(Path.GetTempPath(), "salryn-host-command-loopback-" + Guid.NewGuid().ToString("N"));

        try
        {
            SalrynAppServices services = SalrynAppServices.CreateDevelopment(tempRoot);
            Result<DatabaseInitializationSummary> initResult = services.DatabaseInitialization.InitializeDatabase();

            if (initResult.IsFailure)
            {
                throw new InvalidOperationException("Host command loopback smoke failed to initialize database.");
            }

            DeploymentIdentitySnapshot identity = services.DeploymentIdentity.GetCurrentAsync().GetAwaiter().GetResult().Value
                ?? throw new InvalidOperationException("Deployment identity is required for host command loopback smoke.");

            SalrynHostCommandEnvelope<object?> statusEnvelope = CreateEnvelope(
                identity,
                SalrynHostCommandNames.DeploymentStatus,
                payload: null);

            SalrynHostCommandResult<DeploymentStatusHostCommandResponse> statusResult = services.HostCommands.Transport
                .SendAsync<object?, DeploymentStatusHostCommandResponse>(
                    SalrynHostCommandLoopbackRules.LoopbackHost,
                    statusEnvelope)
                .GetAwaiter()
                .GetResult();

            if (!statusResult.Succeeded || statusResult.Response is null || !statusResult.Response.IsConfigured)
            {
                throw new InvalidOperationException("Deployment status host command through loopback failed.");
            }

            SalrynHostCommandEnvelope<object?> unknownEnvelope = statusEnvelope with { CommandName = "host.unknown.command" };

            SalrynHostCommandResult<DeploymentStatusHostCommandResponse> unknownResult = services.HostCommands.Transport
                .SendAsync<object?, DeploymentStatusHostCommandResponse>(
                    SalrynHostCommandLoopbackRules.LoopbackHost,
                    unknownEnvelope)
                .GetAwaiter()
                .GetResult();

            if (unknownResult.Succeeded)
            {
                throw new InvalidOperationException("Unknown host command must fail closed.");
            }

            SalrynHostCommandResult<DeploymentStatusHostCommandResponse> remoteResult = services.HostCommands.Transport
                .SendAsync<object?, DeploymentStatusHostCommandResponse>(
                    "192.168.1.50",
                    statusEnvelope)
                .GetAwaiter()
                .GetResult();

            if (remoteResult.Succeeded)
            {
                throw new InvalidOperationException("Remote origin must be rejected by loopback transport.");
            }

            DeploymentIdentityService deploymentIdentity = (DeploymentIdentityService)services.DeploymentIdentity;
            deploymentIdentity.ConfigureClientTerminalAsync(
                new ClientTerminalConfigurationRequest(
                    StoreName: identity.Store.StoreName,
                    TerminalName: "Client-Terminal-Smoke",
                    HostAddress: "127.0.0.1",
                    HostPort: 58161,
                    PairingCredentialLabel: "smoke-placeholder"))
                .GetAwaiter()
                .GetResult();

            SalrynHostCommandResult<DeploymentStatusHostCommandResponse> clientLoopbackResult = services.HostCommands.Transport
                .SendAsync<object?, DeploymentStatusHostCommandResponse>(
                    SalrynHostCommandLoopbackRules.LoopbackHost,
                    statusEnvelope)
                .GetAwaiter()
                .GetResult();

            if (clientLoopbackResult.Succeeded)
            {
                throw new InvalidOperationException("Client terminal loopback must be disabled in LAN-M3.");
            }

            deploymentIdentity.ResetToAllInOneDefaultAsync().GetAwaiter().GetResult();

            SalrynHostCommandResult<BackupAuthorityProbeHostCommandResponse> backupResult = services.HostCommands.Transport
                .SendAsync<object?, BackupAuthorityProbeHostCommandResponse>(
                    SalrynHostCommandLoopbackRules.LoopbackHost,
                    CreateEnvelope(identity, SalrynHostCommandNames.BackupAuthorityProbe, null))
                .GetAwaiter()
                .GetResult();

            if (!backupResult.Succeeded || backupResult.Response is null || !backupResult.Response.BackupAuthorityGranted)
            {
                throw new InvalidOperationException("Backup authority probe must succeed for All-in-One host.");
            }

            if (SalrynHostCommandLoopbackRules.IsForbiddenBindAddress("0.0.0.0") == false)
            {
                throw new InvalidOperationException("0.0.0.0 must be treated as forbidden bind address.");
            }

            Console.WriteLine("Host command loopback smoke checks succeeded.");
            Console.WriteLine();
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                try
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private static SalrynHostCommandEnvelope<object?> CreateEnvelope(
        DeploymentIdentitySnapshot identity,
        string commandName,
        object? payload)
    {
        return new SalrynHostCommandEnvelope<object?>(
            CommandName: commandName,
            RequestId: Guid.NewGuid(),
            StoreId: identity.Store.StoreId,
            HostId: identity.Host.HostId,
            TerminalId: identity.Terminal.TerminalId,
            UserId: Guid.Parse("00000000-0000-0000-0000-000000000001"),
            SessionId: null,
            TimestampUtc: DateTimeOffset.UtcNow,
            SourceMachineRole: identity.Host.InstallationRole,
            InstallationRole: identity.Host.InstallationRole,
            Payload: payload);
    }
}
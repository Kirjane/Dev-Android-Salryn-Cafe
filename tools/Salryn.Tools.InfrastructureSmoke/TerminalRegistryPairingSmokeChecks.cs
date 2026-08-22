using Microsoft.Data.Sqlite;
using Salryn.App.WinForms.Composition;
using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Host;
using Salryn.Core.Security;
using Salryn.Core.TerminalRegistry;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Seeding;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;

internal static class TerminalRegistryPairingSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== TERMINAL REGISTRY PAIRING SMOKE CHECKS =====");

        string tempRoot = Path.Combine(Path.GetTempPath(), "salryn-terminal-registry-pairing-" + Guid.NewGuid().ToString("N"));

        try
        {
            SalrynAppServices services = SalrynAppServices.CreateDevelopment(tempRoot);
            Result<DatabaseInitializationSummary> initResult = services.DatabaseInitialization.InitializeDatabase();

            if (initResult.IsFailure)
            {
                throw new InvalidOperationException("Terminal registry pairing smoke failed to initialize database.");
            }

            DeploymentIdentitySnapshot identity = services.DeploymentIdentity.GetCurrentAsync().GetAwaiter().GetResult().Value
                ?? throw new InvalidOperationException("Deployment identity is required for terminal registry pairing smoke.");

            TerminalRegistryActorContext adminActor = new(
                Guid.Parse("00000000-0000-0000-0000-000000000001"),
                DefaultSeedData.AdminPermissions.ToArray());

            SalrynHostCommandEnvelope<TerminalRegistryActorContext> listEnvelope = CreateEnvelope(
                identity,
                SalrynHostCommandNames.TerminalRegistryList,
                adminActor);

            SalrynHostCommandResult<TerminalRegistryListHostCommandResponse> listResult = services.HostCommands.Transport
                .SendAsync<TerminalRegistryActorContext, TerminalRegistryListHostCommandResponse>(
                    SalrynHostCommandLoopbackRules.LoopbackHost,
                    listEnvelope)
                .GetAwaiter()
                .GetResult();

            if (!listResult.Succeeded || listResult.Response is null || listResult.Response.Terminals.Count == 0)
            {
                throw new InvalidOperationException("Terminal registry list through loopback failed.");
            }

            SalrynHostCommandEnvelope<TerminalPairingIssueHostCommandPayload> issueEnvelope = CreateEnvelope(
                identity,
                SalrynHostCommandNames.TerminalPairingIssue,
                new TerminalPairingIssueHostCommandPayload(
                    adminActor,
                    "Smoke Cashier Terminal",
                    TerminalRegistrationRole.CashierClient));

            SalrynHostCommandResult<TerminalPairingIssueHostCommandResponse> issueResult = services.HostCommands.Transport
                .SendAsync<TerminalPairingIssueHostCommandPayload, TerminalPairingIssueHostCommandResponse>(
                    SalrynHostCommandLoopbackRules.LoopbackHost,
                    issueEnvelope)
                .GetAwaiter()
                .GetResult();

            if (!issueResult.Succeeded || issueResult.Response is null || string.IsNullOrWhiteSpace(issueResult.Response.PairingCode))
            {
                throw new InvalidOperationException("Pairing invite issue through loopback failed.");
            }

            using (SqliteConnection connection = new($"Data Source={services.DbPaths.DatabasePath}"))
            {
                connection.Open();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    SELECT pairing_code_hash
                    FROM salryn_terminal_pairing_invites
                    WHERE pairing_id = $pairingId
                    LIMIT 1;
                    """;
                command.Parameters.AddWithValue("$pairingId", issueResult.Response.Invite.PairingId.ToString("D"));
                object? storedHash = command.ExecuteScalar();

                if (storedHash is not string hash || hash == issueResult.Response.PairingCode)
                {
                    throw new InvalidOperationException("Raw pairing code must not be persisted.");
                }
            }

            TerminalRegistryActorContext cashierActor = new(
                Guid.Parse("00000000-0000-0000-0000-000000000003"),
                DefaultSeedData.CashierPermissions.ToArray());

            SalrynHostCommandResult<TerminalPairingIssueHostCommandResponse> cashierIssueResult = services.HostCommands.Transport
                .SendAsync<TerminalPairingIssueHostCommandPayload, TerminalPairingIssueHostCommandResponse>(
                    SalrynHostCommandLoopbackRules.LoopbackHost,
                    CreateEnvelope(
                        identity,
                        SalrynHostCommandNames.TerminalPairingIssue,
                        new TerminalPairingIssueHostCommandPayload(
                            cashierActor,
                            "Forbidden Cashier Terminal",
                            TerminalRegistrationRole.CashierClient)))
                .GetAwaiter()
                .GetResult();

            if (cashierIssueResult.Succeeded)
            {
                throw new InvalidOperationException("Cashier must not issue pairing invites through loopback.");
            }

            SalrynHostCommandResult<SalrynTerminalPairingInvite> revokeResult = services.HostCommands.Transport
                .SendAsync<TerminalPairingRevokeHostCommandPayload, SalrynTerminalPairingInvite>(
                    SalrynHostCommandLoopbackRules.LoopbackHost,
                    CreateEnvelope(
                        identity,
                        SalrynHostCommandNames.TerminalPairingRevoke,
                        new TerminalPairingRevokeHostCommandPayload(
                            adminActor,
                            issueResult.Response.Invite.PairingId)))
                .GetAwaiter()
                .GetResult();

            if (!revokeResult.Succeeded || revokeResult.Response is null)
            {
                throw new InvalidOperationException("Pairing invite revoke through loopback failed.");
            }

            Result<SalrynTerminalPairingInvite> validateRevoked = services.TerminalPairingPreparation
                .ValidatePairingCodeAsync(issueResult.Response.PairingCode)
                .GetAwaiter()
                .GetResult();

            if (validateRevoked.IsSuccess)
            {
                throw new InvalidOperationException("Revoked pairing invite must fail validation.");
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

            SalrynHostCommandResult<TerminalRegistryListHostCommandResponse> clientListResult = services.HostCommands.Transport
                .SendAsync<TerminalRegistryActorContext, TerminalRegistryListHostCommandResponse>(
                    SalrynHostCommandLoopbackRules.LoopbackHost,
                    listEnvelope)
                .GetAwaiter()
                .GetResult();

            if (clientListResult.Succeeded)
            {
                throw new InvalidOperationException("Client terminal authority must fail terminal registry loopback commands.");
            }

            Console.WriteLine("Terminal registry pairing smoke checks succeeded.");
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

    private static SalrynHostCommandEnvelope<TPayload> CreateEnvelope<TPayload>(
        DeploymentIdentitySnapshot identity,
        string commandName,
        TPayload payload)
    {
        return new SalrynHostCommandEnvelope<TPayload>(
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

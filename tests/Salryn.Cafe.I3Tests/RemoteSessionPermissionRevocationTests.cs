using Salryn.Api.Local.Composition;
using Salryn.Api.Local.RemotePos;
using Xunit;

namespace Salryn.Cafe.I3Tests;

public sealed class RemoteSessionPermissionRevocationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActiveOperatorPermissionOrRoleChangeRevokesExistingAuthority(bool changeRole)
    {
        using var fx = new I3TestDatabaseFixture();
        using var connection = fx.ConnectionFactory.CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE users SET role_id='00000000-0000-0000-0000-000000000003' WHERE id=$id;";
        command.Parameters.AddWithValue("$id", fx.AdminUserId.ToString("D"));
        command.ExecuteNonQuery();
        var current = (await fx.Composition.UserPermissionReadRepository.FindByUserIdAsync(fx.AdminUserId)).Value!;
        var session = new RemotePosOperatorSession
        {
            SessionId=Guid.NewGuid(), TokenHash=[], TerminalId=Guid.NewGuid(), HandshakeSessionId=Guid.NewGuid(),
            ObservedOrigin="synthetic", StoreId=fx.StoreId, HostId=Guid.NewGuid(), UserId=current.UserId,
            Username=current.Username, DisplayName=current.DisplayName, RoleId=current.RoleId, RoleName=current.RoleName,
            Permissions=current.Permissions, IssuedAtUtc=DateTimeOffset.UtcNow, ExpiresAtUtc=DateTimeOffset.UtcNow.AddMinutes(30)
        };
        var lan = LanRemoteTransportApiComposition.Create(fx.Composition);
        var services = RemotePosApiServices.Create(fx.Composition, lan, new RemotePosRuntimeGate());
        Assert.True((await services.SessionService.ValidateCurrentOperatorAsync(session)).Ok);
        command.CommandText = changeRole
            ? "UPDATE users SET role_id='00000000-0000-0000-0000-000000000002' WHERE id=$id;"
            : "DELETE FROM role_permissions WHERE role_id='00000000-0000-0000-0000-000000000003';";
        command.ExecuteNonQuery();
        var denied = await services.SessionService.ValidateCurrentOperatorAsync(session);
        Assert.False(denied.Ok);
        Assert.Null(denied.Session);
        Assert.NotNull(session.RevokedAtUtc);
    }
}

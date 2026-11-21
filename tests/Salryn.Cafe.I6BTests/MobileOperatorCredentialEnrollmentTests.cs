using System.Text.Json;
using Microsoft.Data.Sqlite;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Mobile.Foundation.Cashier;
using Salryn.Mobile.Runtime;
using Xunit;

namespace Salryn.Cafe.I6BTests;

public sealed class MobileOperatorCredentialEnrollmentTests
{
    private const string Placeholder = "!SYNC-BOOTSTRAP-NONLOGIN!";
    private const string Password = "SyntheticCentralCredential!2026";

    [Fact]
    public async Task SuccessfulVerifiedEnrollment_HashesPasswordAndSupportsOfflineLoginAfterRestart()
    {
        using var fixture = new CanonicalCashierFixture();
        var (scope, login) = await Authority(fixture);
        SetPlaceholder(fixture);
        Assert.True((await fixture.Runtime.Authentication.LoginAsync(new LoginRequest("synthetic", Password))).IsFailure);
        Assert.True(fixture.Runtime.OperatorCredentials.EnrollAfterVerifiedLogin(scope, login, "synthetic", Password, fixture.Now).IsSuccess);
        string stored = fixture.Scalar("SELECT password_hash FROM users WHERE username='synthetic';").ToString()!;
        Assert.NotEqual(Password, stored);
        Assert.True(SalrynPasswordHasher.VerifyPassword(Password, stored));
        var restarted = MobileProductionComposition.Create(fixture.Root);
        Assert.Equal(fixture.ActorId, (await restarted.Authentication.LoginAsync(new LoginRequest("synthetic", Password))).Value.UserId);
        string metadata = fixture.Scalar("SELECT value FROM app_metadata WHERE key LIKE 'salryn.cafe.operator.credential.scope.%';").ToString()!;
        Assert.DoesNotContain(Password, metadata);
        Assert.DoesNotContain(stored, metadata);
        Assert.Equal(scope, JsonSerializer.Deserialize<MobileApprovedCredentialScope>(metadata));
    }

    [Theory]
    [InlineData("failed-login")]
    [InlineData("revoked")]
    [InlineData("expired")]
    [InlineData("wrong-user")]
    [InlineData("wrong-device")]
    [InlineData("wrong-tenant")]
    [InlineData("wrong-store")]
    [InlineData("wrong-epoch")]
    [InlineData("inactive")]
    [InlineData("no-permissions")]
    [InlineData("wrong-username")]
    public async Task DeniedOrMismatchedAuthority_NeverChangesCredentialOrAddsScope(string scenario)
    {
        using var fixture = new CanonicalCashierFixture();
        var (scope, login) = await Authority(fixture);
        SetPlaceholder(fixture);
        string username = "synthetic";
        switch (scenario)
        {
            case "failed-login": login = login with { IsAuthenticated = false }; break;
            case "revoked": scope = scope with { IsAuthorized = false }; break;
            case "expired": login = login with { ExpiresAtUtc = fixture.Now }; break;
            case "wrong-user": login = login with { UserId = Guid.NewGuid() }; break;
            case "wrong-device": login = login with { DeviceId = Guid.NewGuid() }; break;
            case "wrong-tenant": login = login with { TenantId = Guid.NewGuid() }; break;
            case "wrong-store": login = login with { StoreId = Guid.NewGuid() }; break;
            case "wrong-epoch": login = login with { ServerEpoch = scope.ServerEpoch + 1 }; break;
            case "inactive": fixture.Execute("UPDATE users SET status=2 WHERE username='synthetic';"); break;
            case "no-permissions": login = login with { Permissions = null }; break;
            case "wrong-username": username = "another-operator"; break;
        }
        Assert.True(fixture.Runtime.OperatorCredentials.EnrollAfterVerifiedLogin(scope, login, username, Password, fixture.Now).IsFailure);
        Assert.Equal(Placeholder, fixture.Scalar("SELECT password_hash FROM users WHERE username='synthetic';"));
        Assert.Equal(0L, fixture.Scalar("SELECT COUNT(*) FROM app_metadata WHERE key LIKE 'salryn.cafe.operator.credential.scope.%';"));
    }

    [Fact]
    public async Task ExistingLocalCredential_IsPreservedRatherThanResetByEnrollment()
    {
        using var fixture = new CanonicalCashierFixture();
        var (scope, login) = await Authority(fixture);
        string before = fixture.Scalar("SELECT password_hash FROM users WHERE username='synthetic';").ToString()!;
        var result = fixture.Runtime.OperatorCredentials.EnrollAfterVerifiedLogin(scope, login, "synthetic", Password, fixture.Now);
        Assert.True(result.IsFailure);
        Assert.Equal("Mobile.CredentialAlreadyLocal", result.ErrorDetail.Code);
        Assert.Equal(before, fixture.Scalar("SELECT password_hash FROM users WHERE username='synthetic';"));
    }

    [Fact]
    public async Task RebootstrapAndPasswordRefresh_RequireSameScopeAndCannotRollBackEpoch()
    {
        using var fixture = new CanonicalCashierFixture();
        var (scope, login) = await Authority(fixture);
        SetPlaceholder(fixture);
        Assert.True(fixture.Runtime.OperatorCredentials.EnrollAfterVerifiedLogin(scope, login, "synthetic", Password, fixture.Now).IsSuccess);
        SetPlaceholder(fixture); // Rebootstrap reinstalls a non-login user, not a reusable credential.
        var nextScope = scope with { ServerEpoch = scope.ServerEpoch + 1 };
        var nextLogin = login with { ServerEpoch = nextScope.ServerEpoch };
        Assert.True(fixture.Runtime.OperatorCredentials.EnrollAfterVerifiedLogin(nextScope, nextLogin, "synthetic", Password + "New", fixture.Now).IsSuccess);
        Assert.True((await fixture.Runtime.Authentication.LoginAsync(new LoginRequest("synthetic", Password))).IsFailure);
        Assert.True((await fixture.Runtime.Authentication.LoginAsync(new LoginRequest("synthetic", Password + "New"))).IsSuccess);
        string before = fixture.Scalar("SELECT password_hash FROM users WHERE username='synthetic';").ToString()!;
        Assert.True(fixture.Runtime.OperatorCredentials.EnrollAfterVerifiedLogin(scope, login, "synthetic", Password, fixture.Now).IsFailure);
        var otherScope = nextScope with { TenantId = Guid.NewGuid() };
        Assert.True(fixture.Runtime.OperatorCredentials.EnrollAfterVerifiedLogin(otherScope, nextLogin with { TenantId = otherScope.TenantId }, "synthetic", Password, fixture.Now).IsFailure);
        Assert.Equal(before, fixture.Scalar("SELECT password_hash FROM users WHERE username='synthetic';"));
    }

    [Fact]
    public async Task ScopePersistenceFailure_RollsBackCredentialHash()
    {
        using var fixture = new CanonicalCashierFixture();
        var (scope, login) = await Authority(fixture);
        SetPlaceholder(fixture);
        fixture.Execute("CREATE TRIGGER synthetic_enrollment_fault BEFORE INSERT ON app_metadata WHEN NEW.key LIKE 'salryn.cafe.operator.credential.scope.%' BEGIN SELECT RAISE(ABORT,'synthetic enrollment fault'); END;");
        Assert.Throws<SqliteException>(() => fixture.Runtime.OperatorCredentials.EnrollAfterVerifiedLogin(scope, login, "synthetic", Password, fixture.Now));
        Assert.Equal(Placeholder, fixture.Scalar("SELECT password_hash FROM users WHERE username='synthetic';"));
    }

    [Fact]
    public async Task RemoteCurrentPermissions_ReplaceSeededDefaultsWithoutGrantingRevokedCredit()
    {
        using var fixture=new CanonicalCashierFixture();
        var(scope,login)=await Authority(fixture); SetPlaceholder(fixture);
        login=login with {Permissions=login.Permissions!.Where(permission=>permission!="Receivables.CreateCreditSale").ToArray()};
        Assert.True(fixture.Runtime.OperatorCredentials.EnrollAfterVerifiedLogin(scope,login,"synthetic",Password,fixture.Now).IsSuccess);
        var user=(await fixture.Runtime.Authentication.LoginAsync(new LoginRequest("synthetic",Password))).Value;
        Assert.DoesNotContain("Receivables.CreateCreditSale",user.Permissions);
        Assert.Equal(login.Permissions!.Order(),user.Permissions.Order());
        var restarted=MobileProductionComposition.Create(fixture.Root);
        await restarted.InitializeDatabaseAsync();
        var after=(await restarted.Authentication.LoginAsync(new LoginRequest("synthetic",Password))).Value;
        Assert.DoesNotContain("Receivables.CreateCreditSale",after.Permissions);
        Assert.Equal(login.Permissions.Order(),after.Permissions.Order());
    }

    [Fact]
    public async Task SuccessfulZeroPermissionRefresh_RemovesOldGrants_AndDoesNotExtendOnFailure()
    {
        using var f=new CanonicalCashierFixture();var(scope,login)=await Authority(f);SetPlaceholder(f);
        Assert.True(f.Runtime.OperatorCredentials.EnrollAfterVerifiedLogin(scope,login,"synthetic",Password,f.Now).IsSuccess);
        Assert.True(f.Runtime.OperatorCredentials.EnrollAfterVerifiedLogin(scope,login with{Permissions=[]},"synthetic",Password,f.Now).IsSuccess);
        Assert.Equal(0L,f.Scalar("SELECT COUNT(*) FROM role_permissions WHERE role_id=(SELECT role_id FROM users WHERE username='synthetic');"));
        Assert.True(f.Runtime.OperatorCredentials.EnrollAfterVerifiedLogin(scope,login with{IsAuthenticated=false,ExpiresAtUtc=f.Now.AddDays(1)},"synthetic",Password,f.Now).IsFailure);
        Assert.True(f.Runtime.OperatorCredentials.CheckOfflineAuthority(f.ActorId,scope.DeviceId,scope.TenantId,scope.StoreId,scope.ServerEpoch,login.ExpiresAtUtc).IsFailure);
        Assert.True(f.Runtime.OperatorCredentials.CheckOfflineAuthority(f.ActorId,scope.DeviceId,scope.TenantId,scope.StoreId,scope.ServerEpoch,f.Now).IsSuccess);
    }

    [Fact]
    public async Task SuccessfulChangedRole_UsesOnlyCurrentRemotePermissions()
    {
        using var f=new CanonicalCashierFixture();var(scope,login)=await Authority(f);SetPlaceholder(f);
        Assert.True(f.Runtime.OperatorCredentials.EnrollAfterVerifiedLogin(scope,login,"synthetic",Password,f.Now).IsSuccess);
        Guid role=Guid.Parse(Salryn.Infrastructure.Data.Seeding.DefaultSeedData.CashierRoleId);
        Assert.True(f.Runtime.OperatorCredentials.EnrollAfterVerifiedLogin(scope,login with{RoleId=role,Permissions=["Products.View"]},"synthetic",Password,f.Now).IsSuccess);
        var user=(await f.Runtime.Authentication.LoginAsync(new LoginRequest("synthetic",Password))).Value;
        Assert.Equal(role,user.RoleId);Assert.Equal(["Products.View"],user.Permissions);
    }

    [Fact]
    public async Task FrozenWallClock_CannotExtendOperatorAuthorityBeyondMonotonicDeadline()
    {
        using var f=new CanonicalCashierFixture();var(scope,login)=await Authority(f);SetPlaceholder(f);
        login=login with{ExpiresAtUtc=f.Now.AddSeconds(30)};
        Assert.True(f.Runtime.OperatorCredentials.EnrollAfterVerifiedLogin(scope,login,"synthetic",Password,f.Now).IsSuccess);
        long elapsed=10000;
        var guard=new MobileCashierDeviceGuard(new ProtectedMobileEnrollmentReader(f.Secrets),f.Secrets,()=>new(f.Now,elapsed,1));
        var controller=new MobileCashierController(f.Runtime,guard);
        Assert.True((await controller.LoginAsync("synthetic",Password)).IsSuccess);
        Assert.True((await controller.MenuAsync()).IsSuccess);
        elapsed+=31000;
        var denied=await controller.MenuAsync();
        Assert.True(denied.IsFailure);Assert.Equal("Mobile.OperatorAuthorityExpired",denied.ErrorDetail.Code);
    }

    private static void SetPlaceholder(CanonicalCashierFixture fixture) =>
        fixture.Execute("UPDATE users SET password_hash='" + Placeholder + "' WHERE username='synthetic';");

    private static async Task<(MobileApprovedCredentialScope Scope, MobileVerifiedOperatorLogin Login)> Authority(CanonicalCashierFixture fixture)
    {
        MobileEnrollmentBinding binding = (await new ProtectedMobileEnrollmentReader(fixture.Secrets).ReadAsync())!;
        using var connection=fixture.Runtime.ConnectionFactory.CreateConnection(); connection.Open();
        using var command=connection.CreateCommand(); command.CommandText="SELECT u.role_id,p.permission FROM users u JOIN role_permissions p ON p.role_id=u.role_id WHERE u.id=$user;";
        command.Parameters.AddWithValue("$user",fixture.ActorId.ToString("D"));
        using var reader=command.ExecuteReader(); Guid role=Guid.Empty; List<string> permissions=[];
        while(reader.Read()) {role=Guid.Parse(reader.GetString(0));permissions.Add(reader.GetString(1));}
        return (new(binding.DeviceId, binding.TenantId, binding.StoreId, binding.ServerEpoch, true),
            new(fixture.ActorId, binding.DeviceId, binding.TenantId, binding.StoreId, binding.ServerEpoch, fixture.Now.AddMinutes(10), true) {RoleId=role,Permissions=permissions});
    }
}

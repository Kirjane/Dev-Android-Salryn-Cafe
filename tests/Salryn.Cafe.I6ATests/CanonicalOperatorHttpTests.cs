using System.Net;
using System.Net.Http.Json;
using Salryn.Sync.Central.Server;
using Xunit;

namespace Salryn.Cafe.I6ATests;

public sealed class CanonicalOperatorHttpTests
{
    [Fact]
    public async Task DeviceAuthenticationCannotImpersonateOperator_IndependentCredentialAndCurrentAuthorityRequired()
    {
        string root=Path.Combine(Path.GetTempPath(),"canonical-http-"+Guid.NewGuid().ToString("N"));
        var target=C4ProofDatabase.Create(root);
        target.Execute("UPDATE users SET password_hash=$hash WHERE id=$id;",("$hash",Salryn.Core.Security.SalrynPasswordHasher.HashPassword(CanonicalDomainTransactionTests.Password).Value),("$id",C4ProofIds.ActorUserId.ToString("D")));
        using var host=new CentralSyncTestHost(target.Paths.DatabasePath,canonical:true,canonicalStore:C4ProofIds.StoreId);
        using var admin=host.CreateHttpsClient();
        string adminToken=await host.LoginAsAdminAsync(admin);
        using var device=await host.EnrollApproveAndAuthenticateDeviceAsync(admin,adminToken,"Canonical cashier");
        var context=new AuthenticatedDeviceContext(device.DeviceId,host.TenantId,host.StoreId,"Salryn.Cafe","OfflineAndroidCashier","Active","synthetic");
        var envelope=CanonicalDomainTransactionTests.SaleEnvelope(context);
        var denied=await device.Http.PostAsJsonAsync("/api/sync/v1/push",new PushOperationsRequest(1,[envelope]));
        Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        var badLogin=await device.Http.PostAsJsonAsync("/api/sync/v1/operators/login",new OperatorLoginRequest("c4-proof","wrong"));
        Assert.Equal(HttpStatusCode.Unauthorized,badLogin.StatusCode);
        var loginResponse=await device.Http.PostAsJsonAsync("/api/sync/v1/operators/login",new OperatorLoginRequest("c4-proof",CanonicalDomainTransactionTests.Password));
        Assert.Equal(HttpStatusCode.OK,loginResponse.StatusCode);
        var login=(await loginResponse.Content.ReadFromJsonAsync<OperatorLoginResponse>())!;
        Assert.Equal(C4ProofIds.ActorUserId,login.UserId);
        device.Http.DefaultRequestHeaders.Add(CentralSyncEndpoints.OperatorTokenHeader,login.OperatorToken);
        var applied=await device.Http.PostAsJsonAsync("/api/sync/v1/push",new PushOperationsRequest(1,[envelope]));
        Assert.Equal(HttpStatusCode.OK,applied.StatusCode);
        var result=(await applied.Content.ReadFromJsonAsync<PushOperationsResponse>())!;
        Assert.True(Assert.Single(result.Results).Resolution.IsApplied);
        Assert.Equal(500,target.ScalarLong("SELECT SUM(total_cents) FROM sales;"));
        target.Execute("DELETE FROM role_permissions WHERE permission='Sales.Create';");
        var noLongerAuthorized=await device.Http.PostAsJsonAsync("/api/sync/v1/push",new PushOperationsRequest(1,[envelope]));
        Assert.Equal(HttpStatusCode.Forbidden,noLongerAuthorized.StatusCode);
        Assert.Equal(1,target.ScalarLong("SELECT COUNT(*) FROM sales;"));
        Assert.Equal(1,target.ScalarLong("SELECT COUNT(*) FROM sync_change_feed;"));
        Assert.DoesNotContain(login.OperatorToken,target.ScalarString("SELECT token_hash FROM sync_operator_sessions LIMIT 1;")!);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Salryn.Api.Local;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Infrastructure.Paths;
using Salryn.Mobile.Foundation.P0;
using Xunit;

namespace Salryn.Cafe.I3Tests;

public sealed class CafeCentralConnectionTests
{
    [Theory]
    [InlineData("http://127.0.0.1:5443")]
    [InlineData("https://example.org")]
    [InlineData("https://localhost/path")]
    [InlineData("https://user:password@localhost")]
    [InlineData("https://localhost/?secret=1")]
    [InlineData("https://localhost/#fragment")]
    public void CohostOrigin_RejectsRemotePlaintextOrDecoratedUrls(string origin)
    {
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Throws<InvalidOperationException>(()=>CafeCentralConnection.Validate(new(origin,Guid.NewGuid(),Guid.NewGuid(),Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()))));
    }

    [Fact]
    public void CohostOrigin_AcceptsIndependentP256LoopbackPin_RejectsMissingScopeAndTrailingKeyBytes()
    {
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var config=new CafeCentralConfiguration("https://localhost:5443",Guid.NewGuid(),Guid.NewGuid(),Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        CafeCentralConnection.Validate(config);
        Assert.Throws<InvalidOperationException>(()=>CafeCentralConnection.Validate(config with{TenantId=Guid.Empty}));
        Assert.Throws<InvalidOperationException>(()=>CafeCentralConnection.Validate(config with{PinnedServerKey=Convert.ToBase64String(key.ExportSubjectPublicKeyInfo().Concat(new byte[]{0}).ToArray())}));
    }

    [Fact]
    public async Task ExistingFinancialEvidence_RefusesEnrollmentWithoutChangingTerminalOrWritingSecrets()
    {
        string root=Path.Combine(Path.GetTempPath(),"cafe-central-retained-"+Guid.NewGuid().ToString("N"));
        var(factory,mutation)=CanonicalOrderCompletionFixture.Create(root);
        Guid instance=Guid.NewGuid(),tenant=Guid.NewGuid();
        using var db=factory.CreateConnection();db.Open();using var command=db.CreateCommand();
        command.CommandText="""
            CREATE TABLE sync_server_state(singleton_id INTEGER,central_instance_id TEXT,server_epoch INTEGER);
            CREATE TABLE sync_canonical_domain_scope(singleton_id INTEGER,tenant_id TEXT,store_id TEXT);
            INSERT INTO sync_server_state VALUES(1,$instance,1);
            INSERT INTO sync_canonical_domain_scope VALUES(1,$tenant,$store);
            """;
        command.Parameters.AddWithValue("$instance",instance.ToString("D"));command.Parameters.AddWithValue("$tenant",tenant.ToString("D"));command.Parameters.AddWithValue("$store",mutation.StoreId.ToString("D"));command.ExecuteNonQuery();
        command.CommandText="SELECT terminal_id FROM deployment_identity;";string terminal=(string)command.ExecuteScalar()!;
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var composition=SalrynLocalComposition.Create(DbPaths.CreateAtRoot(root));
        var secrets=new MemorySecrets();var handler=new HealthHandler(instance,key=>ReadProbe(composition,key));
        var connector=new CafeCentralConnection(composition,secrets,()=>new HttpClient(handler,false));
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>connector.EnrollAsync(new("https://localhost:5443",tenant,mutation.StoreId,Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())),"synthetic-invite","QA"));
        Assert.Contains("before opening",error.Message);
        Assert.Empty(secrets.Values);
        Assert.Equal(2,handler.Requests); // Health and committed random-file proof.
        Assert.Equal(0,CountProbes(composition));
        command.CommandText="SELECT terminal_id FROM deployment_identity;";Assert.Equal(terminal,(string)command.ExecuteScalar()!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnrollmentRetry_OnlyKnownRejectionMayUseAnotherInvite(bool unknownResponse)
    {
        string root=Path.Combine(Path.GetTempPath(),"cafe-central-enroll-"+Guid.NewGuid().ToString("N"));
        var composition=SalrynLocalComposition.Create(DbPaths.CreateAtRoot(root));await composition.InitializeDatabaseAsync();
        Guid instance=Guid.NewGuid(),tenant=Guid.NewGuid();
        using var db=composition.ConnectionFactory.CreateConnection();db.Open();using var command=db.CreateCommand();
        command.CommandText="SELECT store_id FROM deployment_identity;";Guid store=Guid.Parse((string)command.ExecuteScalar()!);
        command.CommandText="CREATE TABLE sync_server_state(singleton_id INTEGER,central_instance_id TEXT,server_epoch INTEGER); CREATE TABLE sync_canonical_domain_scope(singleton_id INTEGER,tenant_id TEXT,store_id TEXT); INSERT INTO sync_server_state VALUES(1,$instance,1); INSERT INTO sync_canonical_domain_scope VALUES(1,$tenant,$store);";
        command.Parameters.AddWithValue("$instance",instance.ToString("D"));command.Parameters.AddWithValue("$tenant",tenant.ToString("D"));command.Parameters.AddWithValue("$store",store.ToString("D"));command.ExecuteNonQuery();
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);var secrets=new MemorySecrets();var handler=new EnrollmentFailureHandler(instance,unknownResponse,key=>ReadProbe(composition,key));
        var connector=new CafeCentralConnection(composition,secrets,()=>new HttpClient(handler,false));
        var config=new CafeCentralConfiguration("https://localhost:5443",tenant,store,Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        if(unknownResponse)await Assert.ThrowsAsync<HttpRequestException>(()=>connector.EnrollAsync(config,"invite","QA"));
        else await Assert.ThrowsAsync<InvalidOperationException>(()=>connector.EnrollAsync(config,"invite","QA"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>connector.EnrollAsync(config,"another-invite","QA"));
        Assert.Equal(unknownResponse?1:2,handler.EnrollmentRequests);
    }

    [Fact]
    public async Task CohostProbe_WrongValue_FailsClosedBeforeEnrollmentAndCleansLocalMarker()
    {
        string root=Path.Combine(Path.GetTempPath(),"cafe-central-probe-denied-"+Guid.NewGuid().ToString("N"));
        var composition=SalrynLocalComposition.Create(DbPaths.CreateAtRoot(root));
        await composition.InitializeDatabaseAsync();
        Guid instance=Guid.NewGuid(),tenant=Guid.NewGuid(),store;
        using(var db=composition.ConnectionFactory.CreateConnection())
        {
            db.Open();using var command=db.CreateCommand();
            command.CommandText="SELECT store_id FROM deployment_identity;";store=Guid.Parse((string)command.ExecuteScalar()!);
            command.CommandText="CREATE TABLE sync_server_state(singleton_id INTEGER,central_instance_id TEXT,server_epoch INTEGER); CREATE TABLE sync_canonical_domain_scope(singleton_id INTEGER,tenant_id TEXT,store_id TEXT); INSERT INTO sync_server_state VALUES(1,$instance,1); INSERT INTO sync_canonical_domain_scope VALUES(1,$tenant,$store);";
            command.Parameters.AddWithValue("$instance",instance.ToString("D"));command.Parameters.AddWithValue("$tenant",tenant.ToString("D"));command.Parameters.AddWithValue("$store",store.ToString("D"));command.ExecuteNonQuery();
        }
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var secrets=new MemorySecrets();
        var handler=new HealthHandler(instance,_=>new string('F',64));
        var connector=new CafeCentralConnection(composition,secrets,()=>new HttpClient(handler,false));
        var config=new CafeCentralConfiguration("https://localhost:5443",tenant,store,Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>connector.EnrollAsync(config,"unused-invite","QA"));
        Assert.Contains("probe mismatched",error.Message);
        Assert.Equal(2,handler.Requests);
        Assert.Empty(secrets.Values);
        Assert.Equal(0,CountProbes(composition));
    }

    private static string? ReadProbe(SalrynLocalComposition composition,Guid key)
    {
        using var db=composition.ConnectionFactory.CreateConnection();db.Open();
        using var command=db.CreateCommand();
        command.CommandText="SELECT value FROM app_metadata WHERE key=$key;";
        command.Parameters.AddWithValue("$key","salryn.cafe.cohost.probe."+key.ToString("D"));
        return command.ExecuteScalar() as string;
    }

    private static long CountProbes(SalrynLocalComposition composition)
    {
        using var db=composition.ConnectionFactory.CreateConnection();db.Open();
        using var command=db.CreateCommand();
        command.CommandText="SELECT COUNT(*) FROM app_metadata WHERE key LIKE 'salryn.cafe.cohost.probe.%';";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static HttpResponseMessage ProbeResponse(HttpRequestMessage request,Func<Guid,string?> readProbe)
    {
        var query=request.RequestUri!.Query;
        if(!query.StartsWith("?key=",StringComparison.Ordinal) || !Guid.TryParse(query[5..],out var key))
            return new(HttpStatusCode.BadRequest);
        var value=readProbe(key);
        return new(value is null?HttpStatusCode.Conflict:HttpStatusCode.OK){Content=JsonContent.Create(new{value})};
    }

    [Fact]
    public async Task WindowsDpapi_RoundTripsProtectedCiphertextWithoutPlaintextOnDisk()
    {
        if(!OperatingSystem.IsWindows())
        {
            var unsupported=new CafeCentralProtectedSecrets(Path.Combine(Path.GetTempPath(),"cafe-dpapi-denied-"+Guid.NewGuid().ToString("N")));
            await Assert.ThrowsAsync<PlatformNotSupportedException>(()=>unsupported.WriteAsync("signing",[1,2,3]));return;
        }
        string root=Path.Combine(Path.GetTempPath(),"cafe-dpapi-"+Guid.NewGuid().ToString("N"));
        var store=new CafeCentralProtectedSecrets(root);
        byte[] secret=System.Text.Encoding.UTF8.GetBytes("synthetic-private-device-key");
        await store.WriteAsync("signing",secret);
        Assert.Equal(secret,await store.ReadAsync("signing"));
        Assert.DoesNotContain("synthetic-private-device-key",System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Directory.GetFiles(root).Single())));
    }

    [Fact]
    public async Task LiveHostCentralEndpoints_RejectUnauthenticatedRequestsBeforeNetwork()
    {
        var composition=SalrynLocalComposition.Create(DbPaths.CreateAtRoot(Path.Combine(Path.GetTempPath(),"cafe-central-route-"+Guid.NewGuid().ToString("N"))));
        await using var host=await LocalApiApplicationFactory.CreateAsync([],new(){Composition=composition,Url="http://127.0.0.1:0",EnableStaticWebAssets=false,WriteDevelopmentDatabasePath=false});
        await host.StartAsync();using var client=new HttpClient(){BaseAddress=new Uri(host.Urls.Single())};
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var enrolled=await client.PostAsJsonAsync("/api/cafe/central/enroll",new LocalCafeCentralSyncEndpoints.EnrollRequest(new("https://localhost:5443",Guid.NewGuid(),Guid.NewGuid(),Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())),"invite","QA"));
        Assert.Equal(HttpStatusCode.Unauthorized,enrolled.StatusCode);
        using var sync=await client.PostAsJsonAsync("/api/cafe/central/synchronize",new LocalCafeCentralSyncEndpoints.SynchronizeRequest("synthetic"));
        Assert.Equal(HttpStatusCode.Unauthorized,sync.StatusCode);await host.StopAsync();
    }
    private sealed class MemorySecrets:IDeviceSecretStore
    {
        public Dictionary<string,byte[]> Values {get;}=[];
        public Task<byte[]?> ReadAsync(string key,CancellationToken ct=default)=>Task.FromResult(Values.TryGetValue(key,out var bytes)?bytes.ToArray():null);
        public Task WriteAsync(string key,byte[] secret,CancellationToken ct=default){Values[key]=secret.ToArray();return Task.CompletedTask;}
    }
    private sealed class EnrollmentFailureHandler(Guid instance,bool unknown,Func<Guid,string?> readProbe):HttpMessageHandler
    {
        public int EnrollmentRequests{get;private set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            string path=request.RequestUri!.AbsolutePath;
            if(path=="/api/sync/v1/health")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new{productId="Salryn.Cafe",centralInstanceId=instance,serverEpoch=1L})});
            if(path=="/api/sync/v1/cohost/probe")return Task.FromResult(ProbeResponse(request,readProbe));
            Assert.Equal("/api/sync/v1/devices/enroll",path);
            EnrollmentRequests++;
            if(unknown)throw new HttpRequestException("Synthetic lost enrollment response");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest){Content=JsonContent.Create(new{errorCode="ERR_JOIN_CODE"})});
        }
    }
    private sealed class HealthHandler(Guid instance,Func<Guid,string?> readProbe):HttpMessageHandler
    {
        public int Requests{get;private set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Requests++;
            string path=request.RequestUri!.AbsolutePath;
            if(path=="/api/sync/v1/health")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new{productId="Salryn.Cafe",centralInstanceId=instance,serverEpoch=1L})});
            Assert.Equal("/api/sync/v1/cohost/probe",path);
            return Task.FromResult(ProbeResponse(request,readProbe));
        }
    }
}

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Salryn.Composition.Local;
using Salryn.Infrastructure.Data.Sync;
using Salryn.Mobile.Foundation.P0;
using Salryn.Mobile.Foundation.Security;
using Salryn.Mobile.Foundation.Sync;

namespace Salryn.Api.Local.Services;

public sealed record CafeCentralConfiguration(string HttpsOrigin,Guid TenantId,Guid StoreId,string PinnedServerKey,Guid? DeviceId=null)
{
    public bool EnrollmentOutcomeUnknown {get;init;}
}
public sealed record CafeCentralSyncResult(int Applied,int Pending,long ServerEpoch,string Profile="WINDOWS_LOCAL_DISK_COHOST_QA");

/// <summary>Explicit same-machine canonical authority profile. Never bootstraps over Host data or opens a network SQLite file.</summary>
public sealed class CafeCentralConnection
{
    private const string ConfigKey="salryn.cafe.windows.central.configuration.v1",SigningKey="salryn.cafe.windows.central.signing.v1";
    private readonly SalrynLocalComposition composition;
    private readonly IDeviceSecretStore secrets;
    private readonly Func<HttpClient> clientFactory;
    private readonly SemaphoreSlim gate=new(1,1);
    public CafeCentralConnection(SalrynLocalComposition composition) : this(composition,
        new CafeCentralProtectedSecrets(Path.Combine(composition.DbPaths.ConfigPath,"central-protected")),()=>new HttpClient()) {}
    public CafeCentralConnection(SalrynLocalComposition composition,IDeviceSecretStore secrets,Func<HttpClient> clientFactory)
    {this.composition=composition;this.secrets=secrets;this.clientFactory=clientFactory;}

    public async Task<Guid> EnrollAsync(CafeCentralConfiguration requested,string joinCode,string deviceName,CancellationToken ct=default)
    {
        Validate(requested); await gate.WaitAsync(ct);
        try
        {
            await AssertCohostAsync(requested,ct);
            var existing=await ReadAsync(ct);
            if(existing is not null && (existing.HttpsOrigin!=requested.HttpsOrigin || existing.StoreId!=requested.StoreId || existing.TenantId!=requested.TenantId || existing.PinnedServerKey!=requested.PinnedServerKey))
                throw new InvalidOperationException("Existing Central authority is preserved; administrator reconciliation is required.");
            if(existing?.DeviceId is {} enrolled)return enrolled;
            if(existing?.EnrollmentOutcomeUnknown==true)throw new InvalidOperationException("Prior enrollment outcome is unresolved; administrator reconciliation is required before another invite.");
            AssertNoRetainedOperations();
            using ECDsa key=await SigningAsync(true,ct);
            await secrets.WriteAsync(ConfigKey,JsonSerializer.SerializeToUtf8Bytes(requested with{EnrollmentOutcomeUnknown=true}),ct);
            using HttpClient http=Client(requested);
            using var response=await http.PostAsJsonAsync("/api/sync/v1/devices/enroll",new {joinCode,requested.TenantId,requested.StoreId,productId="Salryn.Cafe",deviceName,publicKeySpkiBase64=Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())},ct);
            if((int)response.StatusCode is >=400 and <500)
                await secrets.WriteAsync(ConfigKey,JsonSerializer.SerializeToUtf8Bytes(requested with{EnrollmentOutcomeUnknown=false}),ct);
            await RequireAsync(response,ct);
            var result=await response.Content.ReadFromJsonAsync<Enrollment>(cancellationToken:ct)??throw new InvalidOperationException("Empty device enrollment response.");
            if(result.DeviceId==Guid.Empty)throw new InvalidOperationException("No device identity returned.");
            // Persist returned identity before any local binding; a lost response never silently re-enrolls.
            await secrets.WriteAsync(ConfigKey,JsonSerializer.SerializeToUtf8Bytes(requested with{DeviceId=result.DeviceId,EnrollmentOutcomeUnknown=false}),ct);
            return result.DeviceId;
        }
        finally{gate.Release();}
    }

    public async Task<CafeCentralSyncResult> ConnectAsync(Guid actor,string username,string password,CancellationToken ct=default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var config=await ReadAsync(ct)??throw new InvalidOperationException("Enroll and approve this Windows device first.");
            Validate(config);if(config.DeviceId is null)throw new InvalidOperationException("Enrollment response is unresolved; preserve setup for reconciliation.");
            Health health=await AssertCohostAsync(config,ct);
            using HttpClient http=Client(config);using ECDsa key=await SigningAsync(false,ct);
            var transport=new HttpSyncOutboxTransport(http,()=>null,()=>(config.DeviceId.Value,config.TenantId,config.StoreId,health.ServerEpoch));
            var authentication=await transport.AuthenticateWithChallengeAsync(config.DeviceId.Value,config.TenantId,config.StoreId,key,ct);
            var lease=authentication.OfflineLease;
            if(!authentication.IsSuccess || lease is null || !MobileOfflineLeaseGuard.VerifyLeaseSignature(lease,config.PinnedServerKey) ||
                lease.DeviceId!=config.DeviceId || lease.TenantId!=config.TenantId || lease.StoreId!=config.StoreId ||
                lease.ServerEpoch!=health.ServerEpoch || lease.ExpiresAtUtc<=DateTimeOffset.UtcNow || lease.IssuedAtUtc>DateTimeOffset.UtcNow.AddMinutes(2))
                throw new InvalidOperationException(authentication.ErrorCode??"Central device approval or signed lease verification failed.");
            http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",authentication.AccessToken);
            http.DefaultRequestHeaders.Add("X-Salryn-Tenant-Id",config.TenantId.ToString("D"));
            http.DefaultRequestHeaders.Add("X-Salryn-Store-Id",config.StoreId.ToString("D"));
            http.DefaultRequestHeaders.Add("X-Salryn-Device-Id",config.DeviceId.Value.ToString("D"));
            using var loginResponse=await http.PostAsJsonAsync("/api/sync/v1/operators/login",new{username,password},ct);
            await RequireAsync(loginResponse,ct);
            var login=await loginResponse.Content.ReadFromJsonAsync<Operator>(cancellationToken:ct)??throw new InvalidOperationException("Empty Central operator response.");
            try
            {
                if(login.UserId!=actor || login.ServerEpoch!=health.ServerEpoch || login.ExpiresAtUtc<=DateTimeOffset.UtcNow)
                    throw new InvalidOperationException("Central login must match the current local operator and authority epoch.");
                BindApprovedTerminal(config);
                var outbox=new SqliteSyncOutboxStore(composition.ConnectionFactory);
                var authenticated=new HttpSyncOutboxTransport(http,()=>authentication.AccessToken,
                    ()=>(config.DeviceId.Value,config.TenantId,config.StoreId,login.ServerEpoch),operatorTokenProvider:()=>login.OperatorToken);
                int applied=0;
                foreach(var batch in outbox.ReadPending(config.DeviceId.Value).Where(row=>row.Envelope.ActorUserId==actor).Select(row=>row.Envelope).Take(2000).Chunk(100))
                {
                    var outcome=await authenticated.PushBatchAsync(batch,ct);
                    foreach(var resolution in outcome.Resolutions.Where(result=>result.IsApplied)){outbox.RecordCentralResult(resolution);applied++;}
                    if(outcome.IsRevokedFenced || outcome.RequiresRebootstrap)throw new InvalidOperationException(outcome.ErrorCode??"Central fenced synchronization; local evidence retained.");
                    if(!outcome.IsSuccess)throw new InvalidOperationException(outcome.ErrorCode??"Central retained unresolved operations; local evidence retained.");
                }
                // No client projection/bootstrap: this profile proves exact same local canonical DB instance above.
                return new(applied,outbox.ReadPending(config.DeviceId.Value).Count,login.ServerEpoch);
            }
            finally
            {
                using var logout=new HttpRequestMessage(HttpMethod.Post,"/api/sync/v1/operators/logout");logout.Headers.Add("X-Salryn-Operator-Token",login.OperatorToken);
                using var ignored=await http.SendAsync(logout,ct);
            }
        }
        finally{gate.Release();}
    }
    private async Task<Health> AssertCohostAsync(CafeCentralConfiguration config,CancellationToken ct)
    {
        string database=Path.GetFullPath(composition.ConnectionFactory.DatabasePath);
        if(database.StartsWith(@"\\",StringComparison.Ordinal) ||
            (OperatingSystem.IsWindows() && new DriveInfo(Path.GetPathRoot(database)!).DriveType==DriveType.Network))
            throw new InvalidOperationException("Cohost canonical database must be on a local disk, never a network share.");
        using HttpClient http=Client(config);
        var health=await http.GetFromJsonAsync<Health>("/api/sync/v1/health",ct)??throw new InvalidOperationException("Central health unavailable.");
        using var connection=composition.ConnectionFactory.CreateConnection();connection.Open();using var command=connection.CreateCommand();
        command.CommandText="SELECT COUNT(*) FROM sync_server_state s JOIN sync_canonical_domain_scope c ON c.singleton_id=s.singleton_id JOIN deployment_identity d ON d.store_id=c.store_id WHERE s.central_instance_id=$instance AND s.server_epoch=$epoch AND c.tenant_id=$tenant AND c.store_id=$store;";
        command.Parameters.AddWithValue("$instance",health.CentralInstanceId.ToString("D"));command.Parameters.AddWithValue("$epoch",health.ServerEpoch);
        command.Parameters.AddWithValue("$tenant",config.TenantId.ToString("D"));command.Parameters.AddWithValue("$store",config.StoreId.ToString("D"));
        if(health.ProductId!="Salryn.Cafe" || Convert.ToInt64(command.ExecuteScalar())!=1)throw new InvalidOperationException("Central must use this exact Windows local-disk canonical database and scope; remote/independent databases are unsupported by this profile.");

        // Static instance/epoch/scope identifiers survive database copies. A committed,
        // unpredictable local-file marker must be visible through the Central service
        // before enrollment, terminal binding, or any outbox reconciliation.
        Guid probeId=Guid.NewGuid();
        string probeKey="salryn.cafe.cohost.probe."+probeId.ToString("D");
        string probeValue=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using(var insert=connection.CreateCommand())
        {
            insert.CommandText="INSERT INTO app_metadata(key,value,updated_at) VALUES($key,$value,$at);";
            insert.Parameters.AddWithValue("$key",probeKey);
            insert.Parameters.AddWithValue("$value",probeValue);
            insert.Parameters.AddWithValue("$at",DateTimeOffset.UtcNow.ToString("O"));
            if(insert.ExecuteNonQuery()!=1)throw new InvalidOperationException("Local cohost probe could not be persisted.");
        }
        try
        {
            using var response=await http.GetAsync("/api/sync/v1/cohost/probe?key="+probeId.ToString("D"),ct);
            if(!response.IsSuccessStatusCode)
                throw new InvalidOperationException("Central cannot verify this exact local canonical SQLite file.");
            var returned=await response.Content.ReadFromJsonAsync<CohostProbe>(cancellationToken:ct);
            if(returned?.Value is not { Length:64 } value ||
                !CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.ASCII.GetBytes(value),
                    System.Text.Encoding.ASCII.GetBytes(probeValue)))
                throw new InvalidOperationException("Central cohost file probe mismatched; independent or copied databases are forbidden.");
        }
        finally
        {
            // Must run even on timeout, cancellation or an invalid/missing response.
            // Never delete a marker changed by another actor.
            using var cleanup=connection.CreateCommand();
            cleanup.CommandText="DELETE FROM app_metadata WHERE key=$key AND value=$value;";
            cleanup.Parameters.AddWithValue("$key",probeKey);
            cleanup.Parameters.AddWithValue("$value",probeValue);
            if(cleanup.ExecuteNonQuery()!=1)
                throw new InvalidOperationException("Local cohost probe cleanup failed; reconciliation is required.");
        }
        return health;
    }
    private void AssertNoRetainedOperations()
    {
        using var connection=composition.ConnectionFactory.CreateConnection();connection.Open();using var command=connection.CreateCommand();
        command.CommandText="SELECT (SELECT COUNT(*) FROM sync_outbox)+(SELECT COUNT(*) FROM cafe_orders)+(SELECT COUNT(*) FROM sales)+(SELECT COUNT(*) FROM cash_movements)+(SELECT COUNT(*) FROM cashier_sessions)+(SELECT COUNT(*) FROM payments)+(SELECT COUNT(*) FROM accounts_receivable_entries)+(SELECT COUNT(*) FROM stock_movements)+(SELECT COUNT(*) FROM cafe_bookings)+(SELECT COUNT(*) FROM cafe_delivery_fulfillments)+(SELECT COUNT(*) FROM cafe_incidents)+(SELECT COUNT(*) FROM cafe_attendance_events)+(SELECT COUNT(*) FROM ingredient_stock_movements);";
        if(Convert.ToInt64(command.ExecuteScalar())!=0)throw new InvalidOperationException("Bind Central device before opening any drawer or creating operations; existing identities are preserved.");
    }
    private void BindApprovedTerminal(CafeCentralConfiguration config)
    {
        using var connection=composition.ConnectionFactory.CreateConnection();connection.Open();using var transaction=connection.BeginTransaction(deferred:false);using var command=connection.CreateCommand();command.Transaction=transaction;
        command.CommandText="SELECT terminal_id FROM deployment_identity WHERE store_id=$store;";command.Parameters.AddWithValue("$store",config.StoreId.ToString("D"));
        string? current=command.ExecuteScalar() as string;
        if(current!=config.DeviceId!.Value.ToString("D"))
        {
            command.CommandText="SELECT (SELECT COUNT(*) FROM sync_outbox)+(SELECT COUNT(*) FROM cafe_orders)+(SELECT COUNT(*) FROM sales)+(SELECT COUNT(*) FROM cash_movements)+(SELECT COUNT(*) FROM cashier_sessions)+(SELECT COUNT(*) FROM payments)+(SELECT COUNT(*) FROM accounts_receivable_entries)+(SELECT COUNT(*) FROM stock_movements)+(SELECT COUNT(*) FROM cafe_bookings)+(SELECT COUNT(*) FROM cafe_delivery_fulfillments)+(SELECT COUNT(*) FROM cafe_incidents)+(SELECT COUNT(*) FROM cafe_attendance_events)+(SELECT COUNT(*) FROM ingredient_stock_movements);";
            if(Convert.ToInt64(command.ExecuteScalar())!=0)throw new InvalidOperationException("Existing operation identities prevent device rebinding.");
            command.CommandText="UPDATE deployment_identity SET terminal_id=$device,updated_at=$at WHERE store_id=$store;";
            command.Parameters.AddWithValue("$device",config.DeviceId.Value.ToString("D"));command.Parameters.AddWithValue("$at",DateTimeOffset.UtcNow.ToString("O"));
            if(command.ExecuteNonQuery()!=1)throw new InvalidOperationException("Store authority changed during binding.");
        }
        transaction.Commit();
    }
    private HttpClient Client(CafeCentralConfiguration config){var client=clientFactory();client.BaseAddress=new Uri(config.HttpsOrigin);client.Timeout=TimeSpan.FromSeconds(30);client.DefaultRequestHeaders.Add("X-Salryn-Product-Id","Salryn.Cafe");return client;}
    private async Task<CafeCentralConfiguration?> ReadAsync(CancellationToken ct){var bytes=await secrets.ReadAsync(ConfigKey,ct);return bytes is null?null:JsonSerializer.Deserialize<CafeCentralConfiguration>(bytes);}
    private async Task<ECDsa> SigningAsync(bool create,CancellationToken ct)
    {
        var bytes=await secrets.ReadAsync(SigningKey,ct);var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        try{if(bytes is null){if(!create)throw new InvalidOperationException("Device key missing; reconcile setup.");bytes=key.ExportPkcs8PrivateKey();await secrets.WriteAsync(SigningKey,bytes,ct);}else key.ImportPkcs8PrivateKey(bytes,out _);return key;}
        catch{key.Dispose();throw;}finally{if(bytes is not null)CryptographicOperations.ZeroMemory(bytes);}
    }
    public static void Validate(CafeCentralConfiguration config)
    {
        if(!Uri.TryCreate(config.HttpsOrigin,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!uri.IsLoopback||uri.AbsolutePath!="/"||uri.UserInfo.Length>0||uri.Query.Length>0||uri.Fragment.Length>0||config.TenantId==Guid.Empty||config.StoreId==Guid.Empty)
            throw new InvalidOperationException("Cohost QA requires an approved HTTPS loopback origin and nonempty tenant/store scope.");
        using var key=ECDsa.Create();var bytes=Convert.FromBase64String(config.PinnedServerKey);key.ImportSubjectPublicKeyInfo(bytes,out int read);
        if(read!=bytes.Length||key.ExportParameters(false).Curve.Oid.Value!="1.2.840.10045.3.1.7")throw new InvalidOperationException("Independent P-256 signing-key pin required.");
    }
    private static async Task RequireAsync(HttpResponseMessage response,CancellationToken ct){if(!response.IsSuccessStatusCode)throw new InvalidOperationException($"Central returned HTTP {(int)response.StatusCode}; retained setup is preserved.");await Task.CompletedTask;}
    private sealed record Enrollment(Guid DeviceId);
    private sealed record Health(string ProductId,Guid CentralInstanceId,long ServerEpoch);
    private sealed record CohostProbe(string Value);
    private sealed record Operator(string OperatorToken,Guid UserId,DateTimeOffset ExpiresAtUtc,long ServerEpoch);
}

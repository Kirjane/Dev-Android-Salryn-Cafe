using Microsoft.AspNetCore.Http;
using Salryn.Api.Local.Services;
using Salryn.Infrastructure.Data.Services;
using Salryn.Core.Models;
using Xunit;

namespace Salryn.Cafe.I3Tests;

public sealed class CanonicalCompletionRecoveryTests
{
    [Fact]
    public void PartialCashArWithAllocatedDiscountsAndFeePostsCanonicalTotals()
    {
        var (factory, mutation) = CanonicalOrderCompletionFixture.Create(Path.Combine(Path.GetTempPath(), "cafe-c5-"+Guid.NewGuid().ToString("N")));
        var result = new SqliteCafeOrderCompletionCoordinator(factory).Complete(mutation);
        Assert.True(result.IsSuccess, result.Error);
        using var db = factory.CreateConnection(); db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT subtotal_cents,discount_cents,fee_total_cents,total_cents,balance_due_cents FROM sales;";
        using (var reader = command.ExecuteReader())
        {
            Assert.True(reader.Read());
            Assert.Equal(1000,reader.GetInt64(0)); Assert.Equal(200,reader.GetInt64(1));
            Assert.Equal(50,reader.GetInt64(2)); Assert.Equal(850,reader.GetInt64(3)); Assert.Equal(500,reader.GetInt64(4));
            Assert.False(reader.Read());
        }
        command.CommandText = "SELECT SUM(line_total_cents),SUM(discount_cents) FROM sale_items;";
        using(var reader = command.ExecuteReader())
        { Assert.True(reader.Read()); Assert.Equal(800,reader.GetInt64(0)); Assert.Equal(200,reader.GetInt64(1)); }
        Assert.True(new SqliteCafeOrderCompletionCoordinator(factory).Complete(mutation).IsSuccess);
        command.CommandText="SELECT COUNT(*) FROM sales;";
        Assert.Equal(1L,Convert.ToInt64(command.ExecuteScalar()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletionReplayMustBindOriginalOrderAndVersion(bool wrongVersion)
    {
        var (factory, mutation) = CanonicalOrderCompletionFixture.Create(Path.Combine(Path.GetTempPath(), "cafe-replay-"+Guid.NewGuid().ToString("N")));
        var service = new SqliteCafeOrderCompletionCoordinator(factory);
        var posted = service.Complete(mutation);
        Assert.True(posted.IsSuccess, posted.Error);
        var altered = wrongVersion ? mutation with { ExpectedVersion = 999 } : mutation with { OrderId = Guid.NewGuid() };
        Assert.True(service.Complete(altered).IsFailure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void V1ReplayRejectsForeignOrderOrOperator(bool foreignOperator)
    {
        var (factory, original) = CanonicalOrderCompletionFixture.Create(Path.Combine(Path.GetTempPath(), "cafe-v1-replay-"+Guid.NewGuid().ToString("N")));
        var mutation = new CafeOrderCompletionMutation(original.CompletionOperationId,original.SaleCommittedOperationId,
            original.DeviceId,original.OrderId,original.StoreId,original.ExpectedVersion,original.ActorUserId,
            original.OccurredAtUtc,[new(PaymentMethod.Cash,1000)]);
        var service = new SqliteCafeOrderCompletionCoordinator(factory);
        var posted = service.Complete(mutation);
        Assert.True(posted.IsSuccess,posted.Error);
        var foreign=Guid.NewGuid();
        using var db=factory.CreateConnection();db.Open();using var command=db.CreateCommand();
        command.CommandText="INSERT INTO users(id,username,display_name,password_hash,role_id,status,created_at) SELECT $id,'other','Other','synthetic',role_id,status,created_at FROM users WHERE id=$original;";
        command.Parameters.AddWithValue("$id",foreign.ToString("D"));command.Parameters.AddWithValue("$original",mutation.ActorUserId.ToString("D"));command.ExecuteNonQuery();
        var altered=foreignOperator ? mutation with { ActorUserId=foreign } : mutation with { OrderId=Guid.NewGuid() };
        Assert.True(service.Complete(altered).IsFailure);
    }

    [Fact]
    public async Task CommittedCanonicalCompletionRecoversHttpOutcomeAfterLostCallbackResponse()
    {
        var (factory, mutation) = CanonicalOrderCompletionFixture.Create(Path.Combine(Path.GetTempPath(), "cafe-outcome-"+Guid.NewGuid().ToString("N")));
        string endpoint = "POST /api/cafe/orders/{orderId:guid}/complete";
        var service = new SqliteCafeOrderCompletionCoordinator(factory);
        var guard = new CafeMutationIdempotencyGuard(factory);
        DefaultHttpContext Context()
        {
            var context = new DefaultHttpContext();
            context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection("Salryn.Cafe.CanonicalOrderCompletion"), "canonical completion"));
            return context;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => guard.ExecuteSyncAsync(Context(), endpoint,
            mutation.ActorUserId, mutation.CompletionOperationId, mutation, _ =>
            {
                var result = service.Complete(mutation);
                Assert.True(result.IsSuccess, result.Error);
                throw new InvalidOperationException("simulated loss after domain commit before HTTP outcome");
            }));
        var response = await new CafeMutationIdempotencyGuard(factory).ExecuteSyncAsync(Context(), endpoint,
            mutation.ActorUserId, mutation.CompletionOperationId, mutation, _ =>
            {
                var result = new SqliteCafeOrderCompletionCoordinator(factory).Complete(mutation);
                Assert.True(result.IsSuccess, result.Error);
                return new(200, result.Value with { Idempotent = false });
            });
        Assert.Equal(200, Assert.IsAssignableFrom<IStatusCodeHttpResult>(response).StatusCode);
        using var db = factory.CreateConnection(); db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sales;";
        Assert.Equal(1L, Convert.ToInt64(command.ExecuteScalar()));
        command.CommandText = "SELECT SUM(total_cents) FROM sales;";
        Assert.Equal(850L, Convert.ToInt64(command.ExecuteScalar()));
        command.CommandText = "SELECT COUNT(*) FROM cafe_mutation_idempotency;";
        Assert.Equal(1L, Convert.ToInt64(command.ExecuteScalar()));
        command.CommandText = "SELECT COUNT(*) FROM sync_outbox WHERE operation_id=$op;";
        command.Parameters.AddWithValue("$op",mutation.CompletionOperationId.ToString("D"));
        Assert.Equal(1L, Convert.ToInt64(command.ExecuteScalar()));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutcomeAndFinancialPostingShareCommitAndRecoverFromFault(bool failOutcomeWrite)
    {
        var (factory, mutation) = CanonicalOrderCompletionFixture.Create(Path.Combine(Path.GetTempPath(), "cafe-atomic-"+Guid.NewGuid().ToString("N")));
        var guard = new CafeMutationIdempotencyGuard(factory);
        string endpoint="POST /api/cafe/orders/{orderId:guid}/complete";
        DefaultHttpContext Context()
        {
            var context=new DefaultHttpContext();
            context.SetEndpoint(new Endpoint(null,new EndpointMetadataCollection("Salryn.Cafe.CanonicalOrderCompletion"),"canonical completion"));
            return context;
        }
        using var db=factory.CreateConnection();db.Open();using var query=db.CreateCommand();
        query.CommandText="SELECT COUNT(*) FROM sync_outbox;";long before=Convert.ToInt64(query.ExecuteScalar());
        if(failOutcomeWrite)
        {
            query.CommandText="CREATE TRIGGER injected_outcome_fault BEFORE INSERT ON cafe_mutation_idempotency BEGIN SELECT RAISE(ABORT,'injected'); END;";
            query.ExecuteNonQuery();
        }
        var context=Context();
        async Task<IResult> Execute(bool loseResponse) => await guard.ExecuteSyncAsync(context,endpoint,mutation.ActorUserId,
            mutation.CompletionOperationId,mutation,_ =>
            {
                var result=new SqliteCafeOrderCompletionCoordinator(factory).Complete(mutation,(connection,transaction,posted) =>
                    guard.PersistAtomicOutcome(context,mutation.CompletionOperationId,connection,transaction,
                        new(200,posted with { Idempotent=false })));
                if(result.IsFailure) return new(500,new { error=result.Error });
                if(loseResponse) throw new InvalidOperationException("lost HTTP response after canonical commit");
                return new(200,result.Value with { Idempotent=false });
            });
        if(failOutcomeWrite)
        {
            Assert.Equal(500,Assert.IsAssignableFrom<IStatusCodeHttpResult>(await Execute(false)).StatusCode);
            query.CommandText="SELECT COUNT(*) FROM sales;";Assert.Equal(0L,Convert.ToInt64(query.ExecuteScalar()));
            query.CommandText="SELECT COUNT(*) FROM sync_outbox;";Assert.Equal(before,Convert.ToInt64(query.ExecuteScalar()));
            query.CommandText="SELECT SUM(stock_on_hand_x1000) FROM products;";
            Assert.Equal(10000L,Convert.ToInt64(query.ExecuteScalar()));
            // No financial write survived the outcome failure; retry may safely use the same canonical operation.
            query.CommandText="DROP TRIGGER injected_outcome_fault;";query.ExecuteNonQuery();
            context=Context();
            Assert.Equal(200,Assert.IsAssignableFrom<IStatusCodeHttpResult>(await Execute(false)).StatusCode);
        }
        else await Assert.ThrowsAsync<InvalidOperationException>(()=>Execute(true));
        var replayContext=Context();
        var replay=await new CafeMutationIdempotencyGuard(factory).ExecuteSyncAsync(replayContext,endpoint,
            mutation.ActorUserId,mutation.CompletionOperationId,mutation,_=>throw new InvalidOperationException("must replay immutable HTTP outcome"));
        Assert.Equal(200,Assert.IsAssignableFrom<IStatusCodeHttpResult>(replay).StatusCode);
        Assert.Equal("true",replayContext.Response.Headers["X-Idempotency-Replayed"].ToString());
        query.CommandText="SELECT COUNT(*) FROM sales;";Assert.Equal(1L,Convert.ToInt64(query.ExecuteScalar()));
        query.CommandText="SELECT COUNT(*) FROM sync_outbox;";Assert.Equal(before+3,Convert.ToInt64(query.ExecuteScalar()));
        query.CommandText="SELECT response_json FROM cafe_mutation_idempotency;";
        using var outcome=System.Text.Json.JsonDocument.Parse((string)query.ExecuteScalar()!);
        Assert.False(outcome.RootElement.GetProperty("idempotent").GetBoolean());
    }

}

using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Salryn.Api.Local.Services;
using Xunit;

namespace Salryn.Cafe.I3Tests;

public sealed class MutationCrashFenceTests
{
    [Fact]
    public async Task FaultAfterCommittedCallback_RestartRetryMustNotExecuteAgain()
    {
        using var fx = new I3TestDatabaseFixture();
        using var connection = fx.ConnectionFactory.CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        // Synthetic posting probe: proves guard execution fencing, not Cafe ledger acceptance.
        command.CommandText = "CREATE TABLE synthetic_postings(amount_cents INTEGER NOT NULL); CREATE TRIGGER synthetic_cache_fault BEFORE INSERT ON cafe_mutation_idempotency BEGIN SELECT RAISE(ABORT,'injected cache crash'); END;";
        command.ExecuteNonQuery();
        Guid operation = Guid.NewGuid();
        IdempotentExecutionOutcome Post(Guid _)
        {
            using var db = fx.ConnectionFactory.CreateConnection();
            db.Open();
            using var insert = db.CreateCommand();
            insert.CommandText = "INSERT INTO synthetic_postings VALUES(100);";
            insert.ExecuteNonQuery();
            return new(200, new { amountCents = 100 });
        }
        await Assert.ThrowsAsync<SqliteException>(() => new CafeMutationIdempotencyGuard(fx.ConnectionFactory)
            .ExecuteSyncAsync(new DefaultHttpContext(), "POST /synthetic-posting", fx.AdminUserId,
                operation, new { amountCents = 100 }, Post));
        command.CommandText = "DROP TRIGGER synthetic_cache_fault;";
        command.ExecuteNonQuery();
        var retry = await new CafeMutationIdempotencyGuard(fx.ConnectionFactory)
            .ExecuteSyncAsync(new DefaultHttpContext(), "POST /synthetic-posting", fx.AdminUserId,
                operation, new { amountCents = 100 }, Post);
        Assert.Equal(409, Assert.IsAssignableFrom<IStatusCodeHttpResult>(retry).StatusCode);
        command.CommandText = "SELECT SUM(amount_cents) FROM synthetic_postings;";
        Assert.Equal(100L, Convert.ToInt64(command.ExecuteScalar()));
        command.CommandText = "SELECT COUNT(*) FROM cafe_mutation_idempotency;";
        Assert.Equal(0L, Convert.ToInt64(command.ExecuteScalar()));
    }
    [Fact]
    public async Task IndependentGuardsCannotExecuteSameOperationWhileFirstIsInFlight()
    {
        using var fx = new I3TestDatabaseFixture();
        Guid operation = Guid.NewGuid();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int executions = 0;
        var first = new CafeMutationIdempotencyGuard(fx.ConnectionFactory).ExecuteAsync(
            new DefaultHttpContext(), "POST /synthetic-posting", fx.AdminUserId, operation, new { amount = 100 },
            async _ => { Interlocked.Increment(ref executions); entered.SetResult(); await release.Task; return new(200, new { amount = 100 }); });
        await entered.Task;
        try
        {
            var retries = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
                new CafeMutationIdempotencyGuard(fx.ConnectionFactory).ExecuteSyncAsync(
                    new DefaultHttpContext(), "POST /synthetic-posting", fx.AdminUserId, operation, new { amount = 100 },
                    _ => { Interlocked.Increment(ref executions); return new(200, new { amount = 100 }); })));
            Assert.All(retries, r => Assert.Equal(409, Assert.IsAssignableFrom<IStatusCodeHttpResult>(r).StatusCode));
            var changed = await new CafeMutationIdempotencyGuard(fx.ConnectionFactory).ExecuteSyncAsync(
                new DefaultHttpContext(), "POST /synthetic-posting", fx.AdminUserId, operation, new { amount = 999 },
                _ => throw new InvalidOperationException("Must not execute a conflicting claim"));
            Assert.Equal(409, Assert.IsAssignableFrom<IStatusCodeHttpResult>(changed).StatusCode);
        }
        finally { release.TrySetResult(); }
        Assert.Equal(200, Assert.IsAssignableFrom<IStatusCodeHttpResult>(await first).StatusCode);
        var context = new DefaultHttpContext();
        var replay = await new CafeMutationIdempotencyGuard(fx.ConnectionFactory).ExecuteSyncAsync(
            context, "POST /synthetic-posting", fx.AdminUserId, operation, new { amount = 100 },
            _ => throw new InvalidOperationException("Committed outcome must replay"));
        Assert.Equal(200, Assert.IsAssignableFrom<IStatusCodeHttpResult>(replay).StatusCode);
        Assert.Equal("true", context.Response.Headers["X-Idempotency-Replayed"].ToString());
        Assert.Equal(1, executions);
    }

}

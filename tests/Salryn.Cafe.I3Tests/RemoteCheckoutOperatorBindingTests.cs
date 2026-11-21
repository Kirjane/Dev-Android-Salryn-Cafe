using Salryn.Api.Local.RemotePos;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data.Repositories;
using Xunit;

namespace Salryn.Cafe.I3Tests;

public sealed class RemoteCheckoutOperatorBindingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignOrLegacyCompletedRequest_DoesNotDiscloseOrReplay(bool legacy)
    {
        using I3TestDatabaseFixture fx = new();
        Guid terminal = Guid.NewGuid();
        string requestId = Guid.NewGuid().ToString("D");
        RemotePosCheckoutRequestStore store = new(fx.ConnectionFactory);
        EnsureOperatorColumn(fx);
        if (legacy)
        {
            using var connection = fx.ConnectionFactory.CreateConnection();
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO remote_pos_checkout_requests
                    (store_id, terminal_id, client_request_id, request_hash, state, receipt_no,
                     sale_id, total_cents, change_cents, message, created_at_utc, updated_at_utc)
                VALUES ($store, $terminal, $request, 'SYNTHETIC', 'Completed', 'SYNTHETIC-BOUNDARY',
                    $sale, 15000, 0, 'Synthetic retained legacy result', $now, $now);
                """;
            cmd.Parameters.AddWithValue("$store", fx.StoreId.ToString("D"));
            cmd.Parameters.AddWithValue("$terminal", terminal.ToString("D"));
            cmd.Parameters.AddWithValue("$request", requestId);
            cmd.Parameters.AddWithValue("$sale", Guid.NewGuid().ToString("D"));
            cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            Assert.Equal(1, cmd.ExecuteNonQuery());
        }
        else
        {
            var pending = await store.GetOrCreatePendingAsync(fx.StoreId, terminal, fx.AdminUserId, requestId,
                "SYNTHETIC", "SYNTHETIC-BOUNDARY");
            await store.MarkCompletedAsync(pending, Guid.NewGuid().ToString("D"), 15000, 0, "Synthetic result");
        }
        // Legacy rows have no safe original actor; a current terminal session cannot claim them.
        var session = Session(fx.StoreId, terminal, legacy ? fx.AdminUserId : Guid.NewGuid());
        RemotePosCheckoutService service = CreateService(fx);
        Assert.Null(await service.FindCheckoutResultAsync(session, requestId));
        var denied = await service.SubmitAsync(session,
            new RemotePosCheckoutSubmitRequest(requestId, [new("Cash", 15000)]));
        Assert.Null(denied.SaleId);
        Assert.Null(denied.ReceiptNumber);
        Assert.Null(denied.TotalCents);
        Assert.NotEqual(RemotePosCheckoutStatus.DuplicateRequest, denied.Status);
        Assert.Equal(0, fx.CountSales());
        Assert.Equal(RemotePosCheckoutRequestStore.Completed,
            (await store.FindAsync(fx.StoreId, terminal, requestId))!.State);
    }

    [Fact]
    public async Task OriginalOperator_ReplaysAfterServiceRestart_AndCannotRebindWinningRequest()
    {
        using I3TestDatabaseFixture fx = new();
        EnsureOperatorColumn(fx);
        Guid terminal = Guid.NewGuid();
        string requestId = Guid.NewGuid().ToString("D");
        RemotePosCheckoutRequestStore store = new(fx.ConnectionFactory);
        var pending = await store.GetOrCreatePendingAsync(fx.StoreId, terminal, fx.AdminUserId,
            requestId, "SYNTHETIC", "SYNTHETIC-BOUNDARY");
        Guid saleId = Guid.NewGuid();
        await store.MarkCompletedAsync(pending, saleId.ToString("D"), 15000, 0, "Synthetic result");
        Guid foreign = Guid.NewGuid();
        var raced = await new RemotePosCheckoutRequestStore(fx.ConnectionFactory).GetOrCreatePendingAsync(
            fx.StoreId, terminal, foreign, requestId, "OTHER", "OTHER-RECEIPT");
        Assert.Equal(fx.AdminUserId, raced.OperatorUserId);
        Assert.Equal("SYNTHETIC", raced.RequestHash);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.MarkCompletedAsync(
            raced with { OperatorUserId = foreign }, Guid.NewGuid().ToString("D"), 999, 0, "Foreign"));
        var session = Session(fx.StoreId, terminal, fx.AdminUserId);
        var result = await CreateService(fx).FindCheckoutResultAsync(session, requestId);
        Assert.NotNull(result);
        Assert.Equal(saleId.ToString("D"), result.SaleId);
        Assert.Equal(15000, result.TotalCents);
        var replay = await CreateService(fx).SubmitAsync(session,
            new RemotePosCheckoutSubmitRequest(requestId, [new("Cash", 15000)]));
        Assert.Equal(RemotePosCheckoutStatus.DuplicateRequest, replay.Status);
        Assert.Equal(saleId.ToString("D"), replay.SaleId);
        Assert.Equal(0, fx.CountSales());
    }

    // Verify the actual canonical migration; tests must not repair missing production schema.
    internal static void EnsureOperatorColumn(I3TestDatabaseFixture fx)
    {
        using var connection = fx.ConnectionFactory.CreateConnection();
        connection.Open();
        using var probe = connection.CreateCommand();
        probe.CommandText = "SELECT COUNT(*) FROM pragma_table_info('remote_pos_checkout_requests') WHERE name = 'operator_user_id';";
        Assert.Equal(1L, Convert.ToInt64(probe.ExecuteScalar()));
    }

    private static RemotePosCheckoutService CreateService(I3TestDatabaseFixture fx)
    {
        SqliteRepositoryFactory repositories = new(fx.ConnectionFactory);
        CashierSessionService sessions = new(repositories.CreateCashierSessionWriteRepository(),
            repositories.CreateCashierSessionReadRepository());
        return new(fx.Composition.ProductReadRepository,
            new PosCheckoutApplicationService(new PosCheckoutService(repositories.CreateSaleWriteRepository(), sessions),
                fx.Composition.ProductReadRepository), sessions, fx.Composition.SaleReadRepository,
            repositories.CreateAuditWriteRepository(), new(), new(fx.ConnectionFactory));
    }

    private static RemotePosOperatorSession Session(Guid store, Guid terminal, Guid user) => new()
    {
        SessionId = Guid.NewGuid(), TokenHash = [], TerminalId = terminal,
        HandshakeSessionId = Guid.NewGuid(), ObservedOrigin = "https://synthetic.invalid",
        StoreId = store, HostId = Guid.NewGuid(), UserId = user, Username = "synthetic",
        DisplayName = "Synthetic", RoleName = "Cashier", RoleId = Guid.NewGuid(), Permissions = [],
        IssuedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(1)
    };
}


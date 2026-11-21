using System.Net;
using System.Text;
using System.Text.Json;
using Salryn.Mobile.Foundation.P0;
using Salryn.Mobile.Foundation.Sync;
using Salryn.Sync.Contracts;
using Xunit;

namespace Salryn.Cafe.I6BTests;

/// <summary>
/// Transport receipt validation only. A central HTTP receipt is not evidence that
/// the business projection or cashier ledger was applied.
/// </summary>
public sealed class MobilePushReceiptBoundaryTests
{
    [Fact]
    public async Task Push_RejectsForeignReceiptDespiteHttpSuccess()
    {
        Guid device = Guid.NewGuid(), tenant = Guid.NewGuid(), store = Guid.NewGuid();
        SyncOperationEnvelopeV1 submitted = Envelope(device, store, Guid.NewGuid(), 1);
        using HttpClient http = Client(Reply(Guid.NewGuid()));
        HttpSyncOutboxTransport transport = Transport(http, device, tenant, store);

        MobilePushOutcome outcome = await transport.PushBatchAsync([submitted]);

        Assert.False(outcome.IsSuccess);
        Assert.Equal("ERR_SYNC_ACK_MISMATCH", outcome.ErrorCode);
        Assert.Empty(outcome.AppliedOperationIds);
        Assert.Empty(outcome.Resolutions);
    }

    [Fact]
    public async Task Push_RejectsMissingReceiptDespiteHttpSuccess()
    {
        Guid device = Guid.NewGuid(), tenant = Guid.NewGuid(), store = Guid.NewGuid();
        SyncOperationEnvelopeV1 submitted = Envelope(device, store, Guid.NewGuid(), 1);
        using HttpClient http = Client(Reply());
        MobilePushOutcome outcome = await Transport(http, device, tenant, store).PushBatchAsync([submitted]);

        Assert.False(outcome.IsSuccess);
        Assert.Equal("ERR_SYNC_ACK_MISMATCH", outcome.ErrorCode);
    }

    [Fact]
    public async Task Push_RejectsDuplicateReceiptForDistinctOperations()
    {
        Guid device = Guid.NewGuid(), tenant = Guid.NewGuid(), store = Guid.NewGuid();
        SyncOperationEnvelopeV1 first = Envelope(device, store, Guid.NewGuid(), 1);
        SyncOperationEnvelopeV1 second = Envelope(device, store, Guid.NewGuid(), 2);
        using HttpClient http = Client(Reply(first.OperationId, first.OperationId));
        MobilePushOutcome outcome = await Transport(http, device, tenant, store).PushBatchAsync([first, second]);

        Assert.False(outcome.IsSuccess);
        Assert.Equal("ERR_SYNC_ACK_MISMATCH", outcome.ErrorCode);
        Assert.Empty(outcome.AppliedOperationIds);
        Assert.Empty(outcome.Resolutions);
    }

    [Fact]
    public async Task Push_RequiresBusinessResolutionEvenForExactReceiptSet()
    {
        Guid device = Guid.NewGuid(), tenant = Guid.NewGuid(), store = Guid.NewGuid();
        SyncOperationEnvelopeV1 first = Envelope(device, store, Guid.NewGuid(), 1);
        SyncOperationEnvelopeV1 second = Envelope(device, store, Guid.NewGuid(), 2);
        using HttpClient http = Client(Reply(second.OperationId, first.OperationId));
        MobilePushOutcome outcome = await Transport(http, device, tenant, store).PushBatchAsync([first, second]);

        Assert.False(outcome.IsSuccess);
        Assert.Equal("ERR_SYNC_NOT_APPLIED", outcome.ErrorCode);
        Assert.Empty(outcome.AppliedOperationIds);
        Assert.Empty(outcome.Resolutions);
        // No canonical sales or projections are asserted by this HTTP-only fixture.
    }

    [Fact]
    public async Task LegacyP0Send_DoesNotAcknowledgeUnrelatedReceipt()
    {
        Guid device = Guid.NewGuid(), tenant = Guid.NewGuid(), store = Guid.NewGuid();
        using HttpClient http = Client(Reply(Guid.NewGuid()));
        HttpSyncOutboxTransport transport = Transport(http, device, tenant, store);
        P0OutboxItem item = new(Guid.NewGuid(), Guid.NewGuid(), P0LocalStore.Pending, 0, null, null);

        transport = new(http, () => "synthetic-token", () => (device, tenant, store, 1L),
            id => SyncProtocolV1.CreateEnvelope(device, store, device, 1, "mobile.p0.operation.v1", 1,
                item.BusinessId, null, DateTimeOffset.UtcNow, "{}", id));
        P0TransportResult result = await transport.SendAsync(item);

        Assert.False(result.Acknowledged);
        Assert.Contains("ERR_SYNC_ACK_MISMATCH", result.Message);
    }

    [Theory]
    [InlineData("NeedsReview")]
    [InlineData("Rejected")]
    [InlineData("Received")]
    [InlineData("applied")]
    public async Task Push_NonAppliedResolutionRetainsPendingEvidenceAfterRestart(string status)
    {
        string path = Path.Combine(Path.GetTempPath(), $"salryn-ack-{Guid.NewGuid():N}.db");
        P0LocalStore local = new(path);
        await local.InitializeAsync();
        await local.WriteProofOperationAsync(Guid.NewGuid(), Salryn.Core.ValueObjects.Money.FromCents(100), false);
        P0OutboxItem item = Assert.Single(await local.GetPendingAsync());
        Guid device = Guid.NewGuid(), tenant = Guid.NewGuid(), store = Guid.NewGuid();
        using HttpClient http = Client(ResolutionReply((item.OperationId, status)));
        var transport = new HttpSyncOutboxTransport(http, () => "synthetic-token", () => (device, tenant, store, 1L),
            id => SyncProtocolV1.CreateEnvelope(device, store, device, 1, "mobile.p0.operation.v1", 1,
                item.BusinessId, null, DateTimeOffset.UtcNow, "{}", id));
        P0DispatchSummary result = await new P0OutboxDispatcher(local, transport).DispatchPendingAsync();
        Assert.Equal(0, result.Acknowledged);
        P0LocalStore restarted = new(path);
        P0OutboxItem retained = Assert.Single(await restarted.GetPendingAsync());
        Assert.Equal(item.OperationId, retained.OperationId);
        Assert.Null(retained.AcknowledgedAtUtc);
        Assert.Equal(new P0StoreCounts(1, 1, 1), await restarted.ReadCountsAsync());
    }

    [Fact]
    public async Task Push_MixedResolutionsOnlyExposeBusinessAppliedIds()
    {
        Guid device = Guid.NewGuid(), tenant = Guid.NewGuid(), store = Guid.NewGuid();
        var first = Envelope(device, store, Guid.NewGuid(), 1);
        var second = Envelope(device, store, Guid.NewGuid(), 2);
        using HttpClient http = Client(ResolutionReply((first.OperationId, "Applied"), (second.OperationId, "NeedsReview")));
        var outcome = await Transport(http, device, tenant, store).PushBatchAsync([first, second]);
        Assert.False(outcome.IsSuccess);
        Assert.Equal(first.OperationId, Assert.Single(outcome.AppliedOperationIds));
    }

    [Fact]
    public async Task Push_ExplicitAppliedResolutionAllowsAcknowledgment()
    {
        Guid device = Guid.NewGuid(), tenant = Guid.NewGuid(), store = Guid.NewGuid();
        var envelope = Envelope(device, store, Guid.NewGuid(), 1);
        using HttpClient http = Client(ResolutionReply((envelope.OperationId, "Applied")));
        var outcome = await Transport(http, device, tenant, store).PushBatchAsync([envelope]);
        Assert.True(outcome.IsSuccess);
        Assert.Equal(envelope.OperationId, Assert.Single(outcome.AppliedOperationIds));
        Assert.Equal(envelope.OperationId, Assert.Single(outcome.Resolutions).OperationId);
    }

    private static string ResolutionReply(params (Guid Id, string Status)[] items) =>
        JsonSerializer.Serialize(new Salryn.Sync.Central.Server.PushOperationsResponse(
            Guid.NewGuid(), 1, 3,
            items.Select(x => new Salryn.Sync.Central.Server.PushOperationResultDto(
                x.Id, false,
                new SyncOperationFinalResolutionV1(x.Id, 1, DateTimeOffset.UtcNow, x.Status,
                    "DOMAIN_PROCESSOR_UNAVAILABLE", "{}", DateTimeOffset.UtcNow), null)).ToArray()));

    [Fact]
    public async Task LegacySend_WithoutPersistedEnvelopeFailsBeforeNetwork()
    {
        Guid device = Guid.NewGuid(), tenant = Guid.NewGuid(), store = Guid.NewGuid();
        using HttpClient http = Client("not-a-receipt");
        var item = new P0OutboxItem(Guid.NewGuid(), Guid.NewGuid(), P0LocalStore.Pending, 0, null, null);
        var outcome = await Transport(http, device, tenant, store).SendAsync(item);
        Assert.False(outcome.Acknowledged);
        Assert.Contains("ERR_SYNC_DURABLE_ENVELOPE_REQUIRED", outcome.Message);
    }

    private static SyncOperationEnvelopeV1 Envelope(Guid device, Guid store, Guid operation, long sequence) =>
        SyncProtocolV1.CreateEnvelope(
            device, store, device, sequence, "mobile.p0.operation.v1", 1,
            Guid.NewGuid(), null, DateTimeOffset.UtcNow, "{}", operation);

    private static string Reply(params Guid[] operationIds) =>
        JsonSerializer.Serialize(new
        {
            centralInstanceId = Guid.NewGuid(),
            serverEpoch = 1L,
            headSequence = 3L,
            results = operationIds.Select(id => new { operationId = id, isDuplicateReplay = false }).ToArray()
        });

    private static HttpClient Client(string json) =>
        new(new FixedReplyHandler(json)) { BaseAddress = new Uri("https://central.salryn-cafe.test") };

    private static HttpSyncOutboxTransport Transport(HttpClient http, Guid device, Guid tenant, Guid store) =>
        new(http, () => "synthetic-token", () => (device, tenant, store, 1L));

    private sealed class FixedReplyHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/sync/v1/push", request.RequestUri?.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}

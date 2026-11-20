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
    }

    [Fact]
    public async Task Push_AcceptsExactReceiptSetWithoutClaimingBusinessProjection()
    {
        Guid device = Guid.NewGuid(), tenant = Guid.NewGuid(), store = Guid.NewGuid();
        SyncOperationEnvelopeV1 first = Envelope(device, store, Guid.NewGuid(), 1);
        SyncOperationEnvelopeV1 second = Envelope(device, store, Guid.NewGuid(), 2);
        using HttpClient http = Client(Reply(second.OperationId, first.OperationId));
        MobilePushOutcome outcome = await Transport(http, device, tenant, store).PushBatchAsync([first, second]);

        Assert.True(outcome.IsSuccess);
        Assert.Null(outcome.ErrorCode);
        Assert.Equal(2, outcome.AppliedOperationIds.Count);
        Assert.Contains(first.OperationId, outcome.AppliedOperationIds);
        Assert.Contains(second.OperationId, outcome.AppliedOperationIds);
        // No canonical sales or projections are asserted by this HTTP-only fixture.
    }

    [Fact]
    public async Task LegacyP0Send_DoesNotAcknowledgeUnrelatedReceipt()
    {
        Guid device = Guid.NewGuid(), tenant = Guid.NewGuid(), store = Guid.NewGuid();
        using HttpClient http = Client(Reply(Guid.NewGuid()));
        HttpSyncOutboxTransport transport = Transport(http, device, tenant, store);
        P0OutboxItem item = new(Guid.NewGuid(), Guid.NewGuid(), P0LocalStore.Pending, 0, null, null);

        P0TransportResult result = await transport.SendAsync(item);

        Assert.False(result.Acknowledged);
        Assert.Contains("ERR_SYNC_ACK_MISMATCH", result.Message);
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

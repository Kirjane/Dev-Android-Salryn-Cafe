using System.Net;
using System.Text;
using System.Text.Json;
using Salryn.Mobile.Foundation.P0;
using Salryn.Mobile.Foundation.Sync;
using Salryn.Mobile.Runtime;
using Salryn.Infrastructure.Data.Sync;
using Salryn.Sync.Contracts;
using Xunit;

namespace Salryn.Cafe.I6BTests;

public sealed class MobileDurableEnvelopeTests
{
    [Fact]
    public async Task CanonicalSequenceAndEntireEnvelopeSurviveRetryAndRestart()
    {
        string root = Path.Combine(Path.GetTempPath(), "cafe-envelope-" + Guid.NewGuid().ToString("N"));
        var composition = MobileProductionComposition.Create(root);
        await composition.InitializeDatabaseAsync();
        Guid device = Guid.NewGuid(), store = Guid.NewGuid(), actor = Guid.NewGuid(), tenant = Guid.NewGuid();
        SyncOperationEnvelopeV1 Enqueue() => composition.SyncOutbox.Enqueue(new SyncEnqueueRequest(
            device, store, actor, "mobile.p0.operation.v1", 1, Guid.NewGuid(), null,
            DateTimeOffset.UtcNow, "{}", Guid.NewGuid()));
        var first = Enqueue();
        var second = Enqueue();
        Assert.True(second.LocalSequence > first.LocalSequence);
        var handler = new CaptureHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://central.salryn-cafe.test") };
        var transport = new HttpSyncOutboxTransport(http, () => "test", () => (device, tenant, store, 1L),
            id => composition.SyncOutbox.Find(id)?.Envelope);
        var item = new P0OutboxItem(first.OperationId, first.AggregateId, P0LocalStore.Pending, 0, null, null);
        await transport.SendAsync(item);
        composition.SyncOutbox.RecordAttemptFailure(first.OperationId, "lost response");
        var restarted = MobileProductionComposition.Create(root);
        var nextTransport = new HttpSyncOutboxTransport(http, () => "test", () => (device, tenant, store, 1L),
            id => restarted.SyncOutbox.Find(id)?.Envelope);
        await nextTransport.SendAsync(item with { AttemptCount = 7 });
        await nextTransport.SendAsync(item with { OperationId = second.OperationId, BusinessId = second.AggregateId });
        Assert.Equal(3, handler.Envelopes.Count);
        Assert.Equal(first, handler.Envelopes[0]);
        Assert.Equal(first, handler.Envelopes[1]);
        Assert.Equal(second, handler.Envelopes[2]);
        Assert.Equal(actor, handler.Envelopes[0].ActorUserId);
        Assert.Equal(2, restarted.SyncOutbox.ReadPending(device).Count);
        Assert.Equal(second.LocalSequence + 1, restarted.SyncOutbox.ReadNextLocalSequence(device));
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public List<SyncOperationEnvelopeV1> Envelopes { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var envelope = json.RootElement.GetProperty("operations")[0].Deserialize<SyncOperationEnvelopeV1>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Envelopes.Add(envelope);
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        }
    }
}

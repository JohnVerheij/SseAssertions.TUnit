using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SseAssertions;
using TUnit.Core;

namespace Smoke.Consumer;

/// <summary>
/// External-consumer smoke test that verifies the just-packed SseAssertions.TUnit NuGet
/// package can be consumed from a deliberately-different namespace (<c>Smoke.Consumer</c>)
/// without leaking into SseAssertions.TUnit's internals. Compiles + runs against the local-feed
/// version pinned in <c>NuGet.config</c>, never the in-repo ProjectReference. This is the last
/// CI step before release and the canary that proves the packed nupkg is a usable consumer
/// artifact.
/// </summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class SmokeTest
{
    [Test]
    public async Task ConsumesPublicTypesFromSseAssertions(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var evt = new SseEvent(EventName: "message", Id: null, RetryMillis: null, Data: "ok");

        await Assert.That(evt.EventName).IsEqualTo("message");
        await Assert.That(evt.Data).IsEqualTo("ok");
    }

    [Test]
    public async Task ConsumesIsServerSentEventsStreamFromAdapter(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string body = "event: ping\ndata: 1\n\n";

        await Assert.That(body).IsServerSentEventsStream();
    }

    /// <summary>Pins the 0.8.0 live-stream surface for an external consumer: <c>SseStream.OpenAsync</c> returns
    /// after the first frame, and the streaming assertions (<c>HasSseEvent</c> with <c>WithId</c> and
    /// <c>Until</c>, <c>HasSseEventIds</c>) read it event by event. The Native AOT gate compiles and runs this
    /// ahead of time.</summary>
    [Test]
    public async Task ConsumesSseStreamAndStreamingAssertions(CancellationToken cancellationToken)
    {
        const string Body = "retry: 3000\n\nid: 1\nevent: tick\ndata: a\n\nid: 2\nevent: done\ndata: b\n\nid: 3\ndata: c\n\n";
        using var handler = new FixedSseHandler(Body);
        using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = new UriBuilder(Uri.UriSchemeHttp, "localhost").Uri };

        await using var sse = await SseStream.OpenAsync(client, new Uri("/events", UriKind.Relative), cancellationToken);

        await Assert.That(sse.FirstFrame.RetryMillis).IsEqualTo(3000);
        await Assert.That(sse).HasSseEvent("tick", cancellationToken).WithId(static id => id is not null).Exactly(1).Until("done");
        await Assert.That(sse).HasSseEventIds(cancellationToken).Sequential(startingAt: 3);
    }

    /// <summary>Answers every request with the same Server-Sent Events body.</summary>
    private sealed class FixedSseHandler(string body) : HttpMessageHandler
    {
        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body)));
            content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content });
        }
    }
}

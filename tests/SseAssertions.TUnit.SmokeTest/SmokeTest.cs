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
}

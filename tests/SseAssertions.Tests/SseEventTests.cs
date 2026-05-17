using System.Threading;
using System.Threading.Tasks;
using SseAssertions;

namespace SseAssertions.Tests;

/// <summary>
/// Framework-agnostic tests for the <see cref="SseEvent"/> public record. The record is the
/// stable public data type of the assertion family; these tests pin its construction and
/// value-equality semantics without depending on the TUnit adapter.
/// </summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class SseEventTests
{
    [Test]
    public async Task SseEvent_AllFieldsSet_ConstructsAndExposesValues(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var evt = new SseEvent(EventName: "tick", Data: "1", Id: "42", RetryMillis: 1500);

        await Assert.That(evt.EventName).IsEqualTo("tick");
        await Assert.That(evt.Data).IsEqualTo("1");
        await Assert.That(evt.Id).IsEqualTo("42");
        await Assert.That(evt.RetryMillis).IsEqualTo(1500);
    }

    [Test]
    public async Task SseEvent_OmittedOptionalParameters_DefaultToNull(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var evt = new SseEvent(EventName: "message", Data: "payload");

        await Assert.That(evt.Id).IsNull();
        await Assert.That(evt.RetryMillis).IsNull();
    }

    [Test]
    public async Task SseEvent_DefaultEventName_IsMessage_PerWhatwgSpec(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var parsed = SseFrameParser.Parse("data: hello\n\n");

        await Assert.That(parsed.Count).IsEqualTo(1);
        await Assert.That(parsed[0].EventName).IsEqualTo("message");
    }

    [Test]
    public async Task SseEvent_ValueEquality_TwoEventsWithSameFieldsAreEqual(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var a = new SseEvent("update", "payload", "1", 500);
        var b = new SseEvent("update", "payload", "1", 500);

        await Assert.That(a).IsEqualTo(b);
    }

    [Test]
    public async Task SseEvent_ValueEquality_DifferingDataMakesEventsUnequal(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var a = new SseEvent("tick", "1");
        var b = new SseEvent("tick", "2");

        await Assert.That(a).IsNotEqualTo(b);
    }

    [Test]
    public async Task SseEvent_DataFieldNeverNull_EmptyStringForFramesWithEmptyData(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var evt = new SseEvent(EventName: "ping", Data: string.Empty);

        await Assert.That(evt.Data).IsEqualTo(string.Empty);
    }
}

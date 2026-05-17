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
        var evt = new SseEvent(EventName: "message", Id: "42", RetryMillis: 1500, Data: "hello");

        await Assert.That(evt.EventName).IsEqualTo("message");
        await Assert.That(evt.Id).IsEqualTo("42");
        await Assert.That(evt.RetryMillis).IsEqualTo(1500);
        await Assert.That(evt.Data).IsEqualTo("hello");
    }

    [Test]
    public async Task SseEvent_OptionalFieldsAreNullable_AcceptsNullEventNameIdAndRetry(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var evt = new SseEvent(EventName: null, Id: null, RetryMillis: null, Data: string.Empty);

        await Assert.That(evt.EventName).IsNull();
        await Assert.That(evt.Id).IsNull();
        await Assert.That(evt.RetryMillis).IsNull();
        await Assert.That(evt.Data).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SseEvent_ValueEquality_TwoEventsWithSameFieldsAreEqual(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var a = new SseEvent("update", "1", 500, "payload");
        var b = new SseEvent("update", "1", 500, "payload");

        await Assert.That(a).IsEqualTo(b);
    }
}

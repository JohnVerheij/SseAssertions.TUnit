using System.Threading;
using System.Threading.Tasks;
using PublicApiGenerator;
using SnapshotAssertions.TUnit;

namespace SseAssertions.TUnit.SnapshotTests;

/// <summary>
/// Pins the public API surface of both shipped assemblies (<c>SseAssertions</c> and
/// <c>SseAssertions.TUnit</c>) using <c>SnapshotAssertions.TUnit</c>'s <c>MatchesSnapshot()</c>
/// chain. Any change to a public type, member, signature, attribute, or visibility produces a
/// diff against the <c>.expected.txt</c> baseline under <c>Snapshots/</c> and fails the test
/// until the snapshot is explicitly re-accepted (write the new content to the expected path,
/// or run with <c>SNAPSHOT_ACCEPT=1</c> to auto-write).
/// </summary>
/// <remarks>
/// Stronger than ApiCompat's per-version baseline check because this snapshot fires on every
/// PR, not just at pack time.
/// </remarks>
[Category("Smoke")]
[Timeout(10_000)]
internal sealed class PublicApiTests
{
    [Test]
    public async Task SseAssertionsPublicApiHasNotChangedAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var assembly = typeof(global::SseAssertions.SseEvent).Assembly;
        // Normalize line endings so the snapshot baseline survives both Linux CI (LF native)
        // and Windows local dev (CRLF native).
        var publicApi = assembly.GeneratePublicApi().ReplaceLineEndings("\n");

        await Assert.That(publicApi).MatchesSnapshot();
    }

    [Test]
    public async Task SseAssertionsTUnitPublicApiHasNotChangedAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var assembly = typeof(global::SseAssertions.TUnit.SseFormatAssertions).Assembly;
        var publicApi = assembly.GeneratePublicApi().ReplaceLineEndings("\n");

        await Assert.That(publicApi).MatchesSnapshot();
    }
}

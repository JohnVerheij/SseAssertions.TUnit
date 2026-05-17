using System.Threading;
using System.Threading.Tasks;
using SseAssertions;

namespace SseAssertions.Tests;

/// <summary>
/// Pins the <see cref="SseCountComparison"/> enum's values and their member-name contract.
/// The enum is part of the public extension surface; consumer-authored typed SSE assertions
/// pass these values to <see cref="SseFailureMessage.EventCountMismatch(string, int, int, SseCountComparison)"/>,
/// so the underlying integer values and member names are observable.
/// </summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class SseCountComparisonTests
{
    [Test]
    public async Task SseCountComparison_HasThreeValues_InDeclarationOrder(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var atLeast = (int)SseCountComparison.AtLeast;
        var atMost = (int)SseCountComparison.AtMost;
        var exactly = (int)SseCountComparison.Exactly;

        await Assert.That(atLeast).IsEqualTo(0);
        await Assert.That(atMost).IsEqualTo(1);
        await Assert.That(exactly).IsEqualTo(2);
    }
}

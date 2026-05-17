namespace SseAssertions;

/// <summary>
/// Identifies which count-comparison terminator the SSE assertion chain applied. Carried by
/// <see cref="SseFailureMessage.EventCountMismatch(string, int, int, SseCountComparison)"/> so
/// failure messages can render the comparison label consistently across the family.
/// </summary>
/// <remarks>
/// The enum is the typed equivalent of the chain methods <c>AtLeast</c>, <c>AtMost</c>, and
/// <c>Exactly</c> on <c>SseAssertions.TUnit.SseAssertion</c>; consumer-authored typed assertions
/// that compose <see cref="SseFailureMessage"/> use it to produce failure messages with the
/// same shape as the shipped chain terminators.
/// </remarks>
public enum SseCountComparison
{
    /// <summary>At least the expected count — matches the chain terminator <c>AtLeast(n)</c>.</summary>
    AtLeast,

    /// <summary>At most the expected count — matches the chain terminator <c>AtMost(n)</c>.</summary>
    AtMost,

    /// <summary>Exactly the expected count — matches the chain terminator <c>Exactly(n)</c>.</summary>
    Exactly,
}

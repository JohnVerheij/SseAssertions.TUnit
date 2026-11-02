namespace SseAssertions.TUnit;

/// <summary>A decided outcome of an <see cref="ISseObserver"/>: a pass, or a failure with its message.</summary>
/// <param name="Failure">The failure message, or <see langword="null"/> for a pass.</param>
internal readonly record struct SseOutcome(string? Failure)
{
    /// <summary>Gets the passing outcome.</summary>
    public static SseOutcome Pass => default;
}

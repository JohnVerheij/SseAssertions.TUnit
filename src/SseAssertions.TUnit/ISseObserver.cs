using SseAssertions;

namespace SseAssertions.TUnit;

/// <summary>
/// An expectation evaluated one event at a time, so a streaming read can stop as soon as the outcome is decided.
/// Implemented by the <c>HasSseEvent</c> and <c>HasSseEventIds</c> matchers.
/// </summary>
internal interface ISseObserver
{
    /// <summary>Gets the event-type name that ends the observation window, or <see langword="null"/> when the
    /// window runs to the end of the stream.</summary>
    string? Until { get; }

    /// <summary>Observes the next event.</summary>
    /// <param name="evt">The event.</param>
    /// <returns>The outcome once it is decided, whatever events follow; <see langword="null"/> while it is
    /// not.</returns>
    SseOutcome? Observe(SseEvent evt);

    /// <summary>Evaluates the expectation against every event observed, once the window has ended.</summary>
    /// <returns><see langword="null"/> when the expectation is met; otherwise the failure message.</returns>
    string? Finish();
}

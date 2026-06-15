using SseAssertions;
using TUnit.Assertions.Core;

namespace SseAssertions.TUnit;

/// <summary>
/// Turns a drained SSE body into an <see cref="AssertionResult"/> for the streaming
/// <c>HasSseEvent</c> chains (<see cref="System.IO.Stream"/> and
/// <see cref="System.Net.Http.HttpResponseMessage"/>). Runs the shared <see cref="SseEventMatcher"/>
/// and, when the expectation is unmet and the read was cancellation-truncated, prefers the
/// cancellation-cut diagnostic over the count/narrower failure.
/// </summary>
internal static class SseStreamEvaluation
{
    /// <summary>Evaluates the matcher against the parsed body, applying cancellation-cut
    /// precedence.</summary>
    /// <param name="matcher">The configured matcher.</param>
    /// <param name="body">The drained SSE body.</param>
    /// <param name="bytesReceived">The number of bytes read (for the cancellation diagnostic).</param>
    /// <param name="cancelled">Whether the read was cut short by cancellation.</param>
    /// <returns>The assertion result.</returns>
    public static AssertionResult Evaluate(SseEventMatcher matcher, string body, int bytesReceived, bool cancelled)
    {
        var events = SseFrameParser.Parse(body);
        var failure = matcher.Evaluate(events);
        if (failure is null)
        {
            return AssertionResult.Passed;
        }

        return cancelled
            ? AssertionResult.Failed(SseFailureMessage.CancellationCutRead(bytesReceived, events.Count, body))
            : AssertionResult.Failed(failure);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SseAssertions;
using TUnit.Assertions.Core;

namespace SseAssertions.TUnit;

/// <summary>
/// Runs an <see cref="ISseObserver"/> over a body: incrementally over a <see cref="Stream"/> (the
/// <see cref="Stream"/> and <see cref="System.Net.Http.HttpResponseMessage"/> chains) or over parsed events (the
/// <see cref="string"/> chains). Every receiver stops at the same point, so a body gives the same verdict on each.
/// </summary>
/// <remarks>
/// The read stops once the observer has decided, or after the event that ends the observation window
/// (<c>Until</c>), or at the end of the stream; the cancellation token is the upper bound. When the token ends
/// the read and the expectation is unmet, the cancellation-cut diagnostic takes precedence; when the expectation
/// holds but the window's closing event never arrived, the assertion fails naming that event.
/// </remarks>
internal static class SseStreamEvaluation
{
    /// <summary>Reads events from <paramref name="stream"/> until the outcome is decided.</summary>
    /// <param name="observer">The expectation.</param>
    /// <param name="stream">The body. An <see cref="SseStream"/> is read from its current position, and a later
    /// assertion continues after the last event this one read.</param>
    /// <param name="cancellationToken">The upper bound of the read. Its cancellation ends the read; a
    /// cancellation by another token propagates.</param>
    /// <returns>The assertion result.</returns>
    public static async Task<AssertionResult> EvaluateAsync(ISseObserver observer, Stream stream, CancellationToken cancellationToken)
    {
        var reader = new SseEventReader(stream);
        var eventsRead = 0;
        var windowEnded = false;
        try
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false) is { } evt)
            {
                eventsRead++;
                if (observer.Observe(evt) is { } outcome)
                {
                    return ToResult(outcome.Failure);
                }

                if (EndsWindow(observer, evt))
                {
                    windowEnded = true;
                    break;
                }
            }

            windowEnded = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The token is the upper bound of the read: evaluate what arrived before it.
        }

        var failure = observer.Finish();
        if (windowEnded)
        {
            return ToResult(failure);
        }

        if (failure is not null)
        {
            return AssertionResult.Failed(SseFailureMessage.CancellationCutRead(
                int.CreateSaturating(reader.BytesRead), eventsRead, reader.TextExcerpt));
        }

        return observer.Until is { } until
            ? AssertionResult.Failed(SseFailureMessage.UntilEventNotReached(until, eventsRead))
            : AssertionResult.Passed;
    }

    /// <summary>Runs the observer over parsed events, stopping where a streaming read would.</summary>
    /// <param name="observer">The expectation.</param>
    /// <param name="events">The parsed events of a whole body.</param>
    /// <returns><see langword="null"/> when the expectation is met; otherwise the failure message.</returns>
    public static string? EvaluateParsed(ISseObserver observer, IReadOnlyList<SseEvent> events)
    {
        foreach (var evt in events)
        {
            if (observer.Observe(evt) is { } outcome)
            {
                return outcome.Failure;
            }

            if (EndsWindow(observer, evt))
            {
                break;
            }
        }

        return observer.Finish();
    }

    /// <summary>Gets whether <paramref name="evt"/> ends the observation window.</summary>
    private static bool EndsWindow(ISseObserver observer, SseEvent evt)
        => observer.Until is { } until && string.Equals(evt.EventName, until, StringComparison.Ordinal);

    private static AssertionResult ToResult(string? failure)
        => failure is null ? AssertionResult.Passed : AssertionResult.Failed(failure);
}

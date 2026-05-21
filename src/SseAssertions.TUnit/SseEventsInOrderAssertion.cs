using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using SseAssertions;
using TUnit.Assertions.Attributes;
using TUnit.Assertions.Core;

namespace SseAssertions.TUnit;

/// <summary>
/// Fluent TUnit assertion that verifies a Server-Sent Events stream contains a named sequence of
/// events in order. Constructed by the TUnit source generator from the
/// <c>HasSseEventsInOrder(params string[])</c> extension on <see cref="string"/>; the
/// <see cref="WithStrictOrdering"/> chain method tightens the assertion to require the named
/// events to appear contiguously.
/// </summary>
/// <remarks>
/// Default (non-strict) mode: each named event must appear in the given order; other events may
/// appear between them. Strict mode: the named events must appear contiguously with no other
/// events between them. An empty <c>eventNames</c> array trivially passes; callers should pass
/// at least one name for the assertion to be meaningful.
/// </remarks>
[AssertionExtension("HasSseEventsInOrder")]
public sealed class SseEventsInOrderAssertion : Assertion<string>
{
    private readonly string[] _expectedEventNames;
    private bool _strict;

    /// <summary>Initialises the assertion. Called by the TUnit source generator.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    /// <param name="eventNames">The event-type names expected in order. Matched case-sensitively
    /// against <see cref="SseEvent.EventName"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="eventNames"/> is
    /// <see langword="null"/>.</exception>
    [SuppressMessage(
        "Performance",
        "MA0109:Consider adding an overload with a Span<T> or Memory<T>",
        Justification = "Public assertion constructor consumed by TUnit's source-generated chain extension. Allocation per assertion-build is negligible.")]
    public SseEventsInOrderAssertion(AssertionContext<string> context, string[] eventNames) : base(context)
    {
        ArgumentNullException.ThrowIfNull(eventNames);
        _expectedEventNames = eventNames;
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".HasSseEventsInOrder({FormatNameList(eventNames)})");
    }

    /// <summary>Tightens the assertion to require the named events to appear contiguously, with
    /// no other events between them.</summary>
    /// <returns>This assertion for chaining.</returns>
    public SseEventsInOrderAssertion WithStrictOrdering()
    {
        _strict = true;
        Context.ExpressionBuilder.Append(".WithStrictOrdering()");
        return this;
    }

    /// <inheritdoc/>
    protected override Task<AssertionResult> CheckAsync(EvaluationMetadata<string> metadata)
    {
        if (metadata.Exception is not null)
        {
            return Task.FromResult(AssertionResult.Failed(
                $"threw {metadata.Exception.GetType().Name}: {metadata.Exception.Message}",
                metadata.Exception));
        }

        var body = metadata.Value;
        if (body is null)
        {
            return Task.FromResult(AssertionResult.Failed("the receiver was null"));
        }

        var events = SseFrameParser.Parse(body);
        return Task.FromResult(Evaluate(events, _expectedEventNames, _strict));
    }

    /// <inheritdoc/>
    protected override string GetExpectation()
    {
        return string.Concat(
            "to contain events [",
            FormatNameList(_expectedEventNames),
            _strict ? "] contiguously" : "] in order");
    }

    internal static AssertionResult Evaluate(IReadOnlyList<SseEvent> events, IReadOnlyList<string> expectedNames, bool strict)
    {
        if (expectedNames.Count is 0)
        {
            // Trivial pass: no constraints to violate.
            return AssertionResult.Passed;
        }

        return strict
            ? EvaluateStrict(events, expectedNames)
            : EvaluateNonStrict(events, expectedNames);
    }

    private static AssertionResult EvaluateNonStrict(IReadOnlyList<SseEvent> events, IReadOnlyList<string> expectedNames)
    {
        var searchStart = 0;
        for (var k = 0; k < expectedNames.Count; k++)
        {
            var name = expectedNames[k];
            var foundAt = FindEvent(events, name, searchStart);
            if (foundAt >= 0)
            {
                searchStart = foundAt + 1;
                continue;
            }

            // Not found from searchStart onward. Was it observed earlier (= out of order) or never?
            var earlierIdx = FindEvent(events, name, 0);
            if (earlierIdx >= 0)
            {
                // Identify the predecessor that consumed our cursor.
                var predecessor = expectedNames[k - 1];
                var predIdx = searchStart - 1;
                return AssertionResult.Failed(string.Concat(
                    "expected events [",
                    FormatNameList(expectedNames),
                    "] in order, but \"",
                    name,
                    "\" (index ",
                    earlierIdx.ToString(CultureInfo.InvariantCulture),
                    ") appeared before \"",
                    predecessor,
                    "\" (index ",
                    predIdx.ToString(CultureInfo.InvariantCulture),
                    ")"));
            }

            return AssertionResult.Failed(string.Concat(
                "expected events [",
                FormatNameList(expectedNames),
                "] in order, but \"",
                name,
                "\" was not in the stream"));
        }

        return AssertionResult.Passed;
    }

    private static AssertionResult EvaluateStrict(IReadOnlyList<SseEvent> events, IReadOnlyList<string> expectedNames)
    {
        if (events.Count < expectedNames.Count)
        {
            return AssertionResult.Failed(string.Concat(
                "expected events [",
                FormatNameList(expectedNames),
                "] contiguously, but the stream had only ",
                events.Count.ToString(CultureInfo.InvariantCulture),
                " event(s)"));
        }

        for (var start = 0; start <= events.Count - expectedNames.Count; start++)
        {
            if (MatchesAt(events, start, expectedNames))
            {
                return AssertionResult.Passed;
            }
        }

        return BuildStrictNearMissFailure(events, expectedNames);
    }

    private static AssertionResult BuildStrictNearMissFailure(IReadOnlyList<SseEvent> events, IReadOnlyList<string> expectedNames)
    {
        // Prefer a concrete mismatch (more informative) over a partial-prefix report. Walk every
        // position where the first expected name appears; return on the first contiguous mismatch,
        // otherwise track the longest partial-prefix match that ran off the end of the stream so the
        // fallback message points at the actual partial match (not a misleading "first expected name
        // not in stream") when the prefix matched but the stream ended early.
        var bestPartialStart = -1;
        var bestPartialLength = 0;
        for (var start = 0; start <= events.Count - 1; start++)
        {
            if (!string.Equals(events[start].EventName, expectedNames[0], StringComparison.Ordinal))
            {
                continue;
            }

            var matchedLength = 1;
            for (var k = 1; k < expectedNames.Count && start + k < events.Count; k++)
            {
                if (!string.Equals(events[start + k].EventName, expectedNames[k], StringComparison.Ordinal))
                {
                    return FailWithMismatch(expectedNames, events[start + k].EventName, start + k, expectedNames[k]);
                }

                matchedLength++;
            }

            if (matchedLength > bestPartialLength)
            {
                bestPartialStart = start;
                bestPartialLength = matchedLength;
            }
        }

        return bestPartialStart >= 0
            ? FailWithPartialPrefix(expectedNames, bestPartialStart, bestPartialLength)
            : FailWithFirstNameNotInStream(expectedNames);
    }

    private static AssertionResult FailWithMismatch(IReadOnlyList<string> expectedNames, string actualName, int actualIndex, string expectedName)
        => AssertionResult.Failed(string.Concat(
            "expected events [",
            FormatNameList(expectedNames),
            "] contiguously, but \"",
            actualName,
            "\" appeared at index ",
            actualIndex.ToString(CultureInfo.InvariantCulture),
            " instead of \"",
            expectedName,
            "\""));

    private static AssertionResult FailWithPartialPrefix(IReadOnlyList<string> expectedNames, int prefixStart, int prefixLength)
    {
        var matchedPrefix = new string[prefixLength];
        for (var i = 0; i < prefixLength; i++)
        {
            matchedPrefix[i] = expectedNames[i];
        }
        return AssertionResult.Failed(string.Concat(
            "expected events [",
            FormatNameList(expectedNames),
            "] contiguously, but the stream ended after matching prefix [",
            FormatNameList(matchedPrefix),
            "] starting at index ",
            prefixStart.ToString(CultureInfo.InvariantCulture)));
    }

    private static AssertionResult FailWithFirstNameNotInStream(IReadOnlyList<string> expectedNames)
        => AssertionResult.Failed(string.Concat(
            "expected events [",
            FormatNameList(expectedNames),
            "] contiguously, but \"",
            expectedNames[0],
            "\" was not in the stream"));

    private static bool MatchesAt(IReadOnlyList<SseEvent> events, int start, IReadOnlyList<string> expectedNames)
    {
        for (var k = 0; k < expectedNames.Count; k++)
        {
            if (!string.Equals(events[start + k].EventName, expectedNames[k], StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    private static int FindEvent(IReadOnlyList<SseEvent> events, string name, int fromIndex)
    {
        for (var i = fromIndex; i < events.Count; i++)
        {
            if (string.Equals(events[i].EventName, name, StringComparison.Ordinal))
            {
                return i;
            }
        }
        return -1;
    }

    private static string FormatNameList(IReadOnlyList<string> names)
    {
        if (names.Count is 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        for (var i = 0; i < names.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }
            sb.Append(names[i]);
        }
        return sb.ToString();
    }
}

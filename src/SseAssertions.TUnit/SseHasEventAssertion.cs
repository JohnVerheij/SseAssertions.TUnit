using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using SseAssertions;
using TUnit.Assertions.Attributes;
using TUnit.Assertions.Core;

namespace SseAssertions.TUnit;

/// <summary>
/// Fluent TUnit assertion that walks a Server-Sent Events wire-format string and verifies the
/// presence (or count) of frames matching a given event-type name. Constructed by the TUnit
/// source generator from the <c>HasSseEvent(string)</c> extension on <see cref="string"/>; chain
/// methods (<c>WithData</c>, <c>AtLeast</c>, <c>AtMost</c>, <c>Exactly</c>) narrow or terminate
/// the assertion.
/// </summary>
/// <remarks>
/// The chain runs against the receiver string parsed via <see cref="SseFrameParser.Parse(string)"/>
/// inside <see cref="CheckAsync"/>. The parsed list is short-lived (per-assertion); failure
/// messages compose factories from <see cref="SseFailureMessage"/>, so consumer-authored typed
/// SSE assertions produce diagnostics identical in shape to this chain.
/// </remarks>
[AssertionExtension("HasSseEvent")]
public sealed class SseHasEventAssertion : Assertion<string>
{
    private readonly string _eventName;
    private Func<string, bool>? _dataPredicate;
    private SseCountComparison _comparison = SseCountComparison.AtLeast;
    private int _expectedCount = 1;

    /// <summary>Initialises the assertion. Called by the TUnit source generator.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    /// <param name="eventName">The SSE event-type name to look for. Matched case-sensitively
    /// against <see cref="SseEvent.EventName"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="eventName"/> is
    /// <see langword="null"/>.</exception>
    public SseHasEventAssertion(AssertionContext<string> context, string eventName) : base(context)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        _eventName = eventName;
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".HasSseEvent({"\""}{eventName}{"\""})");
    }

    /// <summary>Narrows the assertion to frames whose <see cref="SseEvent.Data"/> satisfies the
    /// supplied predicate.</summary>
    /// <param name="predicate">The data predicate. Called with each candidate frame's
    /// <see cref="SseEvent.Data"/> value; frames for which the predicate returns
    /// <see langword="true"/> are counted.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is
    /// <see langword="null"/>.</exception>
    public SseHasEventAssertion WithData(Func<string, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _dataPredicate = predicate;
        Context.ExpressionBuilder.Append(".WithData(...)");
        return this;
    }

    /// <summary>Expects at least <paramref name="count"/> matching frames.</summary>
    /// <param name="count">The minimum match count. Must be non-negative.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is
    /// negative.</exception>
    public SseHasEventAssertion AtLeast(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _comparison = SseCountComparison.AtLeast;
        _expectedCount = count;
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".AtLeast({count})");
        return this;
    }

    /// <summary>Expects at most <paramref name="count"/> matching frames.</summary>
    /// <param name="count">The maximum match count. Must be non-negative.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is
    /// negative.</exception>
    public SseHasEventAssertion AtMost(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _comparison = SseCountComparison.AtMost;
        _expectedCount = count;
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".AtMost({count})");
        return this;
    }

    /// <summary>Expects exactly <paramref name="count"/> matching frames.</summary>
    /// <param name="count">The exact match count. Must be non-negative.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is
    /// negative.</exception>
    public SseHasEventAssertion Exactly(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _comparison = SseCountComparison.Exactly;
        _expectedCount = count;
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".Exactly({count})");
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
        var matchingEvents = new List<SseEvent>();
        var matchingData = new List<string>();
        foreach (var evt in events)
        {
            if (!string.Equals(evt.EventName, _eventName, StringComparison.Ordinal))
            {
                continue;
            }

            if (_dataPredicate is not null && !_dataPredicate(evt.Data))
            {
                matchingData.Add(evt.Data);
                continue;
            }

            matchingEvents.Add(evt);
        }

        var matchCount = matchingEvents.Count;
        if (CountSatisfiesComparison(matchCount))
        {
            return Task.FromResult(AssertionResult.Passed);
        }

        if (matchCount is 0 && _dataPredicate is null)
        {
            return Task.FromResult(AssertionResult.Failed(SseFailureMessage.EventNotFound(_eventName, events)));
        }

        if (_dataPredicate is not null && matchCount is 0 && matchingData.Count > 0)
        {
            return Task.FromResult(AssertionResult.Failed(SseFailureMessage.DataPredicateNotMatched(_eventName, matchingData)));
        }

        return Task.FromResult(AssertionResult.Failed(
            SseFailureMessage.EventCountMismatch(_eventName, _expectedCount, matchCount, _comparison)));
    }

    /// <inheritdoc/>
    protected override string GetExpectation()
    {
        var label = _comparison switch
        {
            SseCountComparison.AtLeast => "at least",
            SseCountComparison.AtMost => "at most",
            SseCountComparison.Exactly => "exactly",
            _ => "exactly",
        };

        return string.Concat(
            "to find ",
            label,
            " ",
            _expectedCount.ToString(CultureInfo.InvariantCulture),
            " event(s) of type \"",
            _eventName,
            "\"");
    }

    private bool CountSatisfiesComparison(int actual)
        => _comparison switch
        {
            SseCountComparison.AtLeast => actual >= _expectedCount,
            SseCountComparison.AtMost => actual <= _expectedCount,
            SseCountComparison.Exactly => actual == _expectedCount,
            _ => actual == _expectedCount,
        };
}

using System;
using System.Globalization;
using System.Threading.Tasks;
using SseAssertions;
using TUnit.Assertions.Attributes;
using TUnit.Assertions.Core;

namespace SseAssertions.TUnit;

/// <summary>
/// Fluent TUnit assertion that every Server-Sent Events frame in a wire-format string carries an <c>id:</c>, and, with
/// <c>StrictlyIncreasing()</c> or <c>Sequential()</c>, that the ids are integers in that order (the property a
/// <c>Last-Event-ID</c> reconnection relies on). <c>Until(eventName)</c> ends the window at a named event. The
/// failure lists the ids seen. Constructed by the TUnit source generator from the <c>HasSseEventIds</c> extension
/// on <see cref="string"/>.
/// </summary>
/// <remarks>
/// The receiver is parsed with <see cref="SseFrameParser.Parse(string)"/>; the check stops at the first violation,
/// as the streaming chains do, so every receiver gives the same verdict.
/// </remarks>
[AssertionExtension("HasSseEventIds")]
public sealed class SseEventIdsAssertion : Assertion<string>
{
    private readonly SseEventIdsMatcher _matcher = new();

    /// <summary>Initializes the assertion. Called by the TUnit source generator.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    public SseEventIdsAssertion(AssertionContext<string> context)
        : base(context)
    {
        Context.ExpressionBuilder.Append(".HasSseEventIds()");
    }

    /// <summary>Requires the ids to be integers, each greater than the one before.</summary>
    /// <returns>This assertion for chaining.</returns>
    public SseEventIdsAssertion StrictlyIncreasing()
    {
        _matcher.Order = SseIdOrder.StrictlyIncreasing;
        _matcher.StartingAt = null;
        Context.ExpressionBuilder.Append(".StrictlyIncreasing()");
        return this;
    }

    /// <summary>Requires the ids to be integers, each one more than the one before, from any first id.</summary>
    /// <returns>This assertion for chaining.</returns>
    public SseEventIdsAssertion Sequential()
    {
        _matcher.Order = SseIdOrder.Sequential;
        _matcher.StartingAt = null;
        Context.ExpressionBuilder.Append(".Sequential()");
        return this;
    }

    /// <summary>Requires the ids to be integers, each one more than the one before, starting at
    /// <paramref name="startingAt"/>.</summary>
    /// <param name="startingAt">The id of the first event.</param>
    /// <returns>This assertion for chaining.</returns>
    public SseEventIdsAssertion Sequential(long startingAt)
    {
        _matcher.Order = SseIdOrder.Sequential;
        _matcher.StartingAt = startingAt;
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".Sequential({startingAt})");
        return this;
    }

    /// <summary>Ends the observation window after the first event of type <paramref name="eventName"/>: only the
    /// events up to and including it are checked. When the body has no such event, the whole body is the
    /// window.</summary>
    /// <param name="eventName">The event-type name that ends the window. Matched case-sensitively.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="eventName"/> is
    /// <see langword="null"/>.</exception>
    public SseEventIdsAssertion Until(string eventName)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        _matcher.Until = eventName;
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".Until({"\""}{eventName}{"\""})");
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

        if (metadata.Value is not { } body)
        {
            return Task.FromResult(AssertionResult.Failed("the receiver was null"));
        }

        var failure = _matcher.Evaluate(SseFrameParser.Parse(body));
        return Task.FromResult(failure is null ? AssertionResult.Passed : AssertionResult.Failed(failure));
    }

    /// <inheritdoc/>
    protected override string GetExpectation() => _matcher.Expectation;
}

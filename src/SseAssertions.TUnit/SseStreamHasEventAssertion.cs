using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SseAssertions;
using TUnit.Assertions.Attributes;
using TUnit.Assertions.Core;

namespace SseAssertions.TUnit;

/// <summary>
/// Fluent TUnit assertion that reads a Server-Sent Events <see cref="Stream"/> and verifies the
/// presence (or count) of frames matching a given event-type name. Constructed by the TUnit source
/// generator from the <c>HasSseEvent(string, CancellationToken)</c> extension on <see cref="Stream"/>;
/// the narrower methods (<c>WithData</c>, <c>WithDataParsedAs&lt;T&gt;</c>, <c>WithId</c>,
/// <c>WithRetryMillis</c>) constrain which frames count and the count terminators (<c>AtLeast</c>,
/// <c>AtMost</c>, <c>Exactly</c>) close the assertion.
/// </summary>
/// <remarks>
/// The stream is read event by event inside <see cref="CheckAsync"/> (UTF-8, as the SSE specification
/// requires), and the read stops as soon as the outcome is decided: <c>AtLeast(n)</c> passes at the n-th
/// match, <c>AtMost</c> and <c>Exactly</c> fail once the count is exceeded, and <c>Until</c> ends the window at a
/// named event. Otherwise the read runs to the end of the stream or until the supplied
/// <see cref="CancellationToken"/> ends it. Matching is delegated to the shared <see cref="SseEventMatcher"/> so
/// the diagnostics match the <see cref="string"/> chain. When the token ends the read and the expectation is
/// unmet, the cancellation-truncated diagnostic takes precedence over the count/narrower failure. On an
/// <see cref="SseStream"/> the next assertion continues after the last event this one read.
/// </remarks>
[AssertionExtension("HasSseEvent")]
public sealed class SseStreamHasEventAssertion : Assertion<Stream>
{
    private readonly string _eventName;
    private readonly SseEventMatcher _matcher;
    private readonly CancellationToken _cancellationToken;

    /// <summary>Initializes the assertion. Called by the TUnit source generator.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    /// <param name="eventName">The SSE event-type name to look for. Matched case-sensitively
    /// against <see cref="SseEvent.EventName"/>.</param>
    /// <param name="cancellationToken">The upper bound of the read; its cancellation ends the read and the
    /// events that arrived are evaluated.</param>
    /// <exception cref="ArgumentNullException"><paramref name="eventName"/> is
    /// <see langword="null"/>.</exception>
    public SseStreamHasEventAssertion(
        AssertionContext<Stream> context, string eventName, CancellationToken cancellationToken = default)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        _eventName = eventName;
        _matcher = new SseEventMatcher(eventName);
        _cancellationToken = cancellationToken;
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".HasSseEvent({"\""}{eventName}{"\""})");
    }

    /// <summary>Narrows the assertion to frames whose <see cref="SseEvent.Data"/> satisfies the
    /// supplied predicate.</summary>
    /// <param name="predicate">The data predicate; frames for which it returns
    /// <see langword="true"/> are counted.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is
    /// <see langword="null"/>.</exception>
    public SseStreamHasEventAssertion WithData(Func<string, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _matcher.DataPredicate = predicate;
        Context.ExpressionBuilder.Append(".WithData(...)");
        return this;
    }

    /// <summary>Narrows the assertion to frames whose <see cref="SseEvent.Data"/> deserializes via
    /// <paramref name="parse"/> into a <typeparamref name="T"/> that satisfies
    /// <paramref name="predicate"/>.</summary>
    /// <typeparam name="T">The type the frame data is parsed into.</typeparam>
    /// <param name="parse">The deserializer applied to each candidate frame's
    /// <see cref="SseEvent.Data"/>. Supply a reflection-free parser (for example a
    /// source-generated <c>JsonSerializer.Deserialize</c> with a <c>JsonTypeInfo&lt;T&gt;</c>) so
    /// the assertion stays AOT-compatible. If it throws, the assertion fails naming the
    /// deserializer exception and the offending data.</param>
    /// <param name="predicate">The predicate applied to the parsed value; frames for which it
    /// returns <see langword="true"/> are counted.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="parse"/> or
    /// <paramref name="predicate"/> is <see langword="null"/>.</exception>
    public SseStreamHasEventAssertion WithDataParsedAs<T>(Func<string, T> parse, Func<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(parse);
        ArgumentNullException.ThrowIfNull(predicate);
        _matcher.ParsedNarrow = SseDataNarrow.Build(parse, predicate);
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithDataParsedAs<{typeof(T).Name}>(...)");
        return this;
    }

    /// <summary>Narrows the assertion to frames carrying the given <c>id:</c> directive.</summary>
    /// <param name="id">The expected <see cref="SseEvent.Id"/>. Matched case-sensitively; a frame
    /// without an <c>id:</c> directive never matches.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> is
    /// <see langword="null"/>.</exception>
    public SseStreamHasEventAssertion WithId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        _matcher.HasIdFilter = true;
        _matcher.ExpectedId = id;
        _matcher.IdPredicate = null;
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithId({"\""}{id}{"\""})");
        return this;
    }

    /// <summary>Narrows the assertion to frames whose <see cref="SseEvent.Id"/> satisfies the supplied
    /// predicate, for example "carries an id, whatever its value" (<c>id =&gt; id is not null</c>).</summary>
    /// <param name="predicate">The id predicate (<see langword="null"/> for a frame without an <c>id:</c>
    /// directive); frames for which it returns <see langword="true"/> are counted. Replaces an earlier
    /// <c>WithId</c>.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is
    /// <see langword="null"/>.</exception>
    public SseStreamHasEventAssertion WithId(Func<string?, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _matcher.HasIdFilter = false;
        _matcher.IdPredicate = predicate;
        Context.ExpressionBuilder.Append(".WithId(...)");
        return this;
    }

    /// <summary>Ends the observation window after the first event of type <paramref name="eventName"/>: only the
    /// events up to and including it count, and the read stops there. On an endless stream this
    /// bounds <c>AtMost</c> and <c>Exactly</c>, which otherwise read until the token ends the read. When the token
    /// ends the read before that event arrives, the assertion fails naming it; when the stream ends first, the
    /// whole stream is the window.</summary>
    /// <param name="eventName">The event-type name that ends the window. Matched case-sensitively.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="eventName"/> is
    /// <see langword="null"/>.</exception>
    public SseStreamHasEventAssertion Until(string eventName)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        _matcher.Until = eventName;
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".Until({"\""}{eventName}{"\""})");
        return this;
    }

    /// <summary>Narrows the assertion to frames whose <see cref="SseEvent.RetryMillis"/> satisfies
    /// the supplied predicate.</summary>
    /// <param name="predicate">The retry-value predicate (<see langword="null"/> for frames without
    /// a <c>retry:</c> directive); frames for which it returns <see langword="true"/> are
    /// counted.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is
    /// <see langword="null"/>.</exception>
    public SseStreamHasEventAssertion WithRetryMillis(Func<int?, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _matcher.RetryPredicate = predicate;
        Context.ExpressionBuilder.Append(".WithRetryMillis(...)");
        return this;
    }

    /// <summary>Expects at least <paramref name="count"/> matching frames.</summary>
    /// <param name="count">The minimum match count. Must be non-negative.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is
    /// negative.</exception>
    public SseStreamHasEventAssertion AtLeast(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _matcher.Comparison = SseCountComparison.AtLeast;
        _matcher.ExpectedCount = count;
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".AtLeast({count})");
        return this;
    }

    /// <summary>Expects at most <paramref name="count"/> matching frames.</summary>
    /// <param name="count">The maximum match count. Must be non-negative.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is
    /// negative.</exception>
    public SseStreamHasEventAssertion AtMost(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _matcher.Comparison = SseCountComparison.AtMost;
        _matcher.ExpectedCount = count;
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".AtMost({count})");
        return this;
    }

    /// <summary>Expects exactly <paramref name="count"/> matching frames.</summary>
    /// <param name="count">The exact match count. Must be non-negative.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is
    /// negative.</exception>
    public SseStreamHasEventAssertion Exactly(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _matcher.Comparison = SseCountComparison.Exactly;
        _matcher.ExpectedCount = count;
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".Exactly({count})");
        return this;
    }

    /// <inheritdoc/>
    protected override async Task<AssertionResult> CheckAsync(EvaluationMetadata<Stream> metadata)
    {
        if (metadata.Exception is not null)
        {
            return AssertionResult.Failed(
                $"threw {metadata.Exception.GetType().Name}: {metadata.Exception.Message}",
                metadata.Exception);
        }

        var stream = metadata.Value;
        if (stream is null)
        {
            return AssertionResult.Failed("the receiver was null");
        }

        return await SseStreamEvaluation.EvaluateAsync(_matcher, stream, _cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override string GetExpectation()
        => SseExpectation.Describe(_eventName, _matcher.Comparison, _matcher.ExpectedCount);
}

using System;
using System.Globalization;
using System.IO;
using System.Text;
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
/// The stream is drained inside <see cref="CheckAsync"/> via <see cref="SseStreamReader.ReadAsync"/>
/// (UTF-8, per the WHATWG SSE default), bounded by the supplied <see cref="CancellationToken"/>;
/// matching is delegated to the shared <see cref="SseEventMatcher"/> so the diagnostics match the
/// <see cref="string"/> chain. When the token cuts the read short and the expectation is unmet, the
/// cancellation-truncated diagnostic takes precedence over the count/narrower failure. The read
/// consumes the full token window and does not stop early once the expectation is satisfied, so the
/// token should be set to the intended time budget.
/// </remarks>
[AssertionExtension("HasSseEvent")]
public sealed class SseStreamHasEventAssertion : Assertion<Stream>
{
    private readonly string _eventName;
    private readonly SseEventMatcher _matcher;
    private readonly CancellationToken _cancellationToken;

    /// <summary>Initialises the assertion. Called by the TUnit source generator.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    /// <param name="eventName">The SSE event-type name to look for. Matched case-sensitively
    /// against <see cref="SseEvent.EventName"/>.</param>
    /// <param name="cancellationToken">Bounds the stream read; cancellation parses the partial
    /// buffer as best-effort SSE.</param>
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
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithId({"\""}{id}{"\""})");
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

        var (body, bytesReceived, cancelled) = await SseStreamReader.ReadAsync(
            stream, Encoding.UTF8, _cancellationToken).ConfigureAwait(false);
        return SseStreamEvaluation.Evaluate(_matcher, body, bytesReceived, cancelled);
    }

    /// <inheritdoc/>
    protected override string GetExpectation()
        => SseExpectation.Describe(_eventName, _matcher.Comparison, _matcher.ExpectedCount);
}

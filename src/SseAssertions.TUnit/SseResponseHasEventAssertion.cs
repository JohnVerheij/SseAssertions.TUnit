using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SseAssertions;
using TUnit.Assertions.Attributes;
using TUnit.Assertions.Core;

namespace SseAssertions.TUnit;

/// <summary>
/// Fluent TUnit assertion that reads a Server-Sent Events <see cref="HttpResponseMessage"/> body and
/// verifies the presence (or count) of frames matching a given event-type name. Constructed by the
/// TUnit source generator from the <c>HasSseEvent(string, bool, CancellationToken)</c> extension on
/// <see cref="HttpResponseMessage"/>; the narrower methods (<c>WithData</c>,
/// <c>WithDataParsedAs&lt;T&gt;</c>, <c>WithId</c>, <c>WithRetryMillis</c>) constrain which frames
/// count and the count terminators (<c>AtLeast</c>, <c>AtMost</c>, <c>Exactly</c>) close the
/// assertion.
/// </summary>
/// <remarks>
/// The body is drained inside <see cref="CheckAsync"/> via <see cref="SseStreamReader.ReadAsync"/>
/// using the response charset (UTF-8 fallback), bounded by the supplied
/// <see cref="CancellationToken"/>; matching is delegated to the shared <see cref="SseEventMatcher"/>.
/// When <c>strictContentType</c> is set (the default) a non-<c>text/event-stream</c> response fails
/// with the unexpected-content-type diagnostic before the body is read. When the token cuts the read
/// short and the expectation is unmet, the cancellation-truncated diagnostic takes precedence.
/// </remarks>
[AssertionExtension("HasSseEvent")]
public sealed class SseResponseHasEventAssertion : Assertion<HttpResponseMessage>
{
    private const string SseMediaType = "text/event-stream";

    private readonly string _eventName;
    private readonly SseEventMatcher _matcher;
    private readonly bool _strictContentType;
    private readonly CancellationToken _cancellationToken;

    /// <summary>Initialises the assertion. Called by the TUnit source generator.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    /// <param name="eventName">The SSE event-type name to look for. Matched case-sensitively
    /// against <see cref="SseEvent.EventName"/>.</param>
    /// <param name="strictContentType">When <see langword="true"/> (the default), the assertion
    /// fails if the response <c>Content-Type</c> media type is not <c>text/event-stream</c>. Set to
    /// <see langword="false"/> for test mocks that serve SSE without the canonical header.</param>
    /// <param name="cancellationToken">Bounds the response-body read; cancellation parses the
    /// partial buffer as best-effort SSE.</param>
    /// <exception cref="ArgumentNullException"><paramref name="eventName"/> is
    /// <see langword="null"/>.</exception>
    public SseResponseHasEventAssertion(
        AssertionContext<HttpResponseMessage> context,
        string eventName,
        bool strictContentType = true,
        CancellationToken cancellationToken = default)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        _eventName = eventName;
        _matcher = new SseEventMatcher(eventName);
        _strictContentType = strictContentType;
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
    public SseResponseHasEventAssertion WithData(Func<string, bool> predicate)
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
    public SseResponseHasEventAssertion WithDataParsedAs<T>(Func<string, T> parse, Func<T, bool> predicate)
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
    public SseResponseHasEventAssertion WithId(string id)
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
    public SseResponseHasEventAssertion WithRetryMillis(Func<int?, bool> predicate)
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
    public SseResponseHasEventAssertion AtLeast(int count)
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
    public SseResponseHasEventAssertion AtMost(int count)
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
    public SseResponseHasEventAssertion Exactly(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _matcher.Comparison = SseCountComparison.Exactly;
        _matcher.ExpectedCount = count;
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".Exactly({count})");
        return this;
    }

    /// <inheritdoc/>
    protected override async Task<AssertionResult> CheckAsync(EvaluationMetadata<HttpResponseMessage> metadata)
    {
        if (metadata.Exception is not null)
        {
            return AssertionResult.Failed(
                $"threw {metadata.Exception.GetType().Name}: {metadata.Exception.Message}",
                metadata.Exception);
        }

        var response = metadata.Value;
        if (response is null)
        {
            return AssertionResult.Failed("the receiver was null");
        }

        if (_strictContentType)
        {
            var mediaType = response.Content?.Headers?.ContentType?.MediaType;
            if (!string.Equals(mediaType, SseMediaType, StringComparison.OrdinalIgnoreCase))
            {
                return AssertionResult.Failed(SseFailureMessage.UnexpectedContentType(mediaType));
            }
        }

        // HttpResponseMessage.Content never returns null (the getter coalesces to an empty content),
        // so the body read is always safe; an empty body parses to zero events and fails with the
        // event-absent diagnostic, matching an empty stream.
        var (body, bytesReceived, cancelled) = await SseStreamReader.ReadResponseBodyAsync(
            response, _cancellationToken).ConfigureAwait(false);
        return SseStreamEvaluation.Evaluate(_matcher, body, bytesReceived, cancelled);
    }

    /// <inheritdoc/>
    protected override string GetExpectation()
        => SseExpectation.Describe(_eventName, _matcher.Comparison, _matcher.ExpectedCount);
}

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
/// Fluent TUnit assertion that every Server-Sent Events frame in a response body carries an <c>id:</c>, and, with
/// <c>StrictlyIncreasing()</c> or <c>Sequential()</c>, that the ids are integers in that order (the property a
/// <c>Last-Event-ID</c> reconnection relies on). <c>Until(eventName)</c> ends the window at a named event. The
/// failure lists the ids seen. Constructed by the TUnit source generator from the <c>HasSseEventIds</c> extension
/// on <see cref="HttpResponseMessage"/>.
/// </summary>
/// <remarks>
/// The body is read event by event and the read stops at the first violation or at the <c>Until</c> event;
/// otherwise it runs to the end of the body or until the cancellation token ends it. Unless
/// <c>strictContentType</c> is <see langword="false"/>, a response that is not <c>text/event-stream</c> fails
/// before the body is read.
/// </remarks>
[AssertionExtension("HasSseEventIds")]
public sealed class SseResponseEventIdsAssertion : Assertion<HttpResponseMessage>
{
    private const string SseMediaType = "text/event-stream";

    private readonly SseEventIdsMatcher _matcher = new();
    private readonly bool _strictContentType;
    private readonly CancellationToken _cancellationToken;

    /// <summary>Initializes the assertion with the <c>Content-Type</c> check on. Called by the TUnit source
    /// generator.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    /// <param name="cancellationToken">The upper bound of the read; its cancellation ends the read and the
    /// events that arrived are evaluated.</param>
    public SseResponseEventIdsAssertion(AssertionContext<HttpResponseMessage> context, CancellationToken cancellationToken = default)
        : this(context, strictContentType: true, cancellationToken)
    {
    }

    /// <summary>Initializes the assertion. Called by the TUnit source generator.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    /// <param name="strictContentType">When <see langword="true"/>, the assertion fails if the response
    /// <c>Content-Type</c> media type is not <c>text/event-stream</c>. Pass <see langword="false"/> for test mocks
    /// that serve SSE without the canonical header.</param>
    /// <param name="cancellationToken">The upper bound of the read; its cancellation ends the read and the
    /// events that arrived are evaluated.</param>
    public SseResponseEventIdsAssertion(
        AssertionContext<HttpResponseMessage> context,
        bool strictContentType,
        CancellationToken cancellationToken = default)
        : base(context)
    {
        _strictContentType = strictContentType;
        _cancellationToken = cancellationToken;
        Context.ExpressionBuilder.Append(".HasSseEventIds()");
    }

    /// <summary>Requires the ids to be integers, each greater than the one before.</summary>
    /// <returns>This assertion for chaining.</returns>
    public SseResponseEventIdsAssertion StrictlyIncreasing()
    {
        _matcher.Order = SseIdOrder.StrictlyIncreasing;
        _matcher.StartingAt = null;
        Context.ExpressionBuilder.Append(".StrictlyIncreasing()");
        return this;
    }

    /// <summary>Requires the ids to be integers, each one more than the one before, from any first id.</summary>
    /// <returns>This assertion for chaining.</returns>
    public SseResponseEventIdsAssertion Sequential()
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
    public SseResponseEventIdsAssertion Sequential(long startingAt)
    {
        _matcher.Order = SseIdOrder.Sequential;
        _matcher.StartingAt = startingAt;
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".Sequential({startingAt})");
        return this;
    }

    /// <summary>Ends the observation window after the first event of type <paramref name="eventName"/>: only the
    /// events up to and including it are checked, and the read stops there. When the token ends the read
    /// before that event arrives, the assertion fails naming it; when the stream ends first, the whole stream is
    /// the window.</summary>
    /// <param name="eventName">The event-type name that ends the window. Matched case-sensitively.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="eventName"/> is
    /// <see langword="null"/>.</exception>
    public SseResponseEventIdsAssertion Until(string eventName)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        _matcher.Until = eventName;
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".Until({"\""}{eventName}{"\""})");
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

        if (metadata.Value is not { } response)
        {
            return AssertionResult.Failed("the receiver was null");
        }

        if (_strictContentType)
        {
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (!string.Equals(mediaType, SseMediaType, StringComparison.OrdinalIgnoreCase))
            {
                return AssertionResult.Failed(SseFailureMessage.UnexpectedContentType(mediaType));
            }
        }

        var stream = await response.Content.ReadAsStreamAsync(CancellationToken.None).ConfigureAwait(false);
        return await SseStreamEvaluation.EvaluateAsync(_matcher, stream, _cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override string GetExpectation() => _matcher.Expectation;
}

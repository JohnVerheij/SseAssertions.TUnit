using System;
using System.Net.Http;
using TUnit.Assertions.Core;

namespace SseAssertions.TUnit;

/// <summary>
/// The default-on <c>Content-Type: text/event-stream</c> validation that every <see cref="HttpResponseMessage"/>
/// receiver shares, so each one fails with the same diagnostic.
/// </summary>
internal static class SseContentTypeCheck
{
    /// <summary>The media type of a Server-Sent Events response.</summary>
    internal const string SseMediaType = "text/event-stream";

    /// <summary>Checks the media type of <paramref name="response"/> when validation is on.</summary>
    /// <param name="response">The response; its <see cref="HttpResponseMessage.Content"/> is never null.</param>
    /// <param name="strictContentType">Whether the media type is validated at all.</param>
    /// <returns>The failing result when validation is on and the media type is not <c>text/event-stream</c>
    /// (compared case-insensitively); otherwise <see langword="null"/>.</returns>
    internal static AssertionResult? Validate(HttpResponseMessage response, bool strictContentType)
    {
        if (!strictContentType)
        {
            return null;
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        return string.Equals(mediaType, SseMediaType, StringComparison.OrdinalIgnoreCase)
            ? null
            : AssertionResult.Failed(SseFailureMessage.UnexpectedContentType(mediaType));
    }
}

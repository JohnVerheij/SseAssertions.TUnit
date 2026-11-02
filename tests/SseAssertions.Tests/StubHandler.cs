using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SseAssertions.Tests;

/// <summary>An HTTP handler that answers every request with the response the test builds, and records the
/// request's <c>Accept</c> header.</summary>
internal sealed class StubHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
{
    /// <summary>Gets the <c>Accept</c> header of the last request.</summary>
    public string? Accept { get; private set; }

    /// <summary>Gets the method of the last request.</summary>
    public HttpMethod? Method { get; private set; }

    /// <summary>Builds an HTTP client over a handler that answers with <paramref name="respond"/>.</summary>
    /// <param name="respond">Builds the response.</param>
    /// <param name="handler">The handler, for inspecting the request.</param>
    /// <returns>The client, with base address <c>http://localhost/</c>.</returns>
    public static HttpClient Client(Func<HttpResponseMessage> respond, out StubHandler handler)
    {
        handler = new StubHandler(respond);
        return new HttpClient(handler) { BaseAddress = new UriBuilder(Uri.UriSchemeHttp, "localhost").Uri };
    }

    /// <summary>Builds an SSE response over <paramref name="body"/>.</summary>
    /// <param name="body">The response body.</param>
    /// <param name="mediaType">The media type, or <see langword="null"/> for no <c>Content-Type</c>.</param>
    /// <param name="status">The status code.</param>
    /// <returns>The response.</returns>
    public static HttpResponseMessage Sse(Stream body, string? mediaType = "text/event-stream", HttpStatusCode status = HttpStatusCode.OK)
    {
        var content = new StreamContent(body);
        if (mediaType is not null)
        {
            content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        }

        return new HttpResponseMessage(status) { Content = content };
    }

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Accept = request.Headers.Accept.ToString();
        Method = request.Method;
        return Task.FromResult(respond());
    }
}

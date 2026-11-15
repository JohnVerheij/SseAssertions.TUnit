// What the README samples use without declaring it: the reader's own types, the test client and the usings the root
// README tells the reader to add. CI compiles the samples with this file (assertions-ci, README code samples); it is
// not part of any project here.
global using SseAssertions;
global using SseAssertions.TUnit;

using System.Text.Json.Serialization;

namespace ReadmeSamples;

/// <summary>An order update event of the reader, as the samples deserialize it.</summary>
/// <param name="OrderId">The order the update is about.</param>
/// <param name="Status">The new status of the order.</param>
public sealed record OrderEvent(int OrderId, string Status);

/// <summary>The reader's source-generated JSON context for <see cref="OrderEvent"/>.</summary>
[JsonSerializable(typeof(OrderEvent))]
public partial class MyJsonContext : JsonSerializerContext;

/// <summary>What every wrapping sample class inherits: the test client, an open stream, a token and a body.</summary>
public partial class Prelude
{
    /// <summary>The HTTP client of the reader's test fixture.</summary>
    protected HttpClient _client = new();

    /// <summary>An open stream, as the samples that start mid-test use it.</summary>
    protected SseStream sse = null!;

    /// <summary>The test's cancellation token.</summary>
    protected CancellationToken ct;

    /// <summary>A response body the reader already read.</summary>
    protected string responseBody = string.Empty;
}

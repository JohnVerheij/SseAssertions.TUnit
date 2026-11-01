# SseAssertions.TUnit

[![NuGet](https://img.shields.io/nuget/v/SseAssertions.TUnit.svg)](https://www.nuget.org/packages/SseAssertions.TUnit/)
[![Downloads](https://img.shields.io/nuget/dt/SseAssertions.TUnit.svg)](https://www.nuget.org/packages/SseAssertions.TUnit/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)

> **Scope:** Test projects only. Not intended for production code.

> Part of the **[DotNetAssertions](https://dotnetassertions.dev)** family of assertion extensions for TUnit.

TUnit-native Server-Sent Events (SSE) assertions for .NET. Fluent entry points over TUnit's `Assert.That(...)` pipeline for asserting on SSE event streams from HTTP response bodies, streams, and strings. `SseStream.OpenAsync` (in the core) connects to a live endpoint and returns once its first frame has arrived, and every streaming assertion reads only until its outcome is decided. AOT-compatible, trimmable, no runtime reflection in the assertion path.

> **Full documentation and roadmap:** [sseassertions.dev](https://sseassertions.dev) (source: [github.com/JohnVerheij/SseAssertions.TUnit](https://github.com/JohnVerheij/SseAssertions.TUnit))

## What ships

| Entry point | Receiver | Shape |
|---|---|---|
| `HasSseEvent(eventName)` | `string` | Chain with `WithData(predicate)`, `WithDataParsedAs<T>(parse, predicate)`, `WithId(id)`, `WithId(predicate)`, `WithRetryMillis(predicate)`, `Until(eventName)`, `AtLeast(n)`, `AtMost(n)`, `Exactly(n)` (`WithId(predicate)` and `Until` v0.8.0+) |
| `HasSseEvent(eventName, cancellationToken)` | `Stream`, including `SseStream` | Same chain; the read stops as soon as the outcome is decided (v0.8.0+), the token is the upper bound |
| `HasSseEvent(eventName, cancellationToken)` / `HasSseEvent(eventName, strictContentType, cancellationToken)` | `HttpResponseMessage` | Same chain; default-on `Content-Type: text/event-stream` validation (v0.7.0+); positional token (v0.8.0+) |
| `HasSseEventIds()` / `HasSseEventIds(cancellationToken)` | `string` / `Stream` / `HttpResponseMessage` | Every event carries an `id:`; chain with `StrictlyIncreasing()`, `Sequential()`, `Sequential(startingAt)`, `Until(eventName)`. The failure lists the ids seen. (v0.8.0+) |
| `IsServerSentEventsStream()` | `string` | Lightweight discriminator. |
| `HasSseContentType(strict)` | `HttpResponseMessage` | Header-only discriminator (no body read). `strict: false` (default) matches `text/event-stream` with any parameters; `strict: true` requires the bare media type with no parameters. |
| `HasFirstSseEvent(eventName)` | `string` | Asserts the first parsed frame's `event:` name. Unlabeled frames match `HasFirstSseEvent("message")` per the WHATWG default. |
| `HasFirstSseEvent(eventName, cancellationToken)` | `Stream` | Async; reads the first event only (v0.8.0+; earlier versions read to the end). |
| `HasFirstSseEvent(eventName, strictContentType, cancellationToken)` | `HttpResponseMessage` | Async; validates `Content-Type` (default-on) then asserts first frame. |
| `HasSseEventsInOrder(eventNames)` | `string` | Chain with `.WithStrictOrdering()`. Default permits gaps between named events; strict mode requires contiguous appearance. |
| `HasSseEventsInOrder(eventNames, strictOrdering, cancellationToken)` | `Stream` | Async flat form; the read stops once the sequence is complete (v0.8.0+). `strictOrdering: true` requires contiguous. |
| `HasSseEventsInOrder(eventNames, strictOrdering, strictContentType, cancellationToken)` | `HttpResponseMessage` | Async flat form with `Content-Type` validation (default-on). |
| `HasSseRetryDirective(millis)` | `string` | Asserts a `retry:` directive is present. `millis: null` accepts any value; `millis: <n>` requires exact match. |
| `HasSseRetryDirective(millis, cancellationToken)` | `Stream` | Async; same shape. |
| `HasSseRetryDirective(millis, strictContentType, cancellationToken)` | `HttpResponseMessage` | Async; validates `Content-Type` (default-on) then inspects retry directives. |
| `HasSseRetryDirectiveFirst()` | `string` / `Stream` / `HttpResponseMessage` | Asserts a `retry:` directive precedes the first non-empty `data:` field, checked at the wire-field level. An empty `data:` line carries no payload and does not count, so the standard ASP.NET Core control frame (`event: retry` + empty `data:` + `retry: <ms>`) passes. `Stream` / `HttpResponseMessage` forms take `cancellationToken`; `HttpResponseMessage` takes `strictContentType` (default-on). Matches the `retry:` directive field, not an `event: retry` named event: a stream emitting `event: retry` + `data:` with no `retry:` field fails. (v0.3.0+; empty-`data:` handling v0.4.1+) |
| `EndsCleanlyOnCancellation(cancellationToken)` | `Stream` / `HttpResponseMessage` | Asserts a canceled read tears down via cooperative cancellation rather than a transport exception (`IOException` / `HttpRequestException`). `HttpResponseMessage` form takes `strictContentType` (default-on) and reads the body via `ReadAsStreamAsync`. (`Stream` v0.3.0+; `HttpResponseMessage` v0.4.0+) |

The `HasSseEvent(eventName)` chain narrows by data payload (`WithData(Func<string, bool>)`, or `WithDataParsedAs<T>(Func<string, T> parse, Func<T, bool> predicate)` for a caller-supplied reflection-free deserializer), by the per-event `id:` directive (`WithId(string)`), or by the per-event `retry:` directive (`WithRetryMillis(Func<int?, bool>)`), then terminates with `AtLeast / AtMost / Exactly` count assertions. The same chain is available on all three receivers; on `Stream` and `HttpResponseMessage` the body is read event by event inside the assertion and the read stops once the outcome is decided, with the supplied cancellation token as the upper bound. `Until(eventName)` ends the observation window after the first event of that type. Successive assertions on one `SseStream` continue where the previous one stopped.

## Install

```bash
dotnet add package SseAssertions.TUnit
```

The framework-agnostic `SseAssertions` core (defining the `SseEvent` public record, `SseFrameParser`, and `SseFailureMessage` factories) comes transitively.

**Requirements:** TUnit 1.76.45 or later, .NET 10. AOT-compatible, trimmable, no runtime reflection in the assertion path.

## Quick start

```csharp
[Test]
public async Task TickEndpoint_EmitsThreeTicks()
{
    const string body = "event: tick\ndata: 1\n\nevent: tick\ndata: 2\n\nevent: tick\ndata: 3\n\n";

    await Assert.That(body).HasSseEvent("tick").Exactly(3);
}

[Test]
public async Task NotificationEndpoint_PublishesAtLeastThreeTicks(CancellationToken ct)
{
    using var response = await _client.GetAsync("/notifications/ticks?take=3", ct);

    await Assert.That(response).HasSseEvent("tick", cancellationToken: ct).AtLeast(3);
}
```

**On a live endpoint:**

```csharp
[Test]
public async Task Cycle_PublishesCompletion(CancellationToken ct)
{
    await using var sse = await SseStream.OpenAsync(_client, new Uri("/cycles/events", UriKind.Relative), ct);
    await _client.PostAsync("/cycles/start", content: null, ct);

    await Assert.That(sse).HasSseEvent("cycleCompleted", ct);
}
```

See the [full README](https://sseassertions.dev) for the Wire-format syntax reference, Failure diagnostics catalog, Cookbook (5 patterns), Design notes, and Out-of-scope caveats.

## License

[MIT](https://github.com/JohnVerheij/SseAssertions.TUnit/blob/main/LICENSE). Copyright (c) 2026 John Verheij.

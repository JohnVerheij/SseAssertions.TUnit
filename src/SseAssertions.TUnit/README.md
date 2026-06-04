# SseAssertions.TUnit

[![NuGet](https://img.shields.io/nuget/v/SseAssertions.TUnit.svg)](https://www.nuget.org/packages/SseAssertions.TUnit/)
[![Downloads](https://img.shields.io/nuget/dt/SseAssertions.TUnit.svg)](https://www.nuget.org/packages/SseAssertions.TUnit/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)

> **Scope:** Test projects only. Not intended for production code.

TUnit-native Server-Sent Events (SSE) assertions for .NET. Fluent entry points over TUnit's `Assert.That(...)` pipeline for asserting on SSE event streams from HTTP response bodies, streams, and strings. AOT-compatible, trimmable, no runtime reflection in the assertion path.

> **Full documentation and roadmap:** [github.com/JohnVerheij/SseAssertions.TUnit](https://github.com/JohnVerheij/SseAssertions.TUnit)

## What ships

| Entry point | Receiver | Shape |
|---|---|---|
| `HasSseEvent(eventName)` | `string` | Chain with `WithData(predicate)`, `AtLeast(n)`, `AtMost(n)`, `Exactly(n)` |
| `HasSseEvent(eventName, minCount, cancellationToken)` | `Stream` | Flat (`Task<AssertionResult>`); cancellation-bounded partial-buffer reads |
| `HasSseEvent(eventName, minCount, strictContentType, cancellationToken)` | `HttpResponseMessage` | Flat; default-on `Content-Type: text/event-stream` validation |
| `IsServerSentEventsStream()` | `string` | Lightweight discriminator. |
| `HasSseContentType(strict)` | `HttpResponseMessage` | Header-only discriminator (no body read). `strict: false` (default) matches `text/event-stream` with any parameters; `strict: true` requires the bare media type with no parameters. |
| `HasFirstSseEvent(eventName)` | `string` | Asserts the first parsed frame's `event:` name. Unlabelled frames match `HasFirstSseEvent("message")` per the WHATWG default. |
| `HasFirstSseEvent(eventName, cancellationToken)` | `Stream` | Async; reads to end then asserts first frame. |
| `HasFirstSseEvent(eventName, strictContentType, cancellationToken)` | `HttpResponseMessage` | Async; validates `Content-Type` (default-on) then asserts first frame. |
| `HasSseEventsInOrder(eventNames)` | `string` | Chain with `.WithStrictOrdering()`. Default permits gaps between named events; strict mode requires contiguous appearance. |
| `HasSseEventsInOrder(eventNames, strictOrdering, cancellationToken)` | `Stream` | Async flat form. `strictOrdering: true` requires contiguous. |
| `HasSseEventsInOrder(eventNames, strictOrdering, strictContentType, cancellationToken)` | `HttpResponseMessage` | Async flat form with `Content-Type` validation (default-on). |
| `HasSseRetryDirective(millis)` | `string` | Asserts a `retry:` directive is present. `millis: null` accepts any value; `millis: <n>` requires exact match. |
| `HasSseRetryDirective(millis, cancellationToken)` | `Stream` | Async; same shape. |
| `HasSseRetryDirective(millis, strictContentType, cancellationToken)` | `HttpResponseMessage` | Async; validates `Content-Type` (default-on) then inspects retry directives. |
| `HasSseRetryDirectiveFirst()` | `string` / `Stream` / `HttpResponseMessage` | Asserts a `retry:` directive precedes the first `data:` field, checked at the wire-field level. `Stream` / `HttpResponseMessage` forms take `cancellationToken`; `HttpResponseMessage` takes `strictContentType` (default-on). Matches the `retry:` directive field, not an `event: retry` named event: a stream emitting `event: retry` + `data:` carries no `retry:` field and correctly fails. (v0.3.0+) |
| `EndsCleanlyOnCancellation(cancellationToken)` | `Stream` / `HttpResponseMessage` | Asserts a cancelled read tears down via cooperative cancellation rather than a transport exception (`IOException` / `HttpRequestException`). `HttpResponseMessage` form takes `strictContentType` (default-on) and reads the body via `ReadAsStreamAsync`. (`Stream` v0.3.0+; `HttpResponseMessage` v0.4.0+) |

The chain on the `string` receiver composes `WithData(Func<string, bool>)` to narrow by data payload and `AtLeast / AtMost / Exactly` to terminate with a count assertion. The async receivers (`Stream`, `HttpResponseMessage`) use a flat-form entry point because composing an async body read with a synchronous chain is awkward in C#; the async-receiver chain is a candidate for a future release.

## Install

```bash
dotnet add package SseAssertions.TUnit
```

The framework-agnostic `SseAssertions` core (defining the `SseEvent` public record, `SseFrameParser`, and `SseFailureMessage` factories) comes transitively.

**Requirements:** TUnit 1.49.0 or later, .NET 10. AOT-compatible, trimmable, no runtime reflection in the assertion path.

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

    await Assert.That(response).HasSseEvent("tick", minCount: 3, cancellationToken: ct);
}
```

See the [full README](https://github.com/JohnVerheij/SseAssertions.TUnit) for the Wire-format syntax reference, Failure diagnostics catalog, Cookbook (5 patterns), Design notes, and Out-of-scope caveats.

## License

[MIT](https://github.com/JohnVerheij/SseAssertions.TUnit/blob/main/LICENSE). Copyright (c) 2026 John Verheij.

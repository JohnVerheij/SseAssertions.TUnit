# SseAssertions.TUnit

[![CI](https://github.com/JohnVerheij/SseAssertions.TUnit/actions/workflows/ci.yml/badge.svg)](https://github.com/JohnVerheij/SseAssertions.TUnit/actions/workflows/ci.yml)
[![CodeQL](https://github.com/JohnVerheij/SseAssertions.TUnit/actions/workflows/codeql.yml/badge.svg)](https://github.com/JohnVerheij/SseAssertions.TUnit/actions/workflows/codeql.yml)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/JohnVerheij/SseAssertions.TUnit/badge)](https://scorecard.dev/viewer/?uri=github.com/JohnVerheij/SseAssertions.TUnit)
[![OpenSSF Best Practices](https://www.bestpractices.dev/projects/16143/badge)](https://www.bestpractices.dev/projects/16143)
[![codecov](https://codecov.io/gh/JohnVerheij/SseAssertions.TUnit/branch/main/graph/badge.svg)](https://codecov.io/gh/JohnVerheij/SseAssertions.TUnit)
[![NuGet](https://img.shields.io/nuget/v/SseAssertions.TUnit.svg)](https://www.nuget.org/packages/SseAssertions.TUnit/)
[![Downloads](https://img.shields.io/nuget/dt/SseAssertions.TUnit.svg)](https://www.nuget.org/packages/SseAssertions.TUnit/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)

TUnit-native Server-Sent Events (SSE) assertions for .NET. Fluent entry points over TUnit's `Assert.That(...)` pipeline for asserting on SSE event streams from HTTP response bodies, streams, and strings. For a live endpoint, `SseStream.OpenAsync` returns once the server's first frame has arrived (it has subscribed), so a test can trigger events and assert on them without a sleep; every streaming assertion reads only until its outcome is decided. AOT-compatible, trimmable, no runtime reflection in the assertion path.

> **Scope:** Test projects only. Not intended for production code.

> Part of the **[DotNetAssertions](https://dotnetassertions.dev)** family of assertion extensions for TUnit.

---

## Table of contents

- [Why this package](#why-this-package)
- [Install](#install)
- [Package layout](#package-layout)
- [Namespaces](#namespaces)
- [Quick start](#quick-start)
- [Wire-format syntax](#wire-format-syntax)
- [Entry points](#entry-points)
- [Failure diagnostics](#failure-diagnostics)
- [Cookbook](#cookbook)
- [Out of scope](#out-of-scope)
- [Design notes](#design-notes)
- [Stability intent (pre-1.0)](#stability-intent-pre-10)
- [Roadmap](#roadmap)
- [Family compatibility](#family-compatibility)
- [Pair with](#pair-with)
- [Contributing](#contributing)
- [License](#license)

## Why this package

Server-Sent Events is a small, well-defined wire format that turns up frequently in
test code: an HTTP endpoint streams `event: tick\ndata: 1\n\n` frames; a test wants to
assert "the endpoint emitted at least 3 tick events" or "the order-update with id
42 carried status `shipped`". Without a dedicated assertion library that means
writing a small ad-hoc parser per test, or reaching for a heavier framework whose
mental model doesn't match the wire format.

`SseAssertions.TUnit` ships a focused fluent surface for that case:

- A WHATWG / W3C-compliant frame parser (`SseFrameParser.Parse(string)`) that
  produces `IReadOnlyList<SseEvent>` records.
- A single TUnit `HasSseEvent("tick")` chain on the `string`, `Stream`, and
  `HttpResponseMessage` receivers with the `WithData(predicate)` /
  `WithDataParsedAs<T>(parse, predicate)` / `WithId(id)` /
  `WithRetryMillis(predicate)` narrowers and `AtLeast(n)` / `AtMost(n)` /
  `Exactly(n)` terminators.
- On the `Stream` and `HttpResponseMessage` receivers the same chain adds
  cancellation-bounded partial-buffer reads and default-on
  `Content-Type: text/event-stream` validation, so per-frame payload can be
  asserted against a live, open SSE response.
- A public `SseFailureMessage` extension point so consumer-authored typed SSE
  assertions produce failure messages in the same shape as the shipped surface.

No runtime reflection. No `Microsoft.AspNetCore.*` dependency. AOT-clean from
day one. The framework-agnostic `SseAssertions` core ships separately so
non-TUnit consumers can reuse the parser.

## Install

```bash
# TUnit consumers install the adapter; the core is pulled transitively:
dotnet add package SseAssertions.TUnit

# Framework-agnostic consumers (rare in test projects) can pull the core directly:
dotnet add package SseAssertions
```

**Requirements:** TUnit 1.76.45 or later, .NET 10. AOT-compatible, trimmable.

## Package layout

| Package | Purpose | Depends on |
|---|---|---|
| [`SseAssertions`](https://www.nuget.org/packages/SseAssertions/) | Framework-agnostic core: `SseStream` (connect to a live endpoint), `SseEventReader` (read events as they arrive), `SseEvent` record, `SseFrameParser`, `SseFailureMessage`, `SseCountComparison`, `SseFormat` | BCL only |
| [`SseAssertions.TUnit`](https://www.nuget.org/packages/SseAssertions.TUnit/) | TUnit adapter: `HasSseEvent` and `HasSseEventIds` chains on `string`, `Stream`, and `HttpResponseMessage`, plus first-event, in-order, retry-directive, and `IsServerSentEventsStream()` checks | `SseAssertions` + `TUnit.Assertions` + `TUnit.Core` |

Install `SseAssertions.TUnit` for TUnit test projects; `SseAssertions` comes
transitively. Adapters for other test frameworks (NUnit, xUnit, MSTest) are not
shipped; they would reuse the `SseAssertions` core. Open a feature request if
you need one.

## Namespaces

| Type / member | Namespace | Auto-imported? |
|---|---|---|
| Fluent entry points (`HasSseEvent`, `HasSseEventIds`, `IsServerSentEventsStream`, ...) | `TUnit.Assertions.Extensions` | Yes (TUnit auto-imports) |
| Core types (`SseStream`, `SseEventReader`, `SseEvent`, `SseFrameParser`, `SseFailureMessage`, `SseCountComparison`, `SseFormat`) | `SseAssertions` | No - needs `using SseAssertions;` |
| Chain assertion class (`SseHasEventAssertion`) | `SseAssertions.TUnit` | No - generally invisible to consumers; only surfaces in failure-message types |

A `GlobalUsings.cs` in your test project:

```csharp
global using SseAssertions;
global using SseAssertions.TUnit;
```

makes both namespaces available everywhere. The fluent entry points
`HasSseEvent` and `IsServerSentEventsStream` are auto-imported via
`TUnit.Assertions.Extensions` so they need no `using` of their own.

## Quick start

**Assert on an SSE wire-format string:**

```csharp
[Test]
public async Task TickEndpoint_EmitsThreeTicks()
{
    const string body = "event: tick\ndata: 1\n\nevent: tick\ndata: 2\n\nevent: tick\ndata: 3\n\n";

    await Assert.That(body).HasSseEvent("tick").Exactly(3);
}
```

**Assert on an HTTP response body:**

```csharp
[Test]
public async Task NotificationEndpoint_PublishesTickEveryTwoSeconds(CancellationToken ct)
{
    using var response = await _client.GetAsync("/notifications/ticks?take=3", ct);

    await Assert.That(response).HasSseEvent("tick", cancellationToken: ct).AtLeast(3);
}
```

**Connect to a live endpoint, trigger, assert:**

```csharp
[Test]
public async Task Cycle_PublishesPredictionThenCompletion(CancellationToken ct)
{
    // Returns once the endpoint has written its first frame: it has subscribed, so nothing is missed.
    await using var sse = await SseStream.OpenAsync(_client, new Uri("/cycles/events", UriKind.Relative), ct);

    await _client.PostAsync("/cycles/start", content: null, ct);

    // Each assertion reads only until it is decided, and the next one continues from there.
    await Assert.That(sse).HasSseEvent("prediction", ct).WithId(id => id is not null);
    await Assert.That(sse).HasSseEvent("cycleCompleted", ct);
}
```

**Assert per-frame payload directly against the open response (no full materialization):**

```csharp
[Test]
public async Task OrderUpdates_StreamHasShippedEventForExpectedOrder(CancellationToken ct)
{
    using var response = await _client.GetAsync("/orders/42/updates?take=5", ct);

    await Assert.That(response)
        .HasSseEvent("order-update", cancellationToken: ct)
        .WithData(json => json.Contains("\"orderId\":42", StringComparison.Ordinal)
            && json.Contains("\"status\":\"shipped\"", StringComparison.Ordinal))
        .AtLeast(1);
}
```

## Wire-format syntax

Quick refresher of the WHATWG / W3C SSE wire format. The parser honors every
rule described here.

- **Field lines** have the form `field-name: field-value`. A single leading
  space after the colon is stripped per spec; subsequent spaces are preserved.
- **Recognized field names** are `event`, `data`, `id`, and `retry`. Unknown
  field names are ignored.
- **Comment lines** start with `:` and are ignored. They are typically used as
  keepalive heartbeats.
- **`data:` lines accumulate** within a frame, joined with `\n` (LF). A final
  trailing LF is stripped before dispatch.
- **A blank line dispatches the frame** as one event. A frame without any
  `data:` line is dropped per spec; the assertion library therefore never
  produces a frame with empty `EventName` and no observed data.
- **Default event name** is `"message"` when no explicit `event:` directive
  appears in the frame. `SseEvent.EventName` is non-nullable; the parser fills
  in the default. **Practical consequence for test fixtures**: a `data: ...\n\n`
  frame with no `event:` directive matches `HasSseEvent("message")`,
  `HasFirstSseEvent("message")`, and `HasSseEventsInOrder("message")`. This is
  per spec, not an assertion-library quirk: the WHATWG SSE algorithm dispatches
  unlabeled frames as `event: message`. Test fixtures that emit unlabeled
  frames must assert against `"message"`, not `null`.
- **`retry:` values** must be non-negative ASCII digits; non-numeric values are
  ignored.
- **`retry:` is a directive field, not a named event.** `HasSseRetryDirectiveFirst`
  matches the WHATWG `retry:` directive field, not an `event: retry` named event.
  A stream that emits `event: retry` followed by a `data:` field but no `retry:`
  field line fails ("no retry directive was found"). The check is spec-strict and
  reads the wire-level field, not the dispatched event name.
- **An empty `data:` line does not count as data.** The standard ASP.NET Core SSE
  writer serializes a reconnection control frame as `event: retry`, then an empty
  `data:` line, then `retry: <ms>` (the runtime fixes field order to event, data,
  id, retry). `HasSseRetryDirectiveFirst` ignores empty `data:` lines, so this
  control frame passes: the `retry:` directive is present and is the first event.
  Only a non-empty `data:` value before the first `retry:` fails.
- **Pin position and value in one read.** `HasSseRetryDirectiveFirst(millis)` asserts
  the leading `retry:` directive's position *and* value together, for a forward-only
  `HttpResponseMessage` body that cannot be read twice (the position-only
  `HasSseRetryDirectiveFirst()` and the value-only `HasSseRetryDirective(millis)`
  each consume the body, so they cannot both inspect the same response).
- **Line terminators**: `\n`, `\r\n`, and `\r` are all valid.
- **UTF-8 BOM** at byte offset 0 is consumed and ignored. A BOM-like character
  appearing mid-stream is treated as a regular character of its containing
  field's value.

## Entry points

| Receiver | Entry point | Returns | Chain methods |
|---|---|---|---|
| `string` | `.HasSseEvent(eventName)` | `SseHasEventAssertion` (chain) | `WithData(Func<string, bool>)`, `WithDataParsedAs<T>(Func<string, T>, Func<T, bool>)`, `WithId(string)`, `WithId(Func<string?, bool>)`, `WithRetryMillis(Func<int?, bool>)`, `Until(string)`, `AtLeast(int)`, `AtMost(int)`, `Exactly(int)` |
| `string` | `.IsServerSentEventsStream()` | flat - `AssertionResult` | - |
| `Stream` (incl. `SseStream`) | `.HasSseEvent(eventName, cancellationToken)` | `SseStreamHasEventAssertion` (chain) | `WithData`, `WithDataParsedAs<T>`, `WithId` (both), `WithRetryMillis`, `Until`, `AtLeast(int)`, `AtMost(int)`, `Exactly(int)` |
| `HttpResponseMessage` | `.HasSseEvent(eventName, cancellationToken)` or `.HasSseEvent(eventName, strictContentType, cancellationToken)` | `SseResponseHasEventAssertion` (chain) | `WithData`, `WithDataParsedAs<T>`, `WithId` (both), `WithRetryMillis`, `Until`, `AtLeast(int)`, `AtMost(int)`, `Exactly(int)` |
| `string` | `.HasSseEventIds()` | `SseEventIdsAssertion` (chain) | `StrictlyIncreasing()`, `Sequential()`, `Sequential(long startingAt)`, `Until(string)` |
| `Stream` (incl. `SseStream`) | `.HasSseEventIds(cancellationToken)` | `SseStreamEventIdsAssertion` (chain) | `StrictlyIncreasing()`, `Sequential()`, `Sequential(long startingAt)`, `Until(string)` |
| `HttpResponseMessage` | `.HasSseEventIds(cancellationToken)` or `.HasSseEventIds(strictContentType, cancellationToken)` | `SseResponseEventIdsAssertion` (chain) | `StrictlyIncreasing()`, `Sequential()`, `Sequential(long startingAt)`, `Until(string)` |
| `HttpResponseMessage` | `.HasSseContentType(strict)` | flat - `AssertionResult` | - |
| `string` | `.HasFirstSseEvent(eventName)` | flat - `AssertionResult` | - |
| `Stream` | `.HasFirstSseEvent(eventName, cancellationToken)` | flat - `Task<AssertionResult>` | - |
| `HttpResponseMessage` | `.HasFirstSseEvent(eventName, strictContentType, cancellationToken)` | flat - `Task<AssertionResult>` | - |
| `string` | `.HasSseEventsInOrder(eventNames)` | `SseEventsInOrderAssertion` (chain) | `WithStrictOrdering()` |
| `Stream` | `.HasSseEventsInOrder(eventNames, strictOrdering, cancellationToken)` | flat - `Task<AssertionResult>` | - |
| `HttpResponseMessage` | `.HasSseEventsInOrder(eventNames, strictOrdering, strictContentType, cancellationToken)` | flat - `Task<AssertionResult>` | - |
| `string` | `.HasSseRetryDirective(millis)` | flat - `AssertionResult` | - |
| `Stream` | `.HasSseRetryDirective(millis, cancellationToken)` | flat - `Task<AssertionResult>` | - |
| `HttpResponseMessage` | `.HasSseRetryDirective(millis, strictContentType, cancellationToken)` | flat - `Task<AssertionResult>` | - |
| `string` | `.HasSseRetryDirectiveFirst()` | flat - `AssertionResult` | - |
| `Stream` | `.HasSseRetryDirectiveFirst(cancellationToken)` | flat - `Task<AssertionResult>` | - |
| `HttpResponseMessage` | `.HasSseRetryDirectiveFirst(strictContentType, cancellationToken)` | flat - `Task<AssertionResult>` | - |
| `string` | `.HasSseRetryDirectiveFirst(millis)` | flat - `AssertionResult` | - |
| `Stream` | `.HasSseRetryDirectiveFirst(millis, cancellationToken)` | flat - `Task<AssertionResult>` | - |
| `HttpResponseMessage` | `.HasSseRetryDirectiveFirst(millis, strictContentType, cancellationToken)` | flat - `Task<AssertionResult>` | - |
| `Stream` | `.EndsCleanlyOnCancellation(cancellationToken)` | flat - `Task<AssertionResult>` | - |
| `HttpResponseMessage` | `.EndsCleanlyOnCancellation(strictContentType, cancellationToken)` | flat - `Task<AssertionResult>` | - |

The same `HasSseEvent(eventName)` chain is available on all three receivers. On
the `string` receiver the body is already in memory; on the `Stream` and
`HttpResponseMessage` receivers the body is read event by event inside the
assertion, and the read stops as soon as the outcome is decided: `AtLeast(n)`
passes at the n-th match, `AtMost` and `Exactly` fail once the count is exceeded,
`HasFirstSseEvent` reads one event, `HasSseEventsInOrder` stops once the sequence is
complete, and `HasSseEventIds` stops at the first violation. `Until(eventName)`
ends the observation window after the first event of that type, which bounds
`AtMost`, `Exactly`, and `HasSseEventIds` on an endless stream. Otherwise the read
runs to the end of the body or until the cancellation token ends it. The
retry-directive assertions read the whole body, because a bare `retry:` frame
carries no event; on a live stream, `SseStream.FirstFrame` gives the first
frame's `retry:` directly. The `string` receiver stops at the same point, so a body
gives the same verdict on every receiver. Bodies are decoded as UTF-8, as the
SSE specification requires.

`HasSseEventIds()` asserts that every event carries an `id:` (the property a
`Last-Event-ID` reconnection relies on); `StrictlyIncreasing()` and
`Sequential()` add the order of integer ids. Its failure lists the ids seen:

```csharp
await Assert.That(sse).HasSseEventIds(ct).Sequential(startingAt: 1).Until("cycleCompleted");
```

## Failure diagnostics

`SseFailureMessage` is the failure-message factory used by the shipped chain.
It is also `public` and intended as the extension point for consumer-authored
typed SSE assertions - see [Cookbook pattern 4](#pattern-4-consumer-authored-typed-sse-assertions).

**Missing event** (the chain looked for `"tick"`, none found):

```text
to find at least one event of type "tick"
  but observed: 0 events of type "tick" out of 2 total event(s) captured:
    [0] event=ping        data="alive"
    [1] event=heartbeat   data=""
```

**Count mismatch** (chain asked for at least 5 but observed 3):

```text
to find at least 5 event(s) of type "tick"
  but observed: 3 event(s) of type "tick"
```

**Data-predicate did not match** (chain found events of the right type but the
data predicate rejected all of them):

```text
to find at least one event of type "tick" whose Data satisfied the predicate
  but observed 3 event(s) of that type; none matched:
    data: "1"
    data: "2"
    data: "3"
```

**Content-Type mismatch** (`HttpResponseMessage` with `strictContentType: true`,
response carried `application/json`):

```text
the response to have Content-Type "text/event-stream"
  but got: application/json
```

**`Until` event never arrived** (the token ended the read before the event that
closes the window; the count itself was met):

```text
the read to reach an event of type "done", which ends the observation window
  but the read was canceled after 4 event(s), before one arrived
```

**Cancellation cut the read** (the `CancellationToken` fired mid-body-read; the
partial buffer was parsed and asserted but did not satisfy the count):

```text
to find at least 5 event(s) of type "tick"
  but observed: 2 event(s) of type "tick"
... and a follow-up:
the read was canceled after 1247 byte(s); parsed 3 event(s) from the partial buffer
  partial body excerpt: event: tick\ndata: 1\n\nevent: tick\ndata: 2\n\nevent: tick\nda...
```

Per-event `Data` is truncated at 80 characters in the failure list; body
excerpts (parse failures and cancellation excerpts) are truncated at 256
characters. Truncations use the U+2026 ellipsis (`...`).

## Cookbook

### Pattern 1: Assert events from a finite-output HTTP-response SSE stream

For SSE endpoints that emit a bounded number of events and close, the receiver
overload on `HttpResponseMessage` works end-to-end in one call:

```csharp
[Test]
public async Task NotificationEndpoint_PublishesAtLeastThreeTicks(CancellationToken ct)
{
    using var response = await _client.GetAsync("/notifications/ticks?take=3", ct);

    await Assert.That(response).HasSseEvent("tick", cancellationToken: ct).AtLeast(3);
}
```

### Pattern 2: JSON-payload composition without a JsonAssertions reference

Per the family's cross-package references rule, this package does not depend on
`JsonAssertions.TUnit`. Compose the two at the consumer's call site by reading
the body to a string and writing a `WithData` predicate that uses your
`JsonSerializerContext`:

```csharp
[Test]
public async Task OrderUpdates_StreamHasShippedEventForExpectedOrder(CancellationToken ct)
{
    using var response = await _client.GetAsync("/orders/42/updates?take=5", ct);
    var body = await response.Content.ReadAsStringAsync(ct);

    await Assert.That(body)
        .HasSseEvent("order-update")
        .WithData(json =>
        {
            var update = JsonSerializer.Deserialize(json, MyJsonContext.Default.OrderEvent);
            return update?.OrderId == 42 && update.Status == "shipped";
        })
        .AtLeast(1);
}
```

`MyJsonContext` is a consumer-defined `[JsonSerializable(typeof(OrderEvent))]`
context; deserialization stays AOT-clean.

### Pattern 3: Waiting for an event without polling

For "the endpoint will emit a `cache-ready` event within ten seconds", open the
stream and assert on the event itself: the read returns the moment the event
arrives, and the token bounds the wait. There is no need to poll the endpoint with
TUnit's `Eventually`.

```csharp
[Test]
public async Task CacheWarmup_EmitsReadyEvent(CancellationToken ct)
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(10));

    await using var sse = await SseStream.OpenAsync(_client, new Uri("/cache/status", UriKind.Relative), timeout.Token);
    await Assert.That(sse).HasSseEvent("cache-ready", timeout.Token);
}
```

### Pattern 4: Consumer-authored typed SSE assertions

`SseFailureMessage` is public so consumer-authored assertions can produce
failure messages that match this package's diagnostic style. Compose
`SseFrameParser.Parse` with one of the factory methods:

```csharp
using SseAssertions;
using TUnit.Assertions.Attributes;
using TUnit.Assertions.Core;

public static class OrderEventAssertions
{
    [GenerateAssertion]
    public static AssertionResult HasOrderShippedFor(this string body, int orderId)
    {
        var events = SseFrameParser.Parse(body);
        foreach (var evt in events)
        {
            if (!string.Equals(evt.EventName, "order-update", StringComparison.Ordinal))
            {
                continue;
            }

            var order = JsonSerializer.Deserialize(evt.Data, MyJsonContext.Default.OrderEvent);
            if (order?.OrderId == orderId && order.Status == "shipped")
            {
                return AssertionResult.Passed;
            }
        }

        return AssertionResult.Failed(SseFailureMessage.EventNotFound("order-update", events));
    }
}

// Test:
await Assert.That(responseBody).HasOrderShippedFor(orderId: 42);
```

### Pattern 5: Testing endpoints that never end

Production SSE endpoints typically stream indefinitely with heartbeats. Every
streaming assertion stops reading once its outcome is decided, so most of them
need nothing special:

```csharp
[Test]
public async Task Ticks_ArriveInOrderWithSequentialIds(CancellationToken ct)
{
    await using var sse = await SseStream.OpenAsync(_client, new Uri("/notifications/ticks", UriKind.Relative), ct);

    await Assert.That(sse).HasSseEvent("tick", ct).AtLeast(2);                     // stops at the 2nd tick
    await Assert.That(sse).HasSseEventIds(ct).Sequential().Until("checkpoint");    // stops at the checkpoint
}
```

`AtMost`, `Exactly`, and a passing `HasSseEventIds` need the end of a window to
decide: give it with `Until(eventName)` (the window ends after that event), or let
the token end the read. When the token ends the read before the `Until` event, the
assertion fails naming the event, even if the count was met; without `Until`, the
events that arrived before the token fired are evaluated.

Successive assertions on one `SseStream` continue where the previous one stopped.
To read events yourself, enumerate `sse.ReadEventsAsync(ct)` or use an
`SseEventReader`; stopping the enumeration leaves the stream at the next event.

A test-mode query parameter (`/notifications/ticks?take=3`, after which the
endpoint closes) still works and makes the read end with the body.

## Out of scope

Read this before opening a feature request.

- **`OfType(name)` chain method.** Redundant with `HasSseEvent(name)`.
- **`InAnyOrder()` chain method.** Set-semantics adds complexity for marginal
  benefit; the dominant pattern is order-insensitive `AtLeast(n)`.
- **`WithDataMatching(JsonPath)`.** Cross-package coupling rule prohibits a
  direct `JsonAssertions` reference. Compose via `WithData` and a
  consumer-provided deserialize delegate at the call site.
- **Server-side SSE production helpers.** This is an assertion library, not a
  producer.
- **Reconnection / last-event-id replay logic.** Consumer responsibility.
- **`xUnit` / `NUnit` / `MSTest` adapters.** TUnit only.

## Design notes

- **One `HasSseEvent` chain across all three receivers.** SSE assertions compose, so a chain (`HasSseEvent(name).WithData(...).AtLeast(n)`) stays linear where flat overloads would explode combinatorially. Same shape as `LogAssertions`' `HasLogged().WithException<T>().AtLeast(n)`. The `Stream` and `HttpResponseMessage` receivers read the body inside the assertion's `CheckAsync`, so the narrowers configure the matcher synchronously and the single async read happens once at evaluation time. The `string`, `Stream`, and `HttpResponseMessage` chains all delegate to the shared `SseEventMatcher`, so their diagnostics are identical.
- **`minCount` is `.AtLeast(n)`.** The streaming entry methods carry only what must precede the read (`strictContentType`, `cancellationToken`); the count is a terminator like every other receiver, which is why the pre-0.7.0 `HasSseEvent(eventName, minCount, ...)` flat form was replaced.
- **Content-Type `text/event-stream` validation is on by default.** It catches the foot-gun where a test hits the wrong endpoint and gets HTML / JSON / a 500: without it the parser silently yields zero events and the failure is confusing. Opt out with `strictContentType: false`. Comparison is case-insensitive (RFC 9110).
- **Caller-supplied deserialization, not `JsonTypeInfo<T>`.** `WithData` takes a `Func<string, bool>` and `WithDataParsedAs<T>` takes a `Func<string, T>` parse delegate, so any deserialization strategy stays at your call site. Neither forces a `System.Text.Json` dependency or an adapter; per the family's cross-package rule the package takes no `JsonAssertions` reference. Supply a source-generated `JsonTypeInfo<T>` inside the delegate to keep parsing AOT-compatible.
- **Incremental reads that stop when decided.** The `Stream` / `HttpResponseMessage` receivers read through `SseEventReader`, which parses events as bytes arrive, so an assertion stops at the event that decides it instead of waiting for the end of the body or the token. Lines are split at the byte level (in UTF-8, `\r` and `\n` never occur inside another character), so the bytes after the last event read stay unread: on an `SseStream`, the next assertion continues exactly there. When the token ends the read, the events that arrived are evaluated, and an unmet expectation reports the cancellation cut. Bodies are decoded as UTF-8, as the SSE specification requires; a `Content-Type` charset is ignored.
- **`SseStream` is the body stream.** The connect step returns a `Stream`, positioned after the first frame, so every existing streaming assertion applies to it unchanged. A separate receiver type would have duplicated every chain, and asserting on the response after a read would have lost the bytes already consumed.
- **The token is required where it is the only bound.** `SseStream.OpenAsync` and `SseEventReader.ReadAsync` take a `CancellationToken` without a default: nothing else ends a wait for a frame that never comes.

## Stability intent (pre-1.0)

This is a 0.x release and the public API may evolve.

- **Additive changes** (new entry points, new chain methods) ship in any patch
  without breaking ApiCompat.
- **Breaking changes** to existing signatures bump the minor version (0.X.0)
  and are called out in the [CHANGELOG](CHANGELOG.md).
- `PackageValidationBaselineVersion` pins to the previous shipped version so
  ApiCompat catches binary breaks at pack time; `CompatibilitySuppressions.xml`
  records accepted differences.

The 1.0 milestone signals API stability.

## Roadmap

Nothing planned beyond the current surface; new entry points are demand-driven.

## Family compatibility

The nine assertion-family packages: `LogAssertions.TUnit`, `TimeAssertions.TUnit`, `SnapshotAssertions.TUnit`, `MathAssertions.TUnit`, `JsonAssertions.TUnit`, `SseAssertions.TUnit`, `GrpcAssertions.TUnit`, `TracingAssertions.TUnit`, and `MetricsAssertions.TUnit`: release independently and target the same .NET TFM at any moment (LTS-anchored, multi-target during STS support windows; see the [TFM policy in CONVENTIONS.md](CONVENTIONS.md#tfm-policy) for the rotation schedule). **Mix versions freely.** Each package ships under SemVer with `EnablePackageValidation` strict-mode ApiCompat against its previous baseline, so binary breaks within a version line are caught at pack time.

For per-package release notes:
- [LogAssertions.TUnit CHANGELOG](https://github.com/JohnVerheij/LogAssertions.TUnit/blob/main/CHANGELOG.md)
- [TimeAssertions.TUnit CHANGELOG](https://github.com/JohnVerheij/TimeAssertions.TUnit/blob/main/CHANGELOG.md)
- [SnapshotAssertions.TUnit CHANGELOG](https://github.com/JohnVerheij/SnapshotAssertions.TUnit/blob/main/CHANGELOG.md)
- [MathAssertions.TUnit CHANGELOG](https://github.com/JohnVerheij/MathAssertions.TUnit/blob/main/CHANGELOG.md)
- [JsonAssertions.TUnit CHANGELOG](https://github.com/JohnVerheij/JsonAssertions.TUnit/blob/main/CHANGELOG.md)
- [SseAssertions.TUnit CHANGELOG](https://github.com/JohnVerheij/SseAssertions.TUnit/blob/main/CHANGELOG.md)
- [GrpcAssertions.TUnit CHANGELOG](https://github.com/JohnVerheij/GrpcAssertions.TUnit/blob/main/CHANGELOG.md)
- [TracingAssertions.TUnit CHANGELOG](https://github.com/JohnVerheij/TracingAssertions.TUnit/blob/main/CHANGELOG.md)
- [MetricsAssertions.TUnit CHANGELOG](https://github.com/JohnVerheij/MetricsAssertions.TUnit/blob/main/CHANGELOG.md)

## Pair with

- **[`LogAssertions.TUnit`](https://www.nuget.org/packages/LogAssertions.TUnit/)**: fluent log assertions over `Microsoft.Extensions.Logging.Testing.FakeLogCollector`.
- **[`TimeAssertions.TUnit`](https://www.nuget.org/packages/TimeAssertions.TUnit/)**: `TimeProvider`-aware time assertions and cross-cutting `.WithinTimeBudget(...)` chain methods.
- **[`SnapshotAssertions.TUnit`](https://www.nuget.org/packages/SnapshotAssertions.TUnit/)**: text-snapshot assertions for API-surface tests and similar deterministic-string scenarios. Coexists with Verify; covers the 80% case without coverage friction.
- **[`MathAssertions.TUnit`](https://www.nuget.org/packages/MathAssertions.TUnit/)**: tolerance-aware fluent assertions over numeric and geometric types (vectors, quaternions, matrices, planes, complex numbers, arrays).
- **[`JsonAssertions.TUnit`](https://www.nuget.org/packages/JsonAssertions.TUnit/)**: fluent JSON assertions over `System.Text.Json`, HTTP response bodies (including RFC 7807 ProblemDetails), and source-generated `JsonSerializerContext` registration.
- **[`GrpcAssertions.TUnit`](https://www.nuget.org/packages/GrpcAssertions.TUnit/)**: fluent gRPC outcome assertions (`ThrowsGrpcException` with `StatusCode` shorthands and detail refinements) plus the `GrpcCallBuilder` test-double helper.
- **[`TracingAssertions.TUnit`](https://www.nuget.org/packages/TracingAssertions.TUnit/)**: fluent OpenTelemetry distributed-tracing (`Activity` / span) assertions: operation name, tags, status, and parent/child and same-trace relationships, captured via a raw `ActivityListener` with no OpenTelemetry SDK dependency.
- **[`MetricsAssertions.TUnit`](https://www.nuget.org/packages/MetricsAssertions.TUnit/)**: fluent assertions over `System.Diagnostics.Metrics` instruments (counters, histograms, gauges), built on `MetricCollector`.

## Contributing

Issues and pull requests welcome. Before opening a PR:

- Run `dotnet build` and `dotnet test` locally; the CI pipeline enforces the same quality bar (zero warnings as errors, 90% line / 90% branch coverage minimum).
- Match the existing code style (`.editorconfig` is authoritative; `dotnet format` covers formatting).
- For new assertions, include a test for both the happy path and a representative failure case.

For larger ideas, open a [Discussion](https://github.com/JohnVerheij/SseAssertions.TUnit/discussions) first to align on direction before investing implementation time.

See [CONTRIBUTING.md](CONTRIBUTING.md) for the full PR review checklist, and [CONVENTIONS.md](CONVENTIONS.md) for the family-wide code conventions shared across `LogAssertions.TUnit`, `SnapshotAssertions.TUnit`, `TimeAssertions.TUnit`, `MathAssertions.TUnit`, `JsonAssertions.TUnit`, and this repo.

## License

[MIT](LICENSE). Copyright (c) 2026 John Verheij.

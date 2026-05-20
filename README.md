# SseAssertions.TUnit

[![CI](https://github.com/JohnVerheij/SseAssertions.TUnit/actions/workflows/ci.yml/badge.svg)](https://github.com/JohnVerheij/SseAssertions.TUnit/actions/workflows/ci.yml)
[![CodeQL](https://github.com/JohnVerheij/SseAssertions.TUnit/actions/workflows/codeql.yml/badge.svg)](https://github.com/JohnVerheij/SseAssertions.TUnit/actions/workflows/codeql.yml)
[![codecov](https://codecov.io/gh/JohnVerheij/SseAssertions.TUnit/branch/main/graph/badge.svg)](https://codecov.io/gh/JohnVerheij/SseAssertions.TUnit)
[![NuGet (SseAssertions)](https://img.shields.io/nuget/v/SseAssertions.svg?label=SseAssertions)](https://www.nuget.org/packages/SseAssertions/)
[![NuGet (SseAssertions.TUnit)](https://img.shields.io/nuget/v/SseAssertions.TUnit.svg?label=SseAssertions.TUnit)](https://www.nuget.org/packages/SseAssertions.TUnit/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)

TUnit-native Server-Sent Events (SSE) assertions for .NET. Fluent entry points over TUnit's `Assert.That(...)` pipeline for asserting on SSE event streams from HTTP response bodies, streams, and strings. AOT-compatible, trimmable, no runtime reflection in the assertion path.

> **Scope:** Test projects only. Not intended for production code.

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
- [Out of scope for v0.1.0](#out-of-scope-for-v010)
- [Design notes](#design-notes)
- [Stability intent (pre-1.0)](#stability-intent-pre-10)
- [Roadmap](#roadmap)
- [Family](#family)
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
- A TUnit `HasSseEvent("tick")` chain on the `string` receiver with
  `WithData(predicate)` + `AtLeast(n)` / `AtMost(n)` / `Exactly(n)` terminators.
- Flat `HasSseEvent(eventName, minCount, ...)` entry points on `Stream` and
  `HttpResponseMessage`, with cancellation-bounded partial-buffer reads and
  default-on `Content-Type: text/event-stream` validation.
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

**Requirements:** TUnit 1.44.39 or later, .NET 10. AOT-compatible, trimmable.

## Package layout

| Package | Purpose | Depends on |
|---|---|---|
| [`SseAssertions`](https://www.nuget.org/packages/SseAssertions/) | Framework-agnostic core: `SseEvent` record, `SseFrameParser`, `SseFailureMessage`, `SseCountComparison`, `SseFormat` | BCL only |
| [`SseAssertions.TUnit`](https://www.nuget.org/packages/SseAssertions.TUnit/) | TUnit adapter: `HasSseEvent` chain on `string`, flat `HasSseEvent` on `Stream` and `HttpResponseMessage`, plus `IsServerSentEventsStream()` discriminator | `SseAssertions` + `TUnit.Assertions` + `TUnit.Core` |

Install `SseAssertions.TUnit` for TUnit test projects; `SseAssertions` comes
transitively. Adapters for other test frameworks (NUnit, xUnit, MSTest) are not
shipped; they would reuse the `SseAssertions` core. Open a feature request if
you need one.

## Namespaces

| Type / member | Namespace | Auto-imported? |
|---|---|---|
| Fluent entry points (`HasSseEvent`, `IsServerSentEventsStream`) | `TUnit.Assertions.Extensions` | Yes (TUnit auto-imports) |
| Core types (`SseEvent`, `SseFrameParser`, `SseFailureMessage`, `SseCountComparison`, `SseFormat`) | `SseAssertions` | No - needs `using SseAssertions;` |
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

    await Assert.That(response).HasSseEvent("tick", minCount: 3, cancellationToken: ct);
}
```

**Assert with a data predicate at the consumer's call site:**

```csharp
[Test]
public async Task OrderUpdates_StreamHasShippedEventForExpectedOrder(CancellationToken ct)
{
    using var response = await _client.GetAsync("/orders/42/updates?take=5", ct);
    var body = await response.Content.ReadAsStringAsync(ct);

    await Assert.That(body)
        .HasSseEvent("order-update")
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
- **Recognised field names** are `event`, `data`, `id`, and `retry`. Unknown
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
  in the default.
- **`retry:` values** must be non-negative ASCII digits; non-numeric values are
  ignored.
- **Line terminators**: `\n`, `\r\n`, and `\r` are all valid.
- **UTF-8 BOM** at byte offset 0 is consumed and ignored. A BOM-like character
  appearing mid-stream is treated as a regular character of its containing
  field's value.

## Entry points

| Receiver | Entry point | Returns | Chain methods |
|---|---|---|---|
| `string` | `.HasSseEvent(eventName)` | `SseHasEventAssertion` (chain) | `WithData(Func<string, bool>)`, `AtLeast(int)`, `AtMost(int)`, `Exactly(int)` |
| `string` | `.IsServerSentEventsStream()` | flat - `Task<AssertionResult>` | - |
| `Stream` | `.HasSseEvent(eventName, minCount, ct)` | flat - `Task<AssertionResult>` | - |
| `HttpResponseMessage` | `.HasSseEvent(eventName, minCount, strictContentType, ct)` | flat - `Task<AssertionResult>` | - |

The chain pattern is available on the `string` receiver, where the body is
already in memory. On the async receivers (`Stream`, `HttpResponseMessage`) the
body read happens inside the assertion call and the entry point is flat; if you
need the chain over an HTTP response, read the body into a string in the test
and assert on the string. The async-receiver chain is a candidate for a future
release (see [Roadmap](#roadmap)).

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

**Cancellation cut the read** (the `CancellationToken` fired mid-body-read; the
partial buffer was parsed and asserted but did not satisfy the count):

```text
to find at least 5 event(s) of type "tick"
  but observed: 2 event(s) of type "tick"
... and a follow-up:
the read was cancelled after 1247 byte(s); parsed 3 event(s) from the partial buffer
  partial body excerpt: event: tick\ndata: 1\n\nevent: tick\ndata: 2\n\nevent: tick\nda…
```

Per-event `Data` is truncated at 80 characters in the failure list; body
excerpts (parse failures and cancellation excerpts) are truncated at 256
characters. Truncations use the U+2026 ellipsis (`…`).

## Cookbook

### Pattern 1: Assert events from a finite-output HTTP-response SSE stream

For SSE endpoints that emit a bounded number of events and close, the receiver
overload on `HttpResponseMessage` works end-to-end in one call:

```csharp
[Test]
public async Task NotificationEndpoint_PublishesAtLeastThreeTicks(CancellationToken ct)
{
    using var response = await _client.GetAsync("/notifications/ticks?take=3", ct);

    await Assert.That(response).HasSseEvent("tick", minCount: 3, cancellationToken: ct);
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

### Pattern 3: Polling with TUnit's built-in `Eventually`

For "the endpoint will emit a `cache-ready` event within ten seconds" without a
fixed `take` query parameter, compose with TUnit's `Eventually`:

```csharp
[Test]
public async Task CacheWarmup_EmitsReadyEventWithinTimeout(CancellationToken ct)
{
    await Assert.That(async () =>
        {
            using var response = await _client.GetAsync("/cache/status?take=1", ct);
            return await response.Content.ReadAsStringAsync(ct);
        })
        .Eventually(body => Assert.That(body).HasSseEvent("cache-ready").AtLeast(1))
        .WithinTimeBudget(TimeSpan.FromSeconds(10));
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

### Pattern 5: Testing infinite-stream endpoints (with cancellation-bounded reads)

Production SSE endpoints typically stream indefinitely with heartbeats. The
v0.1.0 buffer-mode read works against bounded-output endpoints; for indefinite
streams you have two approaches:

**(a) Test-mode query parameter** - design the endpoint with a finite-event mode:

```csharp
// Production: GET /notifications/ticks        -> infinite stream
// Test mode:  GET /notifications/ticks?take=3 -> emit 3 events then close
```

**(b) Cancellation-bounded read** - let the test cancel after a known emission
window. The chain captures the cancellation, parses whatever was buffered before
the cut, and asserts against the partial result. No try/catch needed; the
cancellation is part of the chain semantics at v0.1.0:

```csharp
[Test]
[CancelAfter(2_000)]
public async Task TickEndpoint_EmitsAtLeastTwoTicksInTwoSeconds(CancellationToken ct)
{
    using var response = await _client.GetAsync("/notifications/ticks", ct);

    await Assert.That(response).HasSseEvent("tick", minCount: 2, cancellationToken: ct);
}
```

Pattern (a) is the recommended approach for deterministic tests; pattern (b)
suits timing-sensitive scenarios where the endpoint cannot be modified. True
streaming async-enumerable mode is a candidate for a future release.

## Out of scope for v0.1.0

Read this before opening a feature request.

- **Async-receiver chains.** `WithData`, `WithDataParsedAs<T>`, `AtMost`,
  `Exactly`, `WithRetryMillis` attach to the chain on the `string` receiver
  only. The `Stream` and `HttpResponseMessage` receivers use a flat
  `HasSseEvent(eventName, minCount, ...)` entry point because composing an
  async body read with a synchronous fluent chain is awkward in C#.
- **Streaming async-enumerable mode.** v0.1.0 reads the entire response body
  before parsing; this works against bounded-output endpoints (see [Pattern
  5(a)](#pattern-5-testing-infinite-stream-endpoints-with-cancellation-bounded-reads))
  and combines with cancellation for indefinite streams (Pattern 5(b)). A
  true streaming `IAsyncEnumerable<SseEvent>` mode is a candidate for a future
  release.
- **`WithRetryMillis(predicate)`.** Per-event retry-value narrowers are
  deferred until consumer demand surfaces.
- **`OfType(name)` chain method.** Redundant with `HasSseEvent(name)`.
- **`InAnyOrder()` chain method.** Set-semantics adds complexity for marginal
  benefit; the dominant pattern is order-insensitive `AtLeast(n)`.
- **`WithDataMatching(JsonPath)`.** Cross-package coupling rule prohibits a
  direct `JsonAssertions` reference. Compose via `WithData` and a
  consumer-provided deserialize delegate at the call site.
- **Server-side SSE production helpers.** This is an assertion library, not a
  producer.
- **Reconnection / last-event-id replay logic.** Consumer responsibility.
- **`xUnit` / `NUnit` / `MSTest` adapters.** TUnit only at v0.1.0.

## Design notes

### Why the chain pattern (on the string receiver)

SSE assertions naturally compose. Flat methods would explode the overload set
(`HasSseEvent(eventName)`, `HasSseEvent(eventName, minCount)`,
`HasSseEvent(eventName, dataPredicate)`,
`HasSseEvent(eventName, dataPredicate, minCount)`, ...). The chain stays linear
regardless of how many narrowers are added. Precedent in the family:
`LogAssertions.TUnit`'s `HasLogged().WithException<T>().AtLeast(N)`.

### Why flat methods on async receivers

`Stream` and `HttpResponseMessage` receivers require the body read to happen
inside the assertion call. Composing the async read with a synchronous chain
forces every chain method to be async (`Task<SseAssertion>`) which breaks
fluent composition at the call site (`(await response.HasSseEvent(...)).AtLeast(2)`
is awkward to read). The flat-form encodes the most common shape - event name
plus minimum count plus optional content-type validation and cancellation - in
one call. The chain remains available on the `string` receiver for tests that
read the body first.

### Why `Content-Type: text/event-stream` validation is on by default

Default-on catches the foot-gun where a test hits the wrong endpoint and gets
HTML, JSON, or a 500 page - without content-type validation the parser silently
produces an empty event list and the assertion fails with a confusing
`but observed: 0 events`. Opt out via `strictContentType: false` for test mocks
that serve SSE without the canonical header. Comparison is case-insensitive
per RFC 9110 §8.3.2 media-type tokens.

### Why `Func<string, bool>` for data predicates (instead of `JsonTypeInfo<T>`)

Zero coupling. Consumers pass any deserialization strategy (source-gen STJ,
reflection STJ, Newtonsoft, custom format) inside the predicate body. A
`JsonTypeInfo<T>`-typed overload would force either an STJ runtime dependency
or an `AsJsonContext()`-style adapter (which the `JsonAssertions` package
needed because its receiver type *is* the context itself; `SseAssertions`'s
receiver is the wire format, so the simpler delegate works).

### Why no direct `JsonAssertions.TUnit` reference

Per the family's cross-package references rule (see [`CONVENTIONS.md`](CONVENTIONS.md)),
no sibling family package may appear as a `PackageReference` in another
sibling's production `.csproj`. JSON composition is at the consumer's call site
via standard delegates; the assertion family stays internally decoupled.

### Cancellation-bounded partial-buffer reads

The `Stream` and `HttpResponseMessage` overloads use `Content.ReadAsStreamAsync`
plus an `ArrayPool<byte>`-backed `ReadAsync` loop into a `MemoryStream` - not
`Content.ReadAsStringAsync`. `ReadAsStringAsync` discards the partial
accumulator when its cancellation fires; the manual loop captures whatever
bytes arrived before the cut and parses them as best-effort SSE. The encoding
is resolved from the response's `Content-Type` charset (UTF-8 fallback per
WHATWG SSE default).

### Eager parser materialisation

`SseFrameParser.Parse(string)` materialises the full `IReadOnlyList<SseEvent>`
plus every `SseEvent` instance plus every `Data` string eagerly. This is fine
for v0.1.0's test-time, bounded-buffer scenarios where event counts are small.
A future streaming-mode will also address the all-events-in-memory allocation
profile by yielding `SseEvent` instances on demand from the async-enumerable
receiver path.

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

- Async-receiver chains: bring `WithData`, `WithDataParsedAs<T>`, `AtMost`,
  `Exactly`, `WithRetryMillis` to the `Stream` and `HttpResponseMessage` entry
  points so the chain shape matches across all three receivers.
- Streaming async-enumerable read of `HttpResponseMessage` for indefinite-stream
  endpoints; yields `SseEvent` on demand.
- `WithRetryMillis(predicate)` narrower for protocol-conformance tests
  (`retry: 5000` first-event patterns).

Demand-driven; no fixed timeline.

## Family

Part of an assertion family for TUnit, each package independently versioned, targeting the same .NET TFM at any moment:

- **[`LogAssertions.TUnit`](https://www.nuget.org/packages/LogAssertions.TUnit/):** fluent log assertions over `Microsoft.Extensions.Logging.Testing.FakeLogCollector`.
- **[`SnapshotAssertions.TUnit`](https://www.nuget.org/packages/SnapshotAssertions.TUnit/):** text-snapshot assertions for API-surface tests and similar deterministic-string scenarios.
- **[`TimeAssertions.TUnit`](https://www.nuget.org/packages/TimeAssertions.TUnit/):** `TimeProvider`-aware time assertions and cross-cutting `.WithinTimeBudget(...)` chain methods.
- **[`MathAssertions.TUnit`](https://www.nuget.org/packages/MathAssertions.TUnit/):** tolerance comparisons, sequences, statistics, linear algebra, number theory, 3D geometry.
- **[`JsonAssertions.TUnit`](https://www.nuget.org/packages/JsonAssertions.TUnit/):** fluent JSON assertions over `System.Text.Json`, HTTP response bodies (including RFC 7807 ProblemDetails), and source-generated `JsonSerializerContext` registration.

## Contributing

Issues and pull requests welcome. Before opening a PR:

- Run `dotnet build` and `dotnet test` locally; the CI pipeline enforces the same quality bar (zero warnings as errors, 90% line / 90% branch coverage minimum).
- Match the existing code style (`.editorconfig` is authoritative; `dotnet format` covers formatting).
- For new assertions, include a test for both the happy path and a representative failure case.

For larger ideas, open a [Discussion](https://github.com/JohnVerheij/SseAssertions.TUnit/discussions) first to align on direction before investing implementation time.

See [CONTRIBUTING.md](CONTRIBUTING.md) for the full PR review checklist, and [CONVENTIONS.md](CONVENTIONS.md) for the family-wide code conventions shared across `LogAssertions.TUnit`, `SnapshotAssertions.TUnit`, `TimeAssertions.TUnit`, `MathAssertions.TUnit`, `JsonAssertions.TUnit`, and this repo.

## License

[MIT](LICENSE). Copyright (c) 2026 John Verheij.

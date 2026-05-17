# SseAssertions.TUnit

[![NuGet](https://img.shields.io/nuget/v/SseAssertions.TUnit.svg)](https://www.nuget.org/packages/SseAssertions.TUnit/)
[![Downloads](https://img.shields.io/nuget/dt/SseAssertions.TUnit.svg)](https://www.nuget.org/packages/SseAssertions.TUnit/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)

> **Scope:** Test projects only. Not intended for production code.

TUnit-native Server-Sent Events (SSE) assertions for .NET. Fluent entry points over TUnit's `Assert.That(...)` pipeline for asserting on SSE event streams from HTTP response bodies, streams, and strings. AOT-compatible, trimmable, no runtime reflection in the assertion path.

> **Full documentation and roadmap:** [github.com/JohnVerheij/SseAssertions.TUnit](https://github.com/JohnVerheij/SseAssertions.TUnit)

## Status: v0.0.1 (skeleton release)

Establishes the public adapter surface, claims the `SseAssertions.TUnit` identifier on nuget.org, and locks the quality bar before the wider per-frame assertion surface ships at v0.1.0.

| Entry point | Behaviour |
|---|---|
| `IsServerSentEventsStream()` on `string` | Asserts the supplied text has the shape of an SSE stream: at least one field marker (`event:`, `data:`, `id:`, `retry:`) followed by a frame separator. |

The fluent entry point auto-imports via `TUnit.Assertions.Extensions`; no extra `using` directive is needed beyond standard TUnit usings.

The full v0.1.0 surface adds:

- W3C SSE frame parser
- Per-frame JSON-payload deserialization helpers via `JsonTypeInfo<T>` integration (delegate plumbing)
- `[GenerateAssertion]` fluent extensions on `Stream` and `HttpResponseMessage` (in addition to `string`)
- `SseAssertion` chain type with `.OfType(eventName)`, `.UntilEventType(eventName)`, `.Collect()`, `.AtLeast(n)`, `.AtMost(n)`, `.Exactly(n)`

## Install

```bash
dotnet add package SseAssertions.TUnit
```

The framework-agnostic `SseAssertions` core (defining the `SseEvent` public record) comes transitively.

**Requirements:** TUnit 1.44.39 or later, .NET 10. AOT-compatible, trimmable, no runtime reflection in the assertion path.

## Quick start

```csharp
[Test]
public async Task ResponseLooksLikeSseStream()
{
    const string body = "event: tick\ndata: 1\n\nevent: tick\ndata: 2\n\n";

    await Assert.That(body).IsServerSentEventsStream();
}
```

## License

[MIT](https://github.com/JohnVerheij/SseAssertions.TUnit/blob/main/LICENSE). Copyright (c) 2026 John Verheij.

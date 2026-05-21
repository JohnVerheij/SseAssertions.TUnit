# SseAssertions

[![NuGet](https://img.shields.io/nuget/v/SseAssertions.svg)](https://www.nuget.org/packages/SseAssertions/)
[![Downloads](https://img.shields.io/nuget/dt/SseAssertions.svg)](https://www.nuget.org/packages/SseAssertions/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)

> **Scope:** Test projects only. Not intended for production code.

Framework-agnostic core for Server-Sent Events (SSE) assertions in .NET test projects. Defines the `SseEvent` public record (per the WHATWG / W3C SSE wire format), the `SseFrameParser` that turns wire text into `IReadOnlyList<SseEvent>`, the `SseFailureMessage` extension point for typed assertions, and the `SseCountComparison` enum that backs count terminators.

> **Full documentation, roadmap, and the TUnit adapter:** [github.com/JohnVerheij/SseAssertions.TUnit](https://github.com/JohnVerheij/SseAssertions.TUnit)

## What ships

| Type | Purpose |
|---|---|
| `SseEvent` (public record) | Stable public data type. `EventName` (non-nullable, defaults to `"message"` per the WHATWG spec when no `event:` directive appears), `Data` (non-null), `Id?`, `RetryMillis?`. |
| `SseFrameParser.Parse(string)` | WHATWG / W3C SSE wire-format parser; handles all three line terminators, strips a UTF-8 BOM at offset 0, ignores comment lines, accumulates multi-line data with `\n` joins. |
| `SseFailureMessage` | Curated failure-message factories (`ParseFailure`, `EventNotFound`, `EventCountMismatch`, `DataPredicateNotMatched`, `DataDeserializationFailed`, `RetryMillisPredicateNotMatched`, `UnexpectedContentType`, `CancellationCutRead`) for consumer-authored typed SSE assertions. |
| `SseCountComparison` (public enum) | Comparison label (`AtLeast`, `AtMost`, `Exactly`) carried by `EventCountMismatch`. |
| `SseFormat.LooksLikeServerSentEvents(string)` | Lightweight discriminator. |

Test-framework-specific entry points live in adapter packages: [`SseAssertions.TUnit`](https://www.nuget.org/packages/SseAssertions.TUnit/) ships today. xUnit, NUnit, MSTest adapters are possible if demand surfaces.

## Install

```bash
dotnet add package SseAssertions
```

**Requirements:** .NET 10. The package carries zero runtime dependencies beyond BCL.

## License

[MIT](https://github.com/JohnVerheij/SseAssertions.TUnit/blob/main/LICENSE). Copyright (c) 2026 John Verheij.

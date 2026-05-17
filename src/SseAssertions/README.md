# SseAssertions

[![NuGet](https://img.shields.io/nuget/v/SseAssertions.svg)](https://www.nuget.org/packages/SseAssertions/)
[![Downloads](https://img.shields.io/nuget/dt/SseAssertions.svg)](https://www.nuget.org/packages/SseAssertions/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)

> **Scope:** Test projects only. Not intended for production code.

Framework-agnostic core for Server-Sent Events (SSE) assertions in .NET test projects. Defines the `SseEvent` public record (`EventName` / `Id` / `RetryMillis` / `Data` per the WHATWG / W3C SSE wire format) and the entry-point helpers consumed by the TUnit adapter.

> **Full documentation, roadmap, and the TUnit adapter:** [github.com/JohnVerheij/SseAssertions.TUnit](https://github.com/JohnVerheij/SseAssertions.TUnit)

## Status: v0.0.1 (skeleton release)

Establishes the public surface seam, claims the `SseAssertions` identifier on nuget.org, and locks the quality bar before the wider parser + per-frame helper surface ships at v0.1.0.

| Type | Purpose |
|---|---|
| `SseEvent` (public record) | Stable public data type. `EventName?`, `Id?`, `RetryMillis?`, `Data` (non-null). |
| `SseFormat.LooksLikeServerSentEvents(string)` | Lightweight discriminator: does the text have the shape of an SSE stream? |

Test-framework-specific entry points live in adapter packages — [`SseAssertions.TUnit`](https://www.nuget.org/packages/SseAssertions.TUnit/) ships today. xUnit, NUnit, MSTest adapters are possible if demand surfaces.

## Install

```bash
dotnet add package SseAssertions
```

**Requirements:** .NET 10. The package carries zero runtime dependencies beyond BCL.

## License

[MIT](https://github.com/JohnVerheij/SseAssertions.TUnit/blob/main/LICENSE). Copyright (c) 2026 John Verheij.

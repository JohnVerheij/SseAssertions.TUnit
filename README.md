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

## Status: v0.0.1 (skeleton release)

This is the initial preview. The release establishes the repository, claims the `SseAssertions` and `SseAssertions.TUnit` package identifiers on nuget.org, and locks the API style and quality bar before the wider parser + per-frame assertion surface ships at v0.1.0.

**What's shipped at v0.0.1:**

| Type / member | Namespace | Purpose |
|---|---|---|
| `SseEvent` (public record) | `SseAssertions` | The stable public data type. Fields per the WHATWG / W3C SSE wire format: `EventName`, `Id`, `RetryMillis`, `Data`. |
| `SseFormat.LooksLikeServerSentEvents(string)` | `SseAssertions` | Lightweight discriminator returning `true` when the supplied text has the shape of an SSE stream. |
| `IsServerSentEventsStream()` | `TUnit.Assertions.Extensions` (auto-imported) | Fluent `Assert.That(body).IsServerSentEventsStream()` over `string`, generated via TUnit's `[GenerateAssertion]`. |

The full surface (frame parser, per-event chain assertions, HTTP-response and `Stream` overloads, `.OfType` / `.UntilEventType` / `.Collect` chain methods) ships at v0.1.0.

---

## Install

```bash
# TUnit consumers install the adapter; the core is pulled transitively:
dotnet add package SseAssertions.TUnit

# Framework-agnostic consumers (rare in test projects) can pull the core directly:
dotnet add package SseAssertions
```

**Requirements:** TUnit 1.44.39 or later, .NET 10. The packages carry no runtime dependency beyond TUnit (and BCL `System.IO` / `System.Text` for the core). AOT-compatible, trimmable.

## Quick start

```csharp
using SseAssertions.TUnit;  // for the v0.1.0+ richer surface; auto-imported via TUnit at v0.0.1 already

[Test]
public async Task ResponseLooksLikeSseStream()
{
    const string body = "event: tick\ndata: 1\n\nevent: tick\ndata: 2\n\n";

    await Assert.That(body).IsServerSentEventsStream();
}
```

The v0.1.0 surface adds per-event chain assertions, HTTP-response and `Stream` overloads, and per-frame JSON-payload deserialization helpers. See the v0.1.0 roadmap below.

---

## Package layout

| Package | Purpose | Depends on |
|---|---|---|
| [`SseAssertions`](https://www.nuget.org/packages/SseAssertions/) | Framework-agnostic core: `SseEvent` record + `SseFormat` helpers | BCL only |
| [`SseAssertions.TUnit`](https://www.nuget.org/packages/SseAssertions.TUnit/) | TUnit adapter: `[GenerateAssertion]` fluent entry points over `Assert.That(...)` | `SseAssertions` + `TUnit.Assertions` + `TUnit.Core` |

You install `SseAssertions.TUnit`; `SseAssertions` comes transitively. Adapters for other test frameworks (NUnit, xUnit, MSTest) are *not* shipped today; they would reuse the `SseAssertions` core. Open a feature request if you need one.

## Roadmap to v0.1.0

- W3C SSE frame parser producing `IEnumerable<SseEvent>` (UTF-8 BOM handling, `\n` / `\r\n` / `\r` line-terminators per spec, comment-line ignoring).
- Per-frame JSON-payload deserialization helpers via `JsonTypeInfo<T>` integration (delegate plumbing; no `JsonAssertions` package reference per the family's cross-package references rule).
- `[GenerateAssertion]` fluent extensions on `Stream`, `HttpResponseMessage`, and `string`.
- `SseAssertion` chain type with `.OfType(eventName)`, `.UntilEventType(eventName)`, `.Collect()`, `.AtLeast(n)`, `.AtMost(n)`, `.Exactly(n)`.
- README expansion to the family-canonical structure (Table of contents, Why this package, Namespaces + `GlobalUsings.cs` recommendation, Entry points, Failure diagnostics, Cookbook of common patterns, Design notes).

---

## Stability intent (pre-1.0)

This is a 0.x release and the public API may evolve.

- **Additive changes** (new entry points, new input overloads) ship in any patch without breaking ApiCompat.
- **Breaking changes** to existing signatures bump the minor version (0.X.0) and are called out in the [CHANGELOG](CHANGELOG.md).
- `PackageValidationBaselineVersion` pins to the previous shipped version from v0.0.2 onward so ApiCompat catches binary breaks at pack time; `CompatibilitySuppressions.xml` records accepted differences.

The 1.0 milestone signals API stability.

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

## License

[MIT](LICENSE). Copyright (c) 2026 John Verheij.

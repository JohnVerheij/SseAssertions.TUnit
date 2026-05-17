# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] - 2026-05-17: Frame parser, fluent HasSseEvent entry points across three receivers, failure-message extension point

Minor release. Lifts the package from skeleton to functional: the WHATWG / W3C SSE wire-format frame parser ships, with `HasSseEvent` fluent entry points on `string`, `Stream`, and `HttpResponseMessage` receivers, plus a public `SseFailureMessage` factory surface for consumer-authored typed assertions.

### Added

- `SseFrameParser.Parse(string)` returns `IReadOnlyList<SseEvent>` for a WHATWG / W3C SSE wire-format body. Handles all three line terminators (`\n`, `\r\n`, `\r`), strips a single leading UTF-8 BOM at offset 0, ignores comment lines (lines starting with `:`), accumulates multi-line `data:` fields joined with `\n`, parses `retry:` as a non-negative integer (non-numeric values ignored per spec), tolerates unknown fields, and dispatches the trailing frame when the body lacks a final blank line. Per-event `Id` and `RetryMillis` semantics (a small deliberate deviation from the browser stream-wide model) so consumers can assert directly on the wire-format directives observed in each frame.
- `SseFailureMessage` public extension point with eight factory methods: `ParseFailure`, `EventNotFound`, `EventCountMismatch`, `DataPredicateNotMatched`, `DataDeserializationFailed`, `RetryMillisPredicateNotMatched`, `UnexpectedContentType`, `CancellationCutRead`. Truncation rules pinned across the family: per-event `Data` at 80 characters, body excerpts at 256 characters, U+2026 ellipsis as suffix.
- `SseCountComparison` public enum (`AtLeast`, `AtMost`, `Exactly`) backing the chain's count terminators and the typed comparison label in `SseFailureMessage.EventCountMismatch`.
- `HasSseEvent(string eventName)` chain entry point on the `string` receiver, via `[AssertionExtension("HasSseEvent")]`. Returns `SseHasEventAssertion`. Chain methods: `WithData(Func<string, bool>)` (narrow to frames whose `Data` satisfies the predicate), `AtLeast(int)`, `AtMost(int)`, `Exactly(int)`.
- `HasSseEvent(string eventName, int minCount = 1, CancellationToken ct = default)` flat entry point on the `Stream` receiver via `[GenerateAssertion]`. Returns `Task<AssertionResult>`. Reads the stream into a buffer via `ArrayPool`-backed `ReadAsync`; on `OperationCanceledException`, the partial buffer is parsed as best-effort SSE and asserted against. On chain pass the assertion passes; on chain fail the failure renders the `CancellationCutRead` diagnostic.
- `HasSseEvent(string eventName, int minCount = 1, bool strictContentType = true, CancellationToken ct = default)` flat entry point on the `HttpResponseMessage` receiver via `[GenerateAssertion]`. Validates `Content-Type: text/event-stream` by default (case-insensitive per RFC 9110 section 8.3.2); opt-out with `strictContentType: false` for test mocks that serve SSE without the canonical header. Body read uses `Content.ReadAsStreamAsync` plus the same partial-buffer cancellation pattern as the `Stream` receiver, with encoding resolved from the response's `Content-Type` charset (UTF-8 fallback per WHATWG default).

### Changed

- **BREAKING:** `SseEvent` record reshaped. v0.0.1 declared `SseEvent(string? EventName, string? Id, int? RetryMillis, string Data)`; v0.1.0 declares `SseEvent(string EventName, string Data, string? Id = null, int? RetryMillis = null)`. `EventName` is now non-nullable and the parser fills in `"message"` per the WHATWG default when no `event:` directive appears in the frame. Constructor parameter order shifts to put non-nullable fields first (`EventName`, `Data`) before nullable ones (`Id`, `RetryMillis`). The deliberate baseline break is recorded in `CompatibilitySuppressions.xml`. Migration note for v0.0.1 consumers: if your code matched `evt is SseEvent { EventName: null }` (the v0.0.1 idiom for "this frame had no `event:` directive"), the branch is dead at v0.1.0 because the parser populates `"message"` per the spec; switch the pattern to `evt is SseEvent { EventName: "message" }`. Named-argument constructor calls (`new SseEvent(EventName: ..., Data: ...)`) continue to compile; positional calls need to swap argument order to match the new shape.
- `PackageValidationBaselineVersion` pinned to `0.0.1` on both packages; ApiCompat strict-mode validates v0.1.0 against the v0.0.1 baseline at pack time.
- README adopts the family-canonical structure (Table of contents, Why, Install, Package layout, Namespaces, Quick start, Wire-format syntax, Entry points, Failure diagnostics, Cookbook, Out of scope, Design notes, Stability intent, Roadmap, Family, Contributing, License). The per-package READMEs packed into the nupkgs are updated for the v0.1.0 surface.
- `CONVENTIONS.md` updated to v0.7: formalises a per-package strict-scope policy for all six family packages and clarifies the core+adapter packaging rule (five of six packages are core+adapter; `JsonAssertions.TUnit` is the sole single-package member). The file is copied identically across all six repos.

## [0.0.1] - 2026-05-17: Initial preview, skeleton release establishing repository, package identifiers, and quality bar

First public release of the assertion family's 6th package. Two NuGet packages ship from day one to claim both identifiers on nuget.org: a framework-agnostic `SseAssertions` core and a TUnit-native `SseAssertions.TUnit` adapter. .NET 10, AOT-compatible, trimmable, no runtime reflection in the assertion path.

The 0.0.1 scope is intentionally narrow. The release exists to establish the repository, claim the `SseAssertions` and `SseAssertions.TUnit` package identifiers on nuget.org, and lock the API style and quality bar before the wider parser + per-frame assertion surface ships at 0.1.0.

### Added (`SseAssertions`, framework-agnostic core)

- **`SseEvent` public record** with the field set defined by the WHATWG / W3C Server-Sent Events specification: `EventName` (the `event:` field, nullable), `Id` (the `id:` field, nullable), `RetryMillis` (the `retry:` field, nullable), `Data` (the joined `data:` lines, non-null). The stable public data type the assertion family consumes; the 0.1.0 frame parser produces `IEnumerable<SseEvent>`.
- **`SseFormat.LooksLikeServerSentEvents(string)`** lightweight discriminator returning `true` when the supplied text contains at least one SSE field marker (`event:`, `data:`, `id:`, `retry:`) followed by the frame-separator double newline. Intentionally cheap and forgiving; the full per-frame parser arrives in 0.1.0.

### Added (`SseAssertions.TUnit`, TUnit adapter)

- **`IsServerSentEventsStream()`** fluent entry point on `string`, generated via TUnit's `[GenerateAssertion]`. Delegates to `SseFormat.LooksLikeServerSentEvents` and produces a structured failure message when the text doesn't look like an SSE stream.

Both namespaces ship in their respective shipped assemblies; both NuGet packages are independently versioned but evolve in lockstep at the v0.x stage.

### Roadmap to v0.1.0

The wider surface lands at 0.1.0 as a reviewed pull request:

- W3C SSE frame parser producing `IEnumerable<SseEvent>`
- Per-frame JSON-payload deserialization helpers via `JsonTypeInfo<T>` integration (delegate plumbing; no `JsonAssertions` package reference per CONVENTIONS.md cross-package references rule)
- `[GenerateAssertion]` fluent extensions on `Stream`, `HttpResponseMessage`, and `string`
- `SseAssertion` chain type with `.OfType(eventName)`, `.UntilEventType(eventName)`, `.Collect()`, `.AtLeast(n)`, `.AtMost(n)`, `.Exactly(n)`, etc.

### Quality bar (locked at 0.0.1)

- AOT-compatible (`IsAotCompatible=true`), trimmable (`IsTrimmable=true`), no runtime reflection in the assertion path.
- `Nullable=enable`, `TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true`.
- Five Roslyn analyzer packs at full strength (Meziantou, SonarAnalyzer, Roslynator, Microsoft.VisualStudio.Threading, DotNetProjectFile.Analyzers).
- `Microsoft.CodeAnalysis.BannedApiAnalyzers` enforces no-reflection at build time.
- ApiCompat strict mode wired; `PackageValidationBaselineVersion` will pin to 0.0.1 starting from 0.0.2.
- 90% line / 90% branch coverage CI gates.
- Public API surface pinned via snapshot tests using `SnapshotAssertions.TUnit` plus `PublicApiGenerator`; cross-package dogfooding against the family.
- External-consumer smoke test (deliberately different namespace, deliberately different package-resolution path) plus AOT-publish gate on `linux-x64`.
- Trusted Publishing (OIDC) to nuget.org; no long-lived secrets.
- SLSA v1.0 build provenance plus CycloneDX 1.6 SBOM plus SPDX 3.0 SBOM plus OpenVEX v0.2.0 plus Sigstore-signed attestations on every release.
- Source Link, deterministic builds, embedded PDB.
- TUnit dependency pinned to **1.44.39**.

[Unreleased]: https://github.com/JohnVerheij/SseAssertions.TUnit/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/JohnVerheij/SseAssertions.TUnit/compare/v0.0.1...v0.1.0
[0.0.1]: https://github.com/JohnVerheij/SseAssertions.TUnit/releases/tag/v0.0.1

# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- **`CONVENTIONS.md` updated to v0.7**: formalises a per-package strict-scope policy with explicit scope statements for all six family packages, and clarifies the core+adapter packaging rule (five of six packages are core+adapter; `JsonAssertions.TUnit` is the sole single-package member). The file is copied identically across all six repos. `SseAssertions.TUnit` bootstrapped at v0.5; this is its first cumulative update.

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

[Unreleased]: https://github.com/JohnVerheij/SseAssertions.TUnit/compare/v0.0.1...HEAD
[0.0.1]: https://github.com/JohnVerheij/SseAssertions.TUnit/releases/tag/v0.0.1

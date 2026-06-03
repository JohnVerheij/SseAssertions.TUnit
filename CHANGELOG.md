# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.4.0] - 2026-06-04: HttpResponseMessage receiver for EndsCleanlyOnCancellation

Minor release. Adds the `HttpResponseMessage` receiver to `EndsCleanlyOnCancellation`, so a cancellation-teardown assertion can run directly against an HTTP response without first extracting the body stream. The `0.3.0` retry-first surface already covered `string`, `Stream`, and `HttpResponseMessage`; this release closes the matching gap for the clean-cancellation assertion. Purely additive; the `0.3.0` ApiCompat baseline is preserved.

### Added

- **`Assert.That(response).EndsCleanlyOnCancellation(strictContentType, cancellationToken)`** on `HttpResponseMessage`. Reads the response body via `ReadAsStreamAsync(cancellationToken)` and asserts the read tears down via cooperative cancellation (the read completes, or raises `OperationCanceledException`) rather than surfacing a transport exception (`IOException`, `HttpRequestException`). Mirrors the existing `Stream` overload's teardown classification and the content-type handling of the other `HttpResponseMessage` receivers: `strictContentType` defaults to `true` (fails with the unexpected-content-type diagnostic when `Content-Type`'s media type is not `text/event-stream`); a null `Content` passes. Source-generated via `[GenerateAssertion]`.

### Changed

- Bumped `PackageValidationBaselineVersion` from `0.2.0` to `0.3.0` on both packages so ApiCompat strict-mode validates `0.4.0` against the most recently published baseline. The `0.4.0` change is purely additive; no `CompatibilitySuppressions.xml` updates required.
- README and packed-README clarification: `HasSseRetryDirectiveFirst` matches the WHATWG `retry:` directive field, not an `event: retry` named event. A stream that emits `event: retry` followed by a `data:` field carries no `retry:` field line, so the assertion correctly fails ("no retry directive was found"). The check is spec-strict and reads the wire-level field, not the dispatched event name.

## [0.3.0] - 2026-06-02: retry-first and clean-cancellation stream assertions

Feature release. Adds two SSE-correctness refinements on the live-stream surface: `HasSseRetryDirectiveFirst()` asserts the server sent a `retry:` directive before any data, and `EndsCleanlyOnCancellation()` asserts a cancelled read tears down cooperatively rather than surfacing a transport exception. Also folds in the accumulated CI hardening and docs hygiene from the unreleased line.

### Added

- **`Assert.That(response).HasSseRetryDirectiveFirst()`** (with `Stream` and `string` receivers) asserts that the SSE stream sends a `retry:` directive before any `data:` field. A `retry:` directive is a reconnection-time hint, not a named event, so the order is checked at the wire-field level: a retry-only frame carries no data and is not dispatched as a parsed event, so it would otherwise be invisible to an event-list check. Source-generated via `[GenerateAssertion]`.
- **`Assert.That(stream).EndsCleanlyOnCancellation(cancellationToken)`** asserts that cancelling the read mid-stream tears down via cooperative cancellation (the read completes, or raises `OperationCanceledException`) rather than surfacing a transport exception (`IOException`, `HttpRequestException`). The assertion drains and discards content, checking only teardown behaviour.
- **`SseFailureMessage.UncleanCancellation(Exception)`** renders the failure message for `EndsCleanlyOnCancellation`, naming the transport exception that surfaced. Public, matching the existing failure-message factory surface for consumer-authored typed SSE assertions.

### Changed

- Removed `paths-ignore` from `.github/workflows/ci.yml` so the `Build, test & pack` required check always reports a status. The previous filter excluded `**.md`, `LICENSE`, `.gitignore`, and `.editorconfig` from triggering CI; combined with branch protection's `Required` flag on `Build, test & pack`, this left docs-only PRs stuck in `Expected — Waiting for status to be reported` and unmergeable. The trade-off is a few minutes of CI per docs-only PR; on a free-tier public repo the cost is zero. Sibling repos receive the same fix as part of their open `chore/infra-family-consistency-sweep` PRs (or a dedicated PR for `TimeAssertions.TUnit`).
- Dropped drift-prone own-version anchors from packed READMEs (`src/SseAssertions.TUnit/README.md`, `src/SseAssertions/README.md`). `## What v0.2.0 ships` and `## What v0.1.0 ships` headings are now `## What ships`; `(carried over from v0.0.1)` parenthetical removed from the entry-points table. Historical "added in vX.Y" markers (none in this repo) are not affected by the sweep. The CHANGELOG remains the single source of truth for what shipped when.
- Added GitHub Actions workflow security scanning. `.github/workflows/zizmor.yml` runs `zizmor` (blocking, with findings shown as inline annotations) on every workflow change; `.github/workflows/codeql.yml` now analyzes the `actions` language alongside `csharp`; `.github/workflows/scorecard.yml` (OpenSSF Scorecard) and `.github/workflows/dependency-review.yml` (fails a PR that adds a high-severity-vulnerable dependency) are new. Added the Renovate `helpers:pinGitHubActionDigestsToSemver` preset so any newly-introduced action is auto-pinned to a commit SHA. CI-only; no effect on shipped packages.

### Security

- Hardened GitHub Actions token handling: set `persist-credentials: false` on every `actions/checkout` so the repository token is not written into `.git/config`; moved the inline coverage-report expression in `ci.yml` into an `env:` variable to remove a template-injection vector; and scoped workflow write permissions (`security-events` on `codeql`; `contents`/`id-token`/`packages`/`attestations` on `release`) to the job level with a read-only workflow-level default. CI-only; no released package is affected.

## [0.2.0] - 2026-05-20: HasSseContentType, HasFirstSseEvent, HasSseEventsInOrder, HasSseRetryDirective

Minor release. Adds four new assertions covering common SSE smoke-test patterns: a header-only Content-Type discriminator (`HasSseContentType`), a first-event check (`HasFirstSseEvent`), an ordered-sequence check with optional contiguous mode (`HasSseEventsInOrder` + `.WithStrictOrdering()`), and a `retry:`-directive check (`HasSseRetryDirective`). Two README accuracy fixes for the v0.1.0 entry-points table; expanded WHATWG default-event-name documentation. No breaking changes; v0.1.0 ApiCompat baseline preserved.

### Added

- `HasSseContentType(bool strict = false)` on `HttpResponseMessage`. Synchronous, header-only discriminator (does not read the body). Non-strict mode passes when `Content-Type`'s media type is `text/event-stream` (case-insensitive) with any trailing parameters such as `charset=utf-8`. Strict mode requires the bare media type with no parameters. Use as a lightweight smoke-test alternative to the `HasSseEvent(strictContentType)` form that reads and parses the body.
- `HasFirstSseEvent(string eventName)` on `string`, `Stream`, and `HttpResponseMessage`. Asserts the first parsed SSE frame's `event:` name. Unlabelled frames match `HasFirstSseEvent("message")` per the WHATWG default-event-name rule. `Stream` and `HttpResponseMessage` overloads are async with `CancellationToken`-bounded reads; the `HttpResponseMessage` overload validates `Content-Type` by default (opt-out via `strictContentType: false`). Failure diagnostics name the observed first event or report `"stream contained no events"`.
- `HasSseEventsInOrder(string[] eventNames)` on `string` (chain) with `.WithStrictOrdering()` modifier, plus `HasSseEventsInOrder(IReadOnlyList<string>, bool strictOrdering, ...)` flat form on `Stream` and `HttpResponseMessage`. Default (non-strict) mode requires each named event to appear in the given order with other events permitted between them; strict mode requires the named events to appear contiguously with no other events between them. An empty `eventNames` array trivially passes. Failure diagnostics name the violation (`"X (index N) appeared before Y (index M)"`, `"Z was not in the stream"`, or `"W appeared at index N instead of V"` for strict contiguous mismatches).
- `HasSseRetryDirective(int? millis = null)` on `string`, `Stream`, and `HttpResponseMessage`. With `millis: null` (default), passes when any frame carries a `retry:` directive (any value); with `millis: <n>`, passes when at least one frame carries `retry: <n>` (any-match semantics across multiple `retry:` directives in the same stream). `HttpResponseMessage` overload validates `Content-Type` by default (opt-out via `strictContentType: false`).
- `SseEventsInOrderAssertion` public sealed class consumed by TUnit's source generator to emit the `HasSseEventsInOrder` chain extension on `IAssertionSource<string>`. Exposes `Evaluate(events, expectedNames, strict)` as an internal helper so the flat `Stream` / `HttpResponseMessage` overloads share the same ordering logic.

### Changed

- README entry-points table: corrected `IsServerSentEventsStream()`'s documented return type from `Task<AssertionResult>` to `AssertionResult` (the API is synchronous) and the cancellation parameter name from `ct` to `cancellationToken` for `HasSseEvent` on `Stream` and `HttpResponseMessage` (the actual API parameter; named-argument callers using `ct:` would not compile). The same fixes applied to the package README's entry-points table.
- Expanded the README "Default event name" bullet in the Wire-format syntax section with a "Practical consequence for test fixtures" note: unlabelled `data: ...\n\n` frames match `HasSseEvent("message")`, `HasFirstSseEvent("message")`, and `HasSseEventsInOrder("message")` per WHATWG; fixtures that emit unlabelled frames must assert against `"message"`, not `null`.
- Bumped `PackageValidationBaselineVersion` from `0.0.1` to `0.1.0` on both packages; ApiCompat strict-mode now validates v0.2.0 against the v0.1.0 baseline at pack time. The v0.2.0 changes are purely additive; no `CompatibilitySuppressions.xml` updates required.
- Replaced forward-looking `v0.2.0` mentions in the README (`async-receiver chain is a v0.2.0 candidate`, `streaming async-enumerable mode is a v0.2.0 candidate`, `WithRetryMillis(predicate)` deferral wording) with version-agnostic phrasing; renamed `## Roadmap to v0.2.0` to `## Roadmap` and `## Out of scope for v0.1.0` to `## Out of scope` so the section headings stop drifting with each release.
- Added a Downloads badge and collapsed the two NuGet version badges to a single adapter-only badge so the banner set matches `TimeAssertions.TUnit`. Replaced the single `## Family` section with `## Family compatibility` (release / ApiCompat prose + per-package CHANGELOG cross-links) and `## Pair with` (sibling package descriptions) to match the family-repo structure.
- Migrated CI dependency automation from Dependabot to Renovate (`.github/renovate.json`), matching `TimeAssertions.TUnit`. Daily schedule (before 4am Europe/Amsterdam), `customManagers` keep TUnit version literals in the root README, package README, smoketest csproj, and bug-report Issue Form in lockstep with the central `Directory.Packages.props` pin. `platformAutomerge` replaces the separate `dependabot-auto-merge.yml` workflow. Dependency dashboard issue enabled. Explicit semantic commit scopes: `deps(nuget)`, `ci(github-actions)`, `ci(dotnet-sdk)`. Auto-merge covers `digest`, `pin`, `pinDigest`, and `lockFileMaintenance` updateTypes alongside `minor` and `patch` so SHA-pinned GitHub Actions digest bumps go through without manual intervention. The three TUnit packages (`TUnit`, `TUnit.Assertions`, `TUnit.Core`) are grouped into a single PR per release.

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
- Pinned `PackageValidationBaselineVersion` to `0.0.1` on both packages; ApiCompat strict-mode now validates v0.1.0 against the v0.0.1 baseline at pack time.
- Updated `README.md` to the family-canonical structure (Table of contents, Why, Install, Package layout, Namespaces, Quick start, Wire-format syntax, Entry points, Failure diagnostics, Cookbook, Out of scope, Design notes, Stability intent, Roadmap, Family, Contributing, License).
- Refreshed the per-package READMEs (`src/SseAssertions/README.md`, `src/SseAssertions.TUnit/README.md`, packed into the nupkgs) for the v0.1.0 surface.
- Updated `CONVENTIONS.md` to v0.7.
- Added a per-package strict-scope policy section to `CONVENTIONS.md` with explicit scope statements for all six family packages.
- Added a core+adapter packaging rule section to `CONVENTIONS.md`: five of six family packages ship core+adapter; `JsonAssertions.TUnit` is the sole single-package member.
- Synchronised `CONVENTIONS.md` across all six family repos (the file is copied identically).

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

[Unreleased]: https://github.com/JohnVerheij/SseAssertions.TUnit/compare/v0.4.0...HEAD
[0.4.0]: https://github.com/JohnVerheij/SseAssertions.TUnit/releases/tag/v0.4.0
[0.3.0]: https://github.com/JohnVerheij/SseAssertions.TUnit/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/JohnVerheij/SseAssertions.TUnit/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/JohnVerheij/SseAssertions.TUnit/compare/v0.0.1...v0.1.0
[0.0.1]: https://github.com/JohnVerheij/SseAssertions.TUnit/releases/tag/v0.0.1

# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [metrics] tag convention

Any changelog entry that changes a computed metric value **must** be prefixed with `[metrics]` and **requires** at least a minor version bump. See the [Metric Compatibility Policy](docs/metrics.md#メトリクス互換性ポリシー) for which changes count as metric-definition changes and the full release procedure.

## [Unreleased]

### Added

- **[metrics]** DOTS/ECS smell detectors UNI024 (`MissingBurstCompile` on `ISystem`/`IJobEntity`/`IJobChunk` structs) and UNI025 (`ManagedReferenceInComponentData` on `struct IComponentData`); per-assembly `burstCoverage` and `ecsTypeCount` metrics when ECS types exist; `metricsVersion` stays 3 ([#133](https://github.com/bigdra50/unilyze/issues/133))
- Finding `id` on every code smell in JSON output (byte-equal to SARIF `unilyzeFingerprint/v1`), `unilyze triage` subcommand (`set`/`list`/`prune`) persisting verdicts to `.unilyze/triage.json`, and per-smell `triage` field when verdicts apply ([#126](https://github.com/bigdra50/unilyze/issues/126))
- `query` evidence packs include finding `id`; `--triage`/`--no-triage` CLI flags and optional `"triage"` config key ([#126](https://github.com/bigdra50/unilyze/issues/126))
- **[metrics]** `false-positive` and `wontfix` triage verdicts are excluded from badge/statusline gates and diff smell regressions; `false-positive` is also excluded from trend `codeSmellCount`; `wontfix` remains visible in trend as accepted debt ([#126](https://github.com/bigdra50/unilyze/issues/126))
- **[metrics]** Hotspot upgrades: bot-commit exclusion (default on, `--no-bot-filter` to disable), optional time-decay weighting (`--half-life <N.unit>`), and method-level X-Ray (`--methods <file>`); hotspot JSON has no `metricsVersion` field and `metricsVersion` stays 3, but default bot filtering changes hotspot rankings on repos with bot traffic ([#128](https://github.com/bigdra50/unilyze/issues/128))
- `trend -f html` / `-o trend.html`: self-contained HTML output with inline-SVG charts for CodeHealth avg/min, warning/critical smell counts, and type metrics; snapshot `sourceFile` provenance, metricsVersion/profile crossing markers, and a two-click diff command builder ([#129](https://github.com/bigdra50/unilyze/issues/129))
- Built-in `unity` smell-threshold profile with SATT-style role-aware thresholds (`MonoBehaviour`, `ScriptableObject`, `EditorExtension`, `PlainCSharp`) selectable via `"profile": "unity"` in `.unilyze.json` or `--profile unity`; user `smells` overrides take precedence over profile defaults ([#87](https://github.com/bigdra50/unilyze/issues/87))
- Per-type `role` field and optional `informationalCount` on `typeMetrics` (unity profile records `LowCohesion` as informational instead of a warning smell, per Palomba ICSME 2014); root `profile` field when a non-default profile is active; `diff`/`trend` warn on profile mismatch ([#87](https://github.com/bigdra50/unilyze/issues/87))
- MinVer-based versioning: git tags are the single source of truth for package, assembly, and CLI version output; semver tag push now creates a GitHub Release with the matching `CHANGELOG.md` section as its body ([#93](https://github.com/bigdra50/unilyze/issues/93))
- `statusline --background-refresh` returns cached output immediately and refreshes stale or missing caches in a detached background process, enabling a cross-platform one-line status line integration without shell-side cache logic ([#92](https://github.com/bigdra50/unilyze/issues/92))
- `calibrate` subcommand: derive smell-threshold candidates from two or more unilyze JSON snapshots using Alves, Ypma & Visser (ICSM 2010) LOC-weighted pooling and 70/80/90 percentiles (80/90/95 for parameter count); outputs risk bands and a `.unilyze.json` smells fragment without changing built-in defaults ([#86](https://github.com/bigdra50/unilyze/issues/86))
- `diff --base-ref <git-ref> <after.json>` analyzes the base ref in a temporary git worktree and diffs against the after snapshot in one command; composes with `-f markdown`, `--fail-on-regression`, and `--changed-only` ([#82](https://github.com/bigdra50/unilyze/issues/82))
- Baseline workflow for brownfield quality gates: `unilyze baseline create` snapshots current smells into `.unilyze/baseline.json`, and `--baseline <file>` suppresses matched fingerprints at analysis time so reports and gates see only newly introduced violations ([#81](https://github.com/bigdra50/unilyze/issues/81))
- Inline suppression comments (`// unilyze-disable`, `// unilyze-disable-next-line`) to silence individual justified occurrences; suppressed smells remain in JSON/SARIF with `"suppressed": true`, merge into root `suppressedCount`, and are excluded from statusline, badge, and diff gating ([#127](https://github.com/bigdra50/unilyze/issues/127))
- Per-smell `baselined` JSON field, root `suppressedCount`, SARIF `suppressions` for baselined results, and optional `"baseline"` path in `.unilyze.json` ([#81](https://github.com/bigdra50/unilyze/issues/81))
- HTML viewer search UX: auto-expands collapsed namespaces for type hits, `/` and `Escape` keyboard shortcuts, and quick-filter chips (low health, smells, cycles) ([#75](https://github.com/bigdra50/unilyze/issues/75))
- HTML viewer bundles Cytoscape.js, dagre, and cytoscape-dagre for full offline graph support; ELK layout remains CDN-only with dagre fallback ([#74](https://github.com/bigdra50/unilyze/issues/74))
- `THIRD-PARTY-NOTICES.txt` at repo root documenting bundled MIT libraries and CDN-only dependencies ([#74](https://github.com/bigdra50/unilyze/issues/74))
- Per-smell threshold overrides (`smells`) and per-rule enable/disable toggles (`rules`) in `.unilyze.json` ([#71](https://github.com/bigdra50/unilyze/issues/71)); maps merge key-wise across global and project config
- WeakTemporization smell (UNI017): flags incremental `transform` mutations in Unity `Update`/`LateUpdate` without delta-time scaling ([#79](https://github.com/bigdra50/unilyze/issues/79))
- **[metrics]** WeakTemporization (UNI017) reported as Warning on Unity `Update`/`LateUpdate` transform mutations lacking delta-time scaling; `metricsVersion` stays 3 (folds into the #70 release-window bump) ([#79](https://github.com/bigdra50/unilyze/issues/79))
- Diff overlay now works in graph mode: changed types get bucket halos, the tap panel shows baseline sections, and `Changed only` filters graph nodes ([#73](https://github.com/bigdra50/unilyze/issues/73))
- `diff --changed-only` filters JSON output to changed type buckets (added/removed/degraded/improved) plus the aggregate summary, omitting unchanged types ([#38](https://github.com/bigdra50/unilyze/issues/38))
- `diff -f markdown` output for PR comments and GITHUB_STEP_SUMMARY ([#37](https://github.com/bigdra50/unilyze/issues/37))
- SARIF and JSON code smells now include the source line number of each occurrence ([#36](https://github.com/bigdra50/unilyze/issues/36))
- `metricsVersion` and `toolVersion` on JSON output root ([#30](https://github.com/bigdra50/unilyze/issues/30)): `metricsVersion` tracks metric-definition compatibility; `toolVersion` records the unilyze assembly version; `diff` / `trend` warn on cross-version comparisons; `diff --fail-on-version-mismatch` exits 2 for CI gates
- `SmellThresholds` registry as single source for code-smell detection thresholds ([#32](https://github.com/bigdra50/unilyze/issues/32)); drift-guard test keeps `docs/metrics.md` in sync
- Default exclude directories (`Library/`, `Temp/`, `obj/`, `bin/`, `.git/`, `Logs/`, `UserSettings/`) for `.cs` enumeration and asmdef discovery, with `disableDefaultExcludes` and `disableGeneratedCodeExcludes` escape hatches in `.unilyze.json` ([#31](https://github.com/bigdra50/unilyze/issues/31))
- **[metrics]** Two async anti-pattern smells: `AsyncVoidMethod` (async void methods, excluding Unity message methods and event-handler signatures) and `BlockingTaskWait` (`.Result` / `.Wait()` / `.GetAwaiter().GetResult()` on `Task`/`ValueTask`/`UniTask`; SyntaxOnly reports only the `GetAwaiter().GetResult()` chain) ([#80](https://github.com/bigdra50/unilyze/issues/80))
- **[metrics]** UNI017–UNI020 Unity hot-path detectors: expensive Unity API (GetComponent, Find, Camera.main), LINQ, collection/array allocation, and string concatenation inside MonoBehaviour per-frame methods (`Update`, `FixedUpdate`, `LateUpdate`, `OnGUI`, coroutines); default Warning severity ([#78](https://github.com/bigdra50/unilyze/issues/78))
- SARIF enrichment for GitHub Code Scanning: rule `help`/`helpUri`/`properties.tags`, result `partialFingerprints` (`unilyzeFingerprint/v1`), and region `endLine`; CI uploads self-analysis SARIF via `upload-sarif` ([#83](https://github.com/bigdra50/unilyze/issues/83))
- `statusline --verbose` prints the previously-swallowed analysis exception to stderr, and `statusline --quiet` suppresses info lines while keeping warnings; `AnalysisPipeline` now shows per-phase progress on stderr when stderr is a TTY ([#76](https://github.com/bigdra50/unilyze/issues/76))
- `projectKind` field on JSON output root (`unity` | `dotnet` | `unknown`) and Unity-agnostic `analysisLevel` stage names (`Syntax`/`Core`/`Full`/`Complete`); `--level syntax|core|full|complete` CLI tokens unchanged ([#72](https://github.com/bigdra50/unilyze/issues/72))
- **[metrics]** Inject hosting .NET runtime BCL references (`TRUSTED_PLATFORM_ASSEMBLIES`) for non-Unity projects so boxing/CBO/DIT semantic metrics populate without MSBuild/Buildalyzer; non-Unity `analysisLevel` reports `Complete` instead of `SyntaxOnly`; `metricsVersion` stays 3 (folds into the #70 release-window bump) ([#84](https://github.com/bigdra50/unilyze/issues/84))
- **[metrics]** Map discovered `.csproj` files to one assembly each (with `ProjectReference` dependency edges) for general .NET solutions without `.asmdef`; assembly-level Abstractness/Instability/DfMS and assembly cycle detection now apply per project; `typeId` assembly prefix changes on affected repos (e.g. `Assembly-CSharp::` → `{ProjectName}::`); Unity (asmdef) and no-csproj paths unchanged; `metricsVersion` stays 3 (folds into the #70 release-window bump) ([#91](https://github.com/bigdra50/unilyze/issues/91))
- `query` subcommand: per-type evidence packs (metrics, smells with `file:line` anchors, dependencies, top methods) as token-efficient Markdown or compact JSON; supports `-i` snapshot input or `-p` direct analysis ([#85](https://github.com/bigdra50/unilyze/issues/85))
- Official composite GitHub Action ([action.yml](action.yml)): installs Unilyze, runs analysis, badge gates, optional `base-ref` diff summary, and optional SARIF generation with outputs for downstream upload ([#88](https://github.com/bigdra50/unilyze/issues/88))

### Changed

- Dropped `net9.0` (STS, EOL) from supported target frameworks; supported runtimes are now `net8.0` and `net10.0` per the .NET version support policy ([#43](https://github.com/bigdra50/unilyze/issues/43)). **Global-tool impact:** environments with only a .NET 9 runtime can no longer run the tool; install a .NET 8 or .NET 10 runtime instead.
- **[metrics]** HighCoupling warning threshold raised from CBO >= 14 to CBO >= 15, aligning code with the documented contract ([#32](https://github.com/bigdra50/unilyze/issues/32))
- **[metrics]** DeepInheritance warning threshold lowered from DIT >= 6 to DIT >= 5, aligning code with the documented contract ([#32](https://github.com/bigdra50/unilyze/issues/32))
- **[metrics]** LowMaintainability warning threshold raised from MI < 20 to MI < 60; LowMaintainability no longer has a Critical tier (docs never defined one) ([#32](https://github.com/bigdra50/unilyze/issues/32))
- **[metrics]** Default excludes and generated-code filtering (root-level `Library/`, `Temp/`, `Logs/`, `UserSettings/`; any-depth `obj/`, `bin/`, `.git/`; `<auto-generated>`, `.g.cs`, `.generated.cs`) plus nearest-asmdef ownership reduce parsed source scope; type counts and derived metrics may decrease ([#31](https://github.com/bigdra50/unilyze/issues/31))
- **[metrics]** Allocation detection (boxing, closure capture, params array) now scans property/indexer accessors, operators, conversion operators, local functions, and field/property initializers in addition to methods and constructors; counts may increase ([#35](https://github.com/bigdra50/unilyze/issues/35))
- Unknown subcommands and options are now usage errors: one-line stderr message, exit `1`, and a `Did you mean '...'?` suggestion for near-misses (previously `config`/`skills` exited `0` and misspelled flags were silently ignored)
- **[metrics]** Boxing, ClosureCapture, and ParamsArrayAllocation smells inside Unity hot-path methods (`Update`, `FixedUpdate`, `LateUpdate`, `OnGUI`, and coroutines returning `IEnumerator`) of `MonoBehaviour`-derived types now escalate from Warning to Critical; `metricsVersion` bumped 2→3 ([#70](https://github.com/bigdra50/unilyze/issues/70)). **BREAKING:** Unity projects with per-frame allocations in those methods will newly fail `badge --metric smells` (any Critical smell is an instant fail regardless of `--fail-over`) and show a red statusline badge; opt out by pinning the previous minor version.

### Fixed
- `skills install` was a silent no-op in Windows-built binaries: embedded skill resource names use the build OS's path separator and the backslash form failed to parse ([#42](https://github.com/bigdra50/unilyze/issues/42))
- `statusline --help` output-format description now matches the actual output (min CodeHealth, MI, boxing, and cycles tokens were missing; smell marker was wrong)

## [0.3.0] - 2026-06-08

First release since v0.2.2. Minor bump because several metric definitions changed.

### Added

- Analysis levels ([#16](https://github.com/bigdra50/unilyze/issues/16), [#17](https://github.com/bigdra50/unilyze/issues/17)): resolved level (`SyntaxOnly` / `CoreEngine` / `FullEngine` / `Complete`) reported on stderr, JSON (`analysisLevel`), badge endpoint JSON/SVG, and statusline marker; `--level <syntax|core|full|complete>` pins the level deterministically and exits non-zero when it cannot be satisfied
- Quality gates ([#18](https://github.com/bigdra50/unilyze/issues/18)): `unilyze badge --fail-under` (codehealth/mi) and `--fail-over` (smells); `unilyze diff --fail-on-regression`; exit codes `0` pass, `1` usage error, `2` gate failed
- DI graph integration ([#19](https://github.com/bigdra50/unilyze/issues/19)): VContainer/Zenject registration edges resolved to TypeIds and integrated into the dependency graph, cycle detection, CBO/Ca/Ce, and TypeRank
- `badge` subcommand with shields.io endpoint JSON output and `--format svg` for self-contained SVG badges (including private repositories)
- Metric compatibility policy in `docs/metrics.md` and release checklist ([#20](https://github.com/bigdra50/unilyze/issues/20))
- Official Microsoft.CodeAnalysis Metrics cross-validation harness (`scripts/crossval`)
- CI workflow to publish self-analysis badges to the `badges` branch
- Badges section and metric validation results in documentation

### Changed

- **[metrics]** Deconstruction `foreach` is now counted in cyclomatic complexity, cognitive complexity, and nesting depth (values may increase)
- **[metrics]** Method-less types are excluded from the average MI denominator (average MI may rise)
- **[metrics]** Removed the `I[A-Z]` DIT heuristic
- Dogfooding badges switched to `--format svg`
- Exclude test projects from self-analysis metrics
- Multiple internal refactors (TypeNameFormat, MemberExtractor, BaseTypeResolver, SemanticEnricher, CycleDetector, ClosureDetector, DI container resolvers, RankCalculator, TypeAnalyzer, AnalysisPipeline, HTML viewer embedded resource)

### Fixed

- **[metrics]** `when`-filtered catch clauses are no longer flagged as `CatchAllException` (fewer false positives)
- `--level syntax` pin no longer silently re-elevates via csproj references
- Badge gate fails closed when the metric is unavailable
- Badge gate rejects value-less flags instead of skipping the gate
- `--fail-on-regression` included in `diff` inline usage string
- DI registration endpoints resolved to TypeIds for graph integration

## [0.2.2] - 2026-05-19

### Added

- Interactive HTML viewer for `unilyze diff` output
- Documentation for the HTML diff viewer

## [0.2.1] - 2026-03-25

### Added

- `--exclude-dir` option and `.unilyze.json` configuration file for directory exclusion ([#1](https://github.com/bigdra50/unilyze/issues/1))
- `config` subcommand (`list` / `add-exclude-dir` / `remove-exclude-dir`)

### Changed

- Added documentation for the `statusline` subcommand

## [0.2.0] - 2026-03-23

### Added

- `statusline` subcommand: compact one-line code health summary for editor status lines, with per-project caching and file-based locking

### Changed

- Projects with no analyzable types return empty output (no `CH:0.0` displayed)
- Statusline cache key hash changed from SHA256 to MD5 for faster shell-side computation

## [0.1.9] - 2026-03-16

New analyzers and calculators; documentation updates and expanded test coverage.

## [0.1.8] - 2026-03-16

Performance improvements: parallel file parsing, CyclomaticComplexity walker rewrite, and Halstead single-pass calculation.

## [0.1.7] - 2026-03-16

Default graph layout changed from Bezier/Dagre to ELK.

## [0.1.6] - 2026-03-16

README updates for `metrics`/`schema` subcommands, skill installation, and CodeSmell thresholds.

## [0.1.5] - 2026-03-16

Fix crash from Roslyn internal exception (NullableWalker NRE).

## [0.1.4] - 2026-03-16

Add `metrics` and `schema` subcommands for agent-oriented self-contained help; metrics comparison validation report.

## [0.1.3] - 2026-03-16

Fix NestingDepth over-counting on `else if` chains; add demo video and NuGet version badge to docs.

## [0.1.2] - 2026-03-15

Fix `hotspot` default `-p` to current directory; auto-trigger NuGet publish on `v*` tag push; docs improvements.

## [0.1.1] - 2026-03-15

Initial public release.

[Unreleased]: https://github.com/bigdra50/unilyze/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/bigdra50/unilyze/compare/v0.2.2...v0.3.0
[0.2.2]: https://github.com/bigdra50/unilyze/compare/v0.2.1...v0.2.2
[0.2.1]: https://github.com/bigdra50/unilyze/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/bigdra50/unilyze/compare/v0.1.9...v0.2.0
[0.1.9]: https://github.com/bigdra50/unilyze/compare/v0.1.8...v0.1.9
[0.1.8]: https://github.com/bigdra50/unilyze/compare/v0.1.7...v0.1.8
[0.1.7]: https://github.com/bigdra50/unilyze/compare/v0.1.6...v0.1.7
[0.1.6]: https://github.com/bigdra50/unilyze/compare/v0.1.5...v0.1.6
[0.1.5]: https://github.com/bigdra50/unilyze/compare/v0.1.4...v0.1.5
[0.1.4]: https://github.com/bigdra50/unilyze/compare/v0.1.3...v0.1.4
[0.1.3]: https://github.com/bigdra50/unilyze/compare/v0.1.2...v0.1.3
[0.1.2]: https://github.com/bigdra50/unilyze/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/bigdra50/unilyze/releases/tag/v0.1.1

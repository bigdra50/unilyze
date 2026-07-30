# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [metrics] tag convention

Any changelog entry that changes a computed metric value **must** be prefixed with `[metrics]` and **requires** at least a minor version bump. See the [Metric Compatibility Policy](docs/metrics.md#metric-compatibility-policy) for which changes count as metric-definition changes and the full release procedure.

## [Unreleased]

## [0.6.1] - 2026-07-31

### Changed

- The Roslyn analysis engine is upgraded from 5.3 to 5.6 (`Microsoft.CodeAnalysis.CSharp`). Measured metrics are unchanged on the golden corpus ([#241](https://github.com/bigdra50/unilyze/pull/241))

### Fixed

- `unilyze statusline` no longer blocks on a cold or expired cache: it serves the cached line at any age instantly (or prints nothing when no cache exists yet) and refreshes in a detached background process with a stampede lock and an atomic cache rewrite. Consumers with tight render budgets previously killed the synchronous analysis before the cache was written, so the segment never appeared ([#242](https://github.com/bigdra50/unilyze/pull/242))

## [0.6.0] - 2026-07-07

### Changed

- `unilyze serve` re-analyzes warm edits incrementally at the resolved semantic level: a source edit re-enriches only the changed types, while a structural change (signature / type-set / global-using change, or a file add/delete) re-enriches everything. The dependency graph and global aggregation are always rebuilt full, so the live snapshot is byte-identical to a full analysis. `--incremental` now accelerates every analysis level, not just `--level syntax` ([#216](https://github.com/bigdra50/unilyze/issues/216))
- `unilyze serve`'s incremental analysis now invalidates precisely on signature and using-directive changes: a reverse dependency index (RDeps) inverted from each type's recorded resolved usage (one IOperation walk per re-enriched type) re-enriches only the changed type and its actual dependents, instead of every type. Member add/remove, base/interface changes, type/file add/delete, and global-using changes keep the conservative full re-enrich; a collapse threshold falls back to full when precision stops paying. A structural edit on the self-corpus re-enriches 2/14 types instead of 14/14 ([#224](https://github.com/bigdra50/unilyze/pull/224))
- The Roslyn analysis engine is upgraded from 4.12 to 5.3 (`Microsoft.CodeAnalysis.CSharp` / `Workspaces`), picking up parser support for the newest C# language versions. Measured metrics are unchanged on the golden corpus ([#230](https://github.com/bigdra50/unilyze/pull/230))
- **[metrics]** `metricsVersion` is now 5, pairing the overload-CycCC correction ([#223](https://github.com/bigdra50/unilyze/pull/223)) with the increment the Metric Compatibility Policy requires; warm incremental caches from earlier versions invalidate automatically
- `unilyze serve`'s incremental analysis now invalidates precisely on member add/remove and base/interface-list changes too: adding or removing a member re-enriches only the changed type and the types that resolved it or its inheritance/interface-implementation descendants (RDeps(B ∪ InhDesc(B))), and a base/interface-list change additionally re-enriches those descendants directly. A member-set change on a static class (where an added/removed member could be an extension method) still falls back to a full re-enrich, as do type/file add/delete and global-using changes. An optional `serve --verify-incremental <N>` shadow-verification mode runs a full analysis alongside the incremental one every N generations and logs any divergence, for dogfooding the invalidation rules without paying the cost on every edit.

### Fixed

- **[metrics]** Semantic cyclomatic-complexity recalculation now binds each overloaded method to its own declaration instead of the first match by name; a complex overload's CycCC no longer collapses to a simpler overload's value. Affects `cyclomaticComplexity` / `maxCyclomaticComplexity` / `averageCyclomaticComplexity` for any type with overloaded methods ([#223](https://github.com/bigdra50/unilyze/pull/223))
- `badge` no longer runs a redundant full project analysis per invocation; `BadgeRunner.Run` computed and discarded a second `AnalysisPipeline.Build`, so every badge run analyzed the project twice. Badge output is unchanged ([#223](https://github.com/bigdra50/unilyze/pull/223))

## [0.5.3] - 2026-06-16

### Added

- `unilyze serve` pans to and highlights the type blocks whose source changed on each live update: after an edit re-analyzes, the viewer expands ancestors, fits the changed nodes, and pulses an amber `.hl-changed` halo. Changed source files are mapped to opaque fileIds via the `X-Unilyze-Changed-FileIds` snapshot header; the initial load and config / `.meta` / `.csproj` edits never trigger a spurious focus ([#221](https://github.com/bigdra50/unilyze/pull/221))

### Fixed

- `unilyze serve` now opens the live viewer in a browser. `TryOpenInBrowser` no longer rewrites the loopback `http://127.0.0.1:PORT/` URL into a bogus `file://` path (broken since the serve MVP [#202](https://github.com/bigdra50/unilyze/issues/202)); absolute http/https URLs pass through unchanged while the local HTML paths used by analyze/trend/diff keep the `file://` behavior ([#221](https://github.com/bigdra50/unilyze/pull/221))

## [0.5.2] - 2026-06-15

### Fixed

- `SourceLocation.FileRef` now correctly indexes each member's source file instead of always being 0; partial type members from different files get distinct FileRef values ([#220](https://github.com/bigdra50/unilyze/pull/220)). Closes [#204](https://github.com/bigdra50/unilyze/issues/204)
- SourceTable paths sorted by ordinal comparison for stable FileRef across incremental and full runs
- Incremental test comparison normalizes FileRef to path strings to absorb parallel parse ordering differences

## [0.5.1] - 2026-06-14

### Added

- Search stepper navigation: `◀ N / M ▶` control next to filter chips steps through individual search matches with zoom-to-node, detail panel open, and automatic namespace expansion for collapsed targets ([#219](https://github.com/bigdra50/unilyze/pull/219))
- Search highlight styles: `.hl` (blue border for all matches), `.hl-focus` (gold border + overlay for current stepper target), and ancestor compound un-dimming so type nodes inside namespaces are fully visible ([#219](https://github.com/bigdra50/unilyze/pull/219))

## [0.5.0] - 2026-06-14

### Added

- Source-position model: `SourceLocation` (FileRef, StartLine, EndLine) on every member kind — field, property, event, indexer, constructor, destructor, operator, and conversion operator; previously only methods carried positions. `Declarations[]` on types aggregates partial declaration locations. `SourceTable` provides integer-indexed path indirection to bound JSON growth. `SchemaVersion` field added to `AnalysisResult` ([#206](https://github.com/bigdra50/unilyze/issues/206))
- Syntax-primary `memberId` for all member kinds: stable, purely syntactic identifier (`{typeId}|{kind}:{signature}`) covering overloads, generics, explicit-interface members, constructors, operators, and conversion operators; identical between SyntaxOnly and semantic analysis runs ([#207](https://github.com/bigdra50/unilyze/issues/207))
- `MethodChangeKind` (Added/Removed/Changed) on `MethodDiff`, separate from the quality-level `ChangeStatus`; methods paired by `memberId` with fallback to `name:paramCount` for old snapshots ([#208](https://github.com/bigdra50/unilyze/issues/208))
- `GitDiffService` for working-tree vs HEAD text diff with explicit unborn/untracked/deleted/no-repo state handling, async process execution with cancellation and 512KB byte cap; `GET /api/diff?fileId=` endpoint on serve ([#211](https://github.com/bigdra50/unilyze/issues/211))
- `HeadAnalysisService` with OID-keyed HEAD analysis cache, independent OID polling (watcher excludes `.git`), and level-match gating for metric badges ([#212](https://github.com/bigdra50/unilyze/issues/212))
- `deltaScore` surfaced as a standalone quality-risk indicator in `/api/state` (score + low/high risk counts), separate from line-level diff classification ([#213](https://github.com/bigdra50/unilyze/issues/213))
- Per-generation 6-stage measurement breakdown (analysis/sanitize/serialize server-side) for performance-driven optimization gating ([#205](https://github.com/bigdra50/unilyze/issues/205))
- README screenshots: type dependency graph, in-browser source viewer, and diff viewer with degradation halos

### Changed

- Re-added `net9.0` as a supported target framework alongside `net8.0` and `net10.0`, reversing the 0.4.0 drop. The .NET version support policy now targets every TFM from the oldest supported LTS up to the latest LTS with no gaps; EOL is no longer an exclusion criterion, and the floor LTS is raised only once it reaches EOL ([#43](https://github.com/bigdra50/unilyze/issues/43)). **Global-tool impact:** the tool again runs on a .NET 9 runtime.
- Diff matching (`CountChangedMethods`, `ComputeSmellChanges`) unified on `memberId` with multiset comparison; multiple same-kind findings in one member are now individually tracked instead of collapsed ([#209](https://github.com/bigdra50/unilyze/issues/209))
- Finding fingerprint v2 (memberId-based) with backward-compatible triage matching via `LegacyId`; baseline entries gain optional `MemberId` field; no existing triage verdicts or baseline ratchets are stranded ([#210](https://github.com/bigdra50/unilyze/issues/210))

## [0.4.1] - 2026-06-13

### Added

- NuGet package icon (`PackageIcon`) so the package displays an icon on nuget.org ([#183](https://github.com/bigdra50/unilyze/pull/183))

## [0.4.0] - 2026-06-13

### Added

- Generated documentation pages for all SARIF rules, per-rule SARIF `helpUri` links, and drift guards that require documentation for new detectors ([#158](https://github.com/bigdra50/unilyze/issues/158))
- GitHub Pages expands from the self-analysis demo to an mkdocs-material documentation site; the demo moves to `/demo/` ([#158](https://github.com/bigdra50/unilyze/issues/158))
- **[metrics]** CodeHealth v2 replaces the six-factor compensatory weighted sum with calibrated, saturating penalties over complexity, size, and interface axes; types previously carried by clean factors can drop when one axis is severe, while types with several correlated mediocre factors no longer pay duplicate penalties. JSON now emits `codeHealthCategory`, one-release `codeHealthV1`, and LoC-weighted/worst-decile assembly aggregates; badge/statusline show the new aggregates and color by the LoC-weighted Healthy/Warning/Alert boundary. `--codehealth-v1` on badge/statusline/diff preserves migration-time display and gate behavior and will be removed with `codeHealthV1` in the next minor release. `metricsVersion` bumps 3→4 for the Phase 3 release window ([#155](https://github.com/bigdra50/unilyze/issues/155))
- **[metrics]** Unity `energyPressure` static proxy (hot-path performance smells per hot-path method), `badge --metric energy --fail-over`, and trend JSON/table/HTML series; independent of CodeHealth, additive JSON fields only, and `metricsVersion` unchanged by this feature (4 ships with this release via [#155](https://github.com/bigdra50/unilyze/issues/155)) ([#157](https://github.com/bigdra50/unilyze/issues/157))
- HTML viewer large-graph mode lazily materializes type nodes and dependency edges from analysis data, computes namespace meta-edges without hidden Cytoscape elements, and runs ELK through a Web Worker with preset-coordinate application; dagre and static-report fallbacks remain intact ([#162](https://github.com/bigdra50/unilyze/issues/162))
- DMM-style `deltaScore` on diff JSON and Markdown output classifies changed methods and types by their post-change complexity, nesting, and size risk; `--fail-on-delta-below <0..1>` exits `2` when the low-risk change ratio misses the gate; based on di Biase et al. (TechDebt 2019), with `metricsVersion` unchanged ([#154](https://github.com/bigdra50/unilyze/issues/154))
- Self-contained single-file binaries for macOS arm64/x64, Linux x64, and Windows x64, with GitHub Release checksums plus Homebrew and Scoop installation channels that require no .NET SDK or runtime ([#153](https://github.com/bigdra50/unilyze/issues/153))
- Security policy with private vulnerability reporting guidance, an HTML viewer threat model, XSS regression coverage for untrusted analysis values, and NuGet Trusted Publishing migration instructions ([#160](https://github.com/bigdra50/unilyze/issues/160))
- **[metrics]** `unilyze dup` subcommand: Roslyn token-stream normalization (identifiers/literals collapsed), Rabin-Karp rolling-hash clone detection (default 100-token window), Markdown/JSON report, third-party same-directory suppression, and `badge --metric dup --fail-over <percent>` CI gate; independent of main analysis output — `metricsVersion` unchanged by this feature (4 ships with this release via [#155](https://github.com/bigdra50/unilyze/issues/155)) ([#130](https://github.com/bigdra50/unilyze/issues/130))
- Opt-in `--resolve-nuget` (or `"resolveNuget": true` in `.unilyze.json`) resolves NuGet package compile-time assemblies from `obj/project.assets.json` and merges them with BCL runtime references; semantic metrics (CBO, DIT, boxing) can increase versus the default run — `metricsVersion` unchanged by this feature (4 ships with this release via [#155](https://github.com/bigdra50/unilyze/issues/155)) ([#136](https://github.com/bigdra50/unilyze/issues/136))
- Opt-in `--include-generated` (or `"includeGenerated": true`) injects `EmitCompilerGeneratedFiles` outputs from `obj/<Config>/<TFM>/generated/**/*.cs` into the Roslyn compilation only (excluded from type counts, smells, and LineCount); requires a single deterministic TFM selection (`--tfm` or csproj `TargetFramework(s)` first) ([#136](https://github.com/bigdra50/unilyze/issues/136))
- JSON output echoes enabled reference-analysis opt-ins (`resolveNuget`, `includeGenerated`, `targetFramework`) when non-default; `diff` warns when opt-in settings differ between snapshots ([#136](https://github.com/bigdra50/unilyze/issues/136))
- `--incremental` flag for syntax-level analysis: persists a per-file content-hash cache under `<project>/.unilyze/cache/syntax/v1/` and re-parses only changed files; semantic levels (core/full/complete) ignore the flag with a stderr note; `metricsVersion` unchanged by this feature (4 ships with this release via [#155](https://github.com/bigdra50/unilyze/issues/155)) ([#135](https://github.com/bigdra50/unilyze/issues/135))
- **[metrics]** Unity scene/prefab/`.asset` YAML cross-check adds `SerializedReference` dependency edges from Inspector wiring of `[SerializeField]`/public fields to concrete component types; affects Ca/Ce/Instability/TypeRank/cycles/DfMS on Unity projects with text-serialized assets; CBO unchanged (declaration-based); `metricsVersion` unchanged by this feature (4 ships with this release via [#155](https://github.com/bigdra50/unilyze/issues/155)) ([#132](https://github.com/bigdra50/unilyze/issues/132))
- `unilyze mcp` stdio MCP server exposing ten agent tools (`analyze`, `get_summary`, `worst_types`, `query_type`, `diff`, `hotspot`, `baseline_status`, `triage_add`, `schema`, `version`) that wrap existing query/diff/hotspot/baseline/triage internals with token-compressed Markdown defaults and optional `max_chars` trimming; no new NuGet dependencies ([#131](https://github.com/bigdra50/unilyze/issues/131))
- `--projects <glob>` on analyze and badge for monorepo batch analysis: expands repeatable globs to project roots, writes per-project `AnalysisResult` JSON (or SARIF/badge files) plus `summary.json`, prints a per-project gate table on stderr, and aggregates exit code `2` when any gate fails ([#137](https://github.com/bigdra50/unilyze/issues/137))
- **[metrics]** DOTS/ECS smell detectors UNI024 (`MissingBurstCompile` on `ISystem`/`IJobEntity`/`IJobChunk` structs) and UNI025 (`ManagedReferenceInComponentData` on `struct IComponentData`); per-assembly `burstCoverage` and `ecsTypeCount` metrics when ECS types exist; `metricsVersion` unchanged by this feature (4 ships with this release via [#155](https://github.com/bigdra50/unilyze/issues/155)) ([#133](https://github.com/bigdra50/unilyze/issues/133))
- Opt-in `--include-api-surface` flag emits per-type API surface (XML doc presence/summary, public signature strings, identifier lists, doc coverage counts) in analyze JSON and `query` evidence packs; quality-audit skill adds a review-coverage phase (CRScore-style pseudo-reference matching) ([#134](https://github.com/bigdra50/unilyze/issues/134))
- Finding `id` on every code smell in JSON output (byte-equal to SARIF `unilyzeFingerprint/v1`), `unilyze triage` subcommand (`set`/`list`/`prune`) persisting verdicts to `.unilyze/triage.json`, and per-smell `triage` field when verdicts apply ([#126](https://github.com/bigdra50/unilyze/issues/126))
- Optional `"maxParallelism"` in `.unilyze.json` to cap `Parallel.ForEach` parse and semantic pre-warm concurrency (default: `Environment.ProcessorCount`); mitigates rc=134 (SIGABRT) OOM hypothesis during Complete-level analysis ([#62](https://github.com/bigdra50/unilyze/issues/62))
- `query` evidence packs include finding `id`; `--triage`/`--no-triage` CLI flags and optional `"triage"` config key ([#126](https://github.com/bigdra50/unilyze/issues/126))
- **[metrics]** `false-positive` and `wontfix` triage verdicts are excluded from badge/statusline gates and diff smell regressions; `false-positive` is also excluded from trend `codeSmellCount`; `wontfix` remains visible in trend as accepted debt ([#126](https://github.com/bigdra50/unilyze/issues/126))
- **[metrics]** Hotspot upgrades: bot-commit exclusion (default on, `--no-bot-filter` to disable), optional time-decay weighting (`--half-life <N.unit>`), and method-level X-Ray (`--methods <file>`); hotspot JSON has no `metricsVersion` field and this feature does not change `metricsVersion` (4 ships with this release via [#155](https://github.com/bigdra50/unilyze/issues/155)), but default bot filtering changes hotspot rankings on repos with bot traffic ([#128](https://github.com/bigdra50/unilyze/issues/128))
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
- **[metrics]** WeakTemporization (UNI017) reported as Warning on Unity `Update`/`LateUpdate` transform mutations lacking delta-time scaling; `metricsVersion` unchanged by this feature (4 ships with this release via [#155](https://github.com/bigdra50/unilyze/issues/155)) ([#79](https://github.com/bigdra50/unilyze/issues/79))
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
- **[metrics]** Inject hosting .NET runtime BCL references (`TRUSTED_PLATFORM_ASSEMBLIES`) for non-Unity projects so boxing/CBO/DIT semantic metrics populate without MSBuild/Buildalyzer; non-Unity `analysisLevel` reports `Complete` instead of `SyntaxOnly`; `metricsVersion` unchanged by this feature (4 ships with this release via [#155](https://github.com/bigdra50/unilyze/issues/155)) ([#84](https://github.com/bigdra50/unilyze/issues/84))
- **[metrics]** Map discovered `.csproj` files to one assembly each (with `ProjectReference` dependency edges) for general .NET solutions without `.asmdef`; assembly-level Abstractness/Instability/DfMS and assembly cycle detection now apply per project; `typeId` assembly prefix changes on affected repos (e.g. `Assembly-CSharp::` → `{ProjectName}::`); Unity (asmdef) and no-csproj paths unchanged; `metricsVersion` unchanged by this feature (4 ships with this release via [#155](https://github.com/bigdra50/unilyze/issues/155)) ([#91](https://github.com/bigdra50/unilyze/issues/91))
- `query` subcommand: per-type evidence packs (metrics, smells with `file:line` anchors, dependencies, top methods) as token-efficient Markdown or compact JSON; supports `-i` snapshot input or `-p` direct analysis ([#85](https://github.com/bigdra50/unilyze/issues/85))
- Official composite GitHub Action ([action.yml](action.yml)): installs Unilyze, runs analysis, badge gates, optional `base-ref` diff summary, and optional SARIF generation with outputs for downstream upload ([#88](https://github.com/bigdra50/unilyze/issues/88))

### Changed

- Internal source and test trees are organized into domain folders with matching namespaces; behavior and metric definitions are unchanged, while self-analysis namespace names and finding IDs change only for this repository ([#159](https://github.com/bigdra50/unilyze/issues/159))
- **BREAKING:** The default `statusline` format no longer includes the `MI:` token; pass `--show-mi` to restore the reference metric. CodeHealth is now the only default statusline metric, based on the validity concerns documented by van Deursen (2014), "Think Twice Before Using the Maintainability Index," and Borg et al. (2024), "Ghost Echoes Revealed" (arXiv:2408.10754). MI computation, JSON output, UNI008, badge behavior, and diff deltas are unchanged; `metricsVersion` remains 4 ([#156](https://github.com/bigdra50/unilyze/issues/156))
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

[Unreleased]: https://github.com/bigdra50/unilyze/compare/v0.4.1...HEAD
[0.4.1]: https://github.com/bigdra50/unilyze/compare/v0.4.0...v0.4.1
[0.4.0]: https://github.com/bigdra50/unilyze/compare/v0.3.0...v0.4.0
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

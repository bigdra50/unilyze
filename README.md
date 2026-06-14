# unilyze

[![CI](https://github.com/bigdra50/unilyze/actions/workflows/ci.yml/badge.svg)](https://github.com/bigdra50/unilyze/actions/workflows/ci.yml)
[![Code Health](https://raw.githubusercontent.com/bigdra50/unilyze/badges/codehealth.svg)](./docs/metrics.md)
[![NuGet](https://img.shields.io/nuget/v/Unilyze.svg)](https://www.nuget.org/packages/Unilyze)

**Free, zero-setup static analysis for Unity — agent-first by design.** unilyze runs on `.cs` files and `.asmdef` alone (no MSBuild/sln required), computes churn × complexity hotspots from git history, and ships skills plus a self-documenting CLI (`metrics`, `schema`, `query`) for AI coding workflows. General C# projects are supported via `.csproj` discovery and semantic analysis when a solution is present.

For build, test, and release information, see [README.dev.md](README.dev.md).

## Demo

<div><video controls src="https://github.com/user-attachments/assets/60ae2566-f961-4324-a16d-8f384b7d03fd" muted="false"></video></div>

![Type dependency graph with Code Health badges](https://github.com/user-attachments/assets/9191b866-55ca-46f2-9f1d-894859d9d020)

Live demo (unilyze analyzing its own source): <https://bigdra50.github.io/unilyze/demo/>

Documentation: <https://bigdra50.github.io/unilyze/>

### Requirements

- No .NET installation is required for Homebrew, Scoop, or direct-download binaries
- The `dotnet tool` channel requires .NET 8.0 or later

**.NET version support policy:** The `dotnet tool` channel targets every TFM from the oldest supported LTS up to the latest LTS, with no gaps. EOL is not an exclusion criterion; the floor LTS is raised only once it reaches EOL. As of 2026-06, supported TFMs are `net8.0`, `net9.0`, and `net10.0`.

## Quick Start

For users who already have .NET, the global tool remains the recommended installation:

```
dotnet tool install --global Unilyze
```

Install a self-contained binary without .NET:

```bash
# macOS or Linux
brew install bigdra50/tap/unilyze

# Windows
scoop install https://github.com/bigdra50/unilyze/releases/latest/download/unilyze.json
```

Release archives for `osx-arm64`, `osx-x64`, `linux-x64`, and `win-x64` are also available from [GitHub Releases](https://github.com/bigdra50/unilyze/releases).
Verify downloads with the attached `SHA256SUMS` file.
For a macOS archive downloaded through a browser, remove the quarantine attribute before the first run:

```bash
xattr -d com.apple.quarantine ./unilyze
```

Run in a Unity project directory to open the analysis results in your browser:

```bash
cd ~/MyUnityProject
unilyze
```

## Tutorials

Step-by-step walkthroughs for the highest-value workflows:

- [CI quality gate](./docs/tutorials/ci-quality-gate.md) — badge gates, `diff --fail-on-regression`, PR markdown comments, `--base-ref` baselines
- [Agent integration](./docs/tutorials/agent-integration.md) — `skills install`, evidence packs (`query`), refactor-loop and quality-audit workflows
- [Continuous refactoring](./docs/tutorials/continuous-refactoring.md) — `hotspot` prioritization, snapshot history, `trend` interpretation

## Usage

```bash
unilyze                                          # Analyze and open in browser
unilyze -p ~/MyUnityProject                      # Specify project path
unilyze -p ~/MyUnityProject -o graph.html        # Save HTML + JSON
unilyze -p ~/MyUnityProject -f json -o result.json  # JSON output
unilyze -p ~/MyUnityProject -f sarif -o report.sarif # SARIF (stable fingerprints)
unilyze -p ~/MyUnityProject --profile unity      # Unity role-aware smell thresholds
unilyze -p ~/MyUnityProject --baseline .unilyze/baseline.json  # Suppress known smells
unilyze -p ~/MyUnityProject --level core         # Pin analysis level
```

### Subcommands

```bash
unilyze config list                                # Show/manage configuration
unilyze baseline create -p .                       # Snapshot smells for zero-new-violations
unilyze diff <before.json> <after.json>            # Compare snapshots (JSON)
unilyze diff --base-ref origin/main after.json -f markdown --fail-on-regression
unilyze diff <before.json> <after.json> -o diff.html --changed-only
unilyze hotspot -p ~/MyUnityProject                # Git churn × complexity
unilyze dup -p ~/MyUnityProject                    # Token-normalized clone detection
unilyze trend <dir-of-jsons>                       # Quality trend across snapshots
unilyze trend <dir-of-jsons> -o trend.html         # Self-contained HTML trend charts
unilyze query --worst 5 -i snapshot.json           # Per-type evidence packs
unilyze calibrate <dir-of-jsons> -o thresholds.json  # Derive threshold candidates
unilyze statusline -p ~/MyUnityProject             # Compact summary for status line
unilyze badge -p ~/MyUnityProject -o badge.json    # shields.io endpoint JSON
unilyze metrics                                    # Metric definitions & thresholds
unilyze schema                                     # JSON field reference
unilyze skills install --claude --cursor           # Install agent skills
```

Run `unilyze --help` for all options. JSON output includes `projectKind` (`unity` | `dotnet`) and `profile`.

**Exit codes** (all commands): `0` success / gate passed, `1` usage error, `2` quality gate failed (`badge` with `--fail-under` / `--fail-over`, or `diff` with `--fail-on-regression`).

### Status Line Integration

`unilyze statusline` outputs a one-line code health summary (e.g. `CH:9.8/5.9 W:9.4 T:7.2 111smells 🔴1 📦66`). Pass `--show-mi` to append the reference MI metric. Use `--background-refresh` for non-blocking updates in Claude Code's status bar. Details: [docs/statusline.md](./docs/statusline.md).

### Badges

```bash
unilyze badge -p . --metric codehealth --fail-under 7   # CI gate example
unilyze badge -p . --metric energy --fail-over 1.0      # Unity hot-path smell density proxy
unilyze badge -p . --format svg -o .github/badges/codehealth.svg
```

The energy metric is a static source-code proxy, not measured energy or power.

See the [CI quality gate tutorial](./docs/tutorials/ci-quality-gate.md) and [docs/ci-integration.md](./docs/ci-integration.md) for endpoint vs SVG badges, quality-gate semantics, GitHub Actions, diff regression gates, and [monorepo `--projects`](./docs/ci-integration.md#monorepo) batch analysis.

#### Private repositories

Generate SVG with `unilyze badge --format svg`, commit under `.github/badges/`, and reference via a relative path — shields.io endpoints do not work in private repos. See [docs/ci-integration.md#private-repositories](./docs/ci-integration.md#private-repositories).

### GitHub Action

```yaml
- uses: bigdra50/unilyze@v1
  with:
    project-path: .
    metric: codehealth
    fail-under: "7.0"
    base-ref: origin/main
    fail-on-regression: "true"
    baseline: .unilyze/baseline.json
```

Full workflow YAML, input table, and `badges.yml` publishing pattern: [docs/ci-integration.md](./docs/ci-integration.md).

## Why unilyze

unilyze targets teams that want **commercial-grade metrics and agent workflows without Unity/MSBuild setup cost or per-seat licensing**. The table below compares four axes that matter for Unity game code and AI-assisted refactoring (pricing as of 2026-06 — verify on vendor sites before budgeting).

| | unilyze | NDepend | SonarQube | CodeScene | Qodana |
|---|---|---|---|---|---|
| **Price** | Free (MIT) | ~€399/seat/yr (Developer)[^ndepend] | ~$2,500/yr (Server Developer, 100K LOC)[^sonar] | from €18/active author/mo[^codescene] | €90/contributor/yr (Ultimate, 3-seat min)[^qodana] |
| **Unity setup** | Zero setup: `.cs`/`.asmdef` alone, Unity DLLs resolved progressively | VS solution / compiled assemblies required[^ndepend-feat] | MSBuild project required (SonarScanner for .NET)[^sonar] | Git repo + service onboarding; no Unity-specific analysis[^codescene-hs] | `.sln`/`.csproj` pre-generated (Rider sync script)[^qodana-unity] |
| **Churn × complexity hotspots** | `unilyze hotspot`, free; method-level via `--methods` | None (trend baselines only)[^ndepend-feat] | None ("Security Hotspot" is unrelated)[^sonar] | File-level; function-level in paid X-Ray[^codescene-hs] | None |
| **Agent integration** | Bundled skills (Claude/Codex/Cursor/Gemini/Windsurf), self-documenting CLI (`metrics`/`schema`/`query`), stable JSON; MCP on roadmap | [NDepend MCP][^ndepend-mcp] | [SonarQube MCP Server (GA)][^sonar-mcp] | [CodeScene MCP][^codescene-mcp] | None found in survey |

[^ndepend]: [NDepend purchase](https://www.ndepend.com/purchase)
[^ndepend-feat]: [NDepend features](https://www.ndepend.com/features)
[^sonar]: [SonarQube pricing in 2026 (dev.to)](https://dev.to/sonarsource/sonarqube-pricing-in-2026-community-developer-enterprise-and-cloud-costs-explained-4e8p)
[^codescene]: [CodeScene pricing](https://codescene.com/pricing)
[^codescene-hs]: [CodeScene hotspots](https://codescene.io/docs/guides/technical/hotspots.html)
[^qodana]: [Qodana pricing](https://www.jetbrains.com/help/qodana/pricing.html)
[^qodana-unity]: [Qodana Unity](https://www.jetbrains.com/help/qodana/unity.html)
[^ndepend-mcp]: [NDepend MCP](https://github.com/ndepend/ndepend-mcp)
[^sonar-mcp]: [SonarQube MCP Server](https://github.com/SonarSource/sonarqube-mcp-server)
[^codescene-mcp]: [CodeScene MCP Server](https://github.com/codescene-oss/codescene-mcp-server)

Free alternatives[^free-alt] (SonarQube Community Build, Qodana Community for .NET, Roslynator, Microsoft.CodeAnalysis.Metrics) lack Unity-specific hot-path detectors, asmdef-first discovery, and bundled agent skills.

[^free-alt]: See [Roslynator CLI](https://josefpihrt.github.io/docs/roslynator/cli) and vendor community editions; pricing links above are paid tiers.

## Analysis Levels

unilyze resolves an analysis level based on which Unity DLLs it can locate. Pin with `--level <syntax|core|full|complete>`. See [docs/ci-integration.md#analysis-levels-in-ci](./docs/ci-integration.md#analysis-levels-in-ci) for the full table and CI caveats.

### Incremental analysis

`--incremental` speeds up **syntax-level** runs by caching per-file parse and enrich results under `<project>/.unilyze/cache/syntax/v1/`. Only changed files are re-parsed on warm runs; cross-file aggregates (dependencies, coupling, cycles) are always recomputed so JSON output matches a full run (`metricsVersion` unchanged).

```bash
unilyze -p . --level syntax --incremental -f json -o result.json
```

Requirements and limits:

- Must be combined with `--level syntax`. Other levels print a one-line stderr warning and run the normal full pipeline with no cache I/O.
- Cannot be used with `-i/--input`.
- Cache invalidates when `toolVersion`, `metricsVersion`, preprocessor defines, thresholds/profile/rules, exclude dirs, or assembly layout change.
- Semantic-level incremental analysis (core/full/complete) is intentionally deferred: metrics such as DIT, boxing, and CBO depend on cross-file symbol resolution, so sound invalidation needs a dependency-closure design.
- CI: persist `.unilyze/cache/` with `actions/cache` keyed on lockfiles and `.unilyze.json` when using `--level syntax --incremental`.

The cache directory includes `.unilyze/cache/.gitignore` containing `*` (auto-created).

## Configuration

Settings merge additively from global config (`~/.config/unilyze/config.json`), project `.unilyze.json`, and CLI flags.

| Scope | Path |
|-------|------|
| Global | `$XDG_CONFIG_HOME/unilyze/config.json` |
| Project | `<project-root>/.unilyze.json` |
| CLI | `--exclude-dir <dir>` (repeatable) |

```jsonc
// .unilyze.json
{
  "excludeDirs": ["Assets/Plugins", "Assets/ThirdParty"],
  "profile": "unity",
  "smells": { "LongMethod": { "lines": 100, "criticalLines": 200 } },
  "rules": { "UNI011": "off", "UNI009": "off" }
}
```

`UNI009` off disables cyclic-dependency detection entirely. Other rule IDs map to smell kinds and filter JSON, SARIF, badge, and statusline output. Threshold keys are case-insensitive; defaults are in [docs/metrics.md](./docs/metrics.md) (drift-tested) and `unilyze metrics`.

### Reference analysis opt-ins (.NET)

For general .NET projects, semantic metrics (CBO, DIT, boxing) use BCL references by default. Optional flags deepen resolution without MSBuild:

```bash
unilyze -p . --resolve-nuget              # NuGet compile assemblies from obj/project.assets.json (after dotnet restore)
unilyze -p . --include-generated        # EmitCompilerGeneratedFiles output (compilation only; metrics unchanged)
unilyze -p . --resolve-nuget --tfm net8.0 # Pin target framework for multi-TFM repos
```

Equivalent `.unilyze.json` keys: `"resolveNuget"`, `"includeGenerated"`, `"targetFramework"`. Enabled settings are echoed in JSON output. Compare snapshots only with identical opt-in settings — see [docs/metrics.md](./docs/metrics.md#reference-analysis-opt-ins-nuget--generated-code).

### Suppressing findings

Three suppression mechanisms compose without double-counting:

| Mechanism | Scope | When to use |
|-----------|-------|-------------|
| Inline comment | Single occurrence | Justified one-off (intentional facade, measured-safe boxing site) |
| `"rules": { "UNI011": "off" }` | Rule-wide | Rule is noisy for the whole project |
| `--baseline` / `baseline create` | Project snapshot | Brownfield freeze; gate on new violations only |

Inline directives (ESLint-style):

```csharp
// unilyze-disable-next-line UNI014 -- top-level guard, intentional
catch { }

// unilyze-disable UNI002
void MeasuredLongMethod() { /* ... */ }
```

- `unilyze-disable-next-line` suppresses listed rules on the **following line** (detector smells with line numbers).
- `unilyze-disable` in the **leading trivia** of a type or method declaration suppresses listed rules for that declaration's scope.
- Omit rule IDs to suppress all rules in scope. Unknown rule IDs and `UNI009` print a stderr warning and are ignored.
- Suppressed smells stay in JSON with `"suppressed": true`, increment root `suppressedCount`, and appear in SARIF with `suppressions` `{ "kind": "inSource" }`. They are excluded from statusline, badge gates, and diff regression counts.

**Known constraints:** metric-based smells (UNI001–UNI008, UNI010) match directives by method or type name, so a directive on one overload suppresses the smell for all same-name overloads; detector smells (UNI011–UNI025) use line positions and distinguish overloads. For partial types, place type-scope directives on the declaration indexed by unilyze (see [docs/metrics.md](./docs/metrics.md)). `UNI009` (cyclic dependency) is config-only.

### Assembly mapping

| Project kind | Discovery | One assembly per |
|--------------|-----------|------------------|
| Unity | `.asmdef` under `Assets/` | asmdef `name` |
| .NET | `.csproj` (solution-first, else recursive) | csproj file name |
| Fallback | no asmdef / no csproj | single `Assembly-CSharp` |

`ProjectReference` items become assembly dependency edges. `--prefix` and `--assembly` filter assemblies the same way for asmdefs and csproj-derived names.

```bash
unilyze config add-exclude-dir Assets/Plugins
unilyze baseline create -p . -o .unilyze/baseline.json
```

## Metrics

| Metric | Description | Granularity |
|--------|-------------|-------------|
| [Cognitive Complexity](./docs/metrics.md#cognitive-complexity-cogcc) | SonarSource-compliant cognitive complexity | Method |
| [Cyclomatic Complexity](./docs/metrics.md#cyclomatic-complexity-cyccc) | McCabe 1976-compliant cyclomatic complexity | Method |
| [Halstead D/E/B](./docs/metrics.md#halstead-complexity-measures) | Difficulty, Effort, EstimatedBugs | Method |
| [LCOM-HS](./docs/metrics.md#lcom-hs-henderson-sellers) | Henderson-Sellers cohesion | Type |
| [WMC](./docs/metrics.md#wmc-weighted-methods-per-class) | Weighted Methods per Class | Type |
| [NOC / RFC / CBO / DIT](./docs/metrics.md) | Chidamber-Kemerer suite | Type |
| [Ca / Ce / Instability](./docs/metrics.md#ca--ce-afferent--efferent-coupling) | Martin package metrics | Type |
| [Maintainability Index](./docs/metrics.md#maintainability-index-mi) | Halstead Volume + CycCC + LoC | Method |
| [TypeRank](./docs/metrics.md#typerank) | PageRank-based importance | Type |
| [Code Health](./docs/metrics.md#code-health) | Composite score (1.0 worst – 10.0 best) | Type |
| [Abstractness / DfMS / Relational Cohesion](./docs/metrics.md) | Assembly-level metrics | Assembly |
| [Burst coverage / ECS type count](./docs/metrics.md#dots--ecs) | `[BurstCompile]` adoption on ECS systems/jobs | Assembly |

Run `unilyze metrics` for definitions and thresholds. See [docs/metrics.md](docs/metrics.md) for specifications and validation data.

## Detection capabilities

Metric-threshold smells (God Class, Long Method, coupling, cohesion, etc.), performance analysis (boxing, closures, params arrays), exception-flow patterns, Unity frame-rate rules (UNI017–UNI021: hot-path API/LINQ/allocation/string concat, weak temporization), async/blocking rules (UNI022–UNI023), DOTS/ECS rules (UNI024–UNI025: missing `[BurstCompile]`, managed `IComponentData` fields) with per-assembly `burstCoverage`, and DI container edge detection (VContainer, Zenject) — all configurable via `.unilyze.json` and `--profile unity`. Does not duplicate `com.unity.entities` source-generator diagnostics that fail the Editor build; see [docs/metrics.md#dots--ecs](./docs/metrics.md#dots--ecs). Thresholds are **not** duplicated here; see [docs/metrics.md](./docs/metrics.md#code-smell) and `unilyze metrics`.

## Output Formats

| Format | Use Case |
|--------|----------|
| `html` | Interactive dependency graph (lazy Cytoscape elements; dagre bundled; ELK Worker via CDN) |
| `json` | Agent integration, programmatic use |
| `sarif` | GitHub Code Scanning (stable fingerprints, rule help links) |

![In-browser read-only source viewer](https://github.com/user-attachments/assets/2b6754c5-3b12-4eb2-8b19-e7e0f2017a0c)

## Diff Viewer

`unilyze diff <before.json> <after.json> -o diff.html` overlays metric deltas on the standard viewer (change badges, `Changed only` toggle, graph halos). Regression gates, markdown PR output, and `--base-ref` workflows: [docs/ci-integration.md](./docs/ci-integration.md).

![Diff viewer with degradation halos and metric badges](https://github.com/user-attachments/assets/7651a23f-2413-405d-a7e5-61d0af0b66f7)

```bash
unilyze diff before.json after.json --fail-on-regression
unilyze diff --base-ref origin/main after.json -f markdown --changed-only
```

## Agent Workflow

See the [agent integration tutorial](./docs/tutorials/agent-integration.md).

```
unilyze (measure) → unilyze query (evidence) → fix → unilyze diff (verify)
```

```bash
unilyze query --worst 5 -i snapshot.json          # evidence packs (md or -f json)
unilyze calibrate snapshots/ -o calibration.json  # percentile thresholds for .unilyze.json
unilyze skills install --claude --codex --cursor
unilyze metrics && unilyze schema                 # self-documenting CLI
```

## Known Limitations

- HTML graph works offline (Cytoscape and dagre bundled). ELK layout runs in a CDN-loaded Worker, falls back to main-thread ELK if Worker startup fails, and uses dagre when ELK is unavailable.
- Large graphs initially materialize only namespace nodes and types in the initially expanded namespace. Type nodes and edges are added on expansion and removed on collapse.
- Windows is covered by CI (windows-latest, net10.0).

## License

MIT

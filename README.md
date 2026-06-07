# unilyze

[![CI](https://github.com/bigdra50/unilyze/actions/workflows/ci.yml/badge.svg)](https://github.com/bigdra50/unilyze/actions/workflows/ci.yml)
[![Code Health](https://raw.githubusercontent.com/bigdra50/unilyze/badges/codehealth.svg)](./docs/metrics.md)
[![NuGet](https://img.shields.io/nuget/v/Unilyze.svg)](https://www.nuget.org/packages/Unilyze)

A CLI tool for static analysis and visualization of type dependencies and code quality in Unity projects.

For build, test, and release information, see [README.dev.md](README.dev.md).

## Demo

<div><video controls src="https://github.com/user-attachments/assets/60ae2566-f961-4324-a16d-8f384b7d03fd" muted="false"></video></div>

### Requirements

- .NET 8.0 or later

## Quick Start

```
dotnet tool install --global Unilyze
```

Run in a Unity project directory to open the analysis results in your browser:

```bash
cd ~/MyUnityProject
unilyze
```

## Usage

```bash
unilyze                                          # Analyze and open in browser
unilyze -p ~/MyUnityProject                      # Specify project path
unilyze -p ~/MyUnityProject -o graph.html        # Save HTML + JSON
unilyze -p ~/MyUnityProject -f json -o result.json  # JSON output
unilyze -p ~/MyUnityProject -f sarif -o report.sarif # SARIF (GitHub Code Scanning)
unilyze -p ~/MyUnityProject --level core         # Pin analysis level (see Analysis Levels)
```

### Subcommands

```bash
unilyze config list                                # Show/manage configuration
unilyze diff <before.json> <after.json>            # Compare snapshots (JSON)
unilyze diff <before.json> <after.json> -o diff.html  # Compare snapshots (interactive HTML)
unilyze hotspot -p ~/MyUnityProject                # Git churn x complexity
unilyze trend <dir-of-jsons>                       # Quality trend
unilyze statusline -p ~/MyUnityProject             # Compact summary for status line
unilyze badge -p ~/MyUnityProject -o badge.json    # shields.io endpoint JSON (CI badges)
unilyze metrics                                    # Metric definitions & thresholds
unilyze schema                                     # JSON field reference
```

Run `unilyze --help` for all options.

### Status Line Integration

`unilyze statusline` outputs a compact one-line code health summary for use with [Claude Code's status line](https://docs.anthropic.com/en/docs/claude-code/statusline):

```
CH:9.8/5.9 MI:52 111smells 🔴1 📦66
```

| Item | Description |
|------|-------------|
| `CH:avg/min` | Average and minimum Code Health (1.0-10.0) |
| `MI:n` | Average Maintainability Index over method-bearing types (green >=80, yellow >=60, red <60) |
| `Nsmells` | Warning-level code smells |
| `🔴N` | Critical-level code smells (hidden if 0) |
| `📦N` | Boxing allocations (hidden if 0) |
| `♻N` | Cyclic dependencies (hidden if 0) |
| `[level]` | Analysis level marker, shown only below `Complete` (`[syntax]` / `[core]` / `[full]`) |

Results are cached per project (default 60s). Add to `~/.claude/statusline.sh`:

```bash
# Unilyze Code Health (Unity projects only)
if [[ -d "$PROJECT_DIR/Assets" ]] && [[ -d "$PROJECT_DIR/ProjectSettings" ]]; then
    UNILYZE_HASH=$(md5 -qs "$PROJECT_DIR")
    UNILYZE_CACHE="${TMPDIR:-/tmp/}unilyze-sl-${UNILYZE_HASH}.txt"
    if [[ -f "$UNILYZE_CACHE" ]]; then
        UNILYZE_STATUS=$(cat "$UNILYZE_CACHE" 2>/dev/null)
        CACHE_AGE=$(( $(date +%s) - $(stat -f %m "$UNILYZE_CACHE" 2>/dev/null || echo 0) ))
        [[ $CACHE_AGE -gt 60 ]] && (unilyze statusline -p "$PROJECT_DIR" > /dev/null 2>&1 &)
    elif command -v unilyze &>/dev/null; then
        (unilyze statusline -p "$PROJECT_DIR" > /dev/null 2>&1 &)
    fi
    [[ -n "${UNILYZE_STATUS:-}" ]] && echo "$UNILYZE_STATUS"
fi
```

### Badges

`unilyze badge` outputs [shields.io endpoint JSON](https://shields.io/badges/endpoint-badge) so you can show code quality badges in your README:

```bash
unilyze badge -p ~/MyUnityProject                  # code health (default)
unilyze badge -p ~/MyUnityProject --metric mi      # maintainability index
unilyze badge -p ~/MyUnityProject --metric smells  # code smell count
unilyze badge -p ~/MyUnityProject --format svg -o .github/badges/codehealth.svg
```

Use `--format svg` to emit a shields.io-style flat SVG badge instead of endpoint JSON. Commit the generated file and reference it from your README with a relative path.

#### Private repositories

The shields.io endpoint approach does not work in private repositories: GitHub's camo proxy and shields.io cannot fetch the raw JSON URL from an authenticated-only repo. Instead, generate the SVG with `unilyze badge --format svg`, commit it into the repository (for example under `.github/badges/`), and reference it from your README via a relative path. Authenticated viewers see the badge rendered inline without going through camo or an external fetch.

```markdown
![Code Health](.github/badges/codehealth.svg)
```

| Metric | Label | Message | Color |
|--------|-------|---------|-------|
| `codehealth` | `code health` | `avg / min` (e.g. `9.2 / 6.1`) | by min: green >=8.0, yellow >=5.0, red below |
| `mi` | `maintainability` | average MI (method-bearing types) | green >=80, yellow >=60, red below |
| `smells` | `smells` | warning count | red if critical > 0, yellow if warnings > 0, green if 0 |

#### Quality gates

`unilyze badge` can act as a CI gate. Without these flags the output is unchanged and the exit code stays `0`.

```bash
unilyze badge --metric codehealth --fail-under 7   # fail if min CodeHealth < 7
unilyze badge --metric mi --fail-under 70          # fail if average MI < 70
unilyze badge --metric smells --fail-over 5        # fail if warnings > 5 (or any critical)
```

| Flag | Valid metrics | Fails when |
|------|---------------|-----------|
| `--fail-under <value>` | `codehealth`, `mi` | min CodeHealth (codehealth) or average MI (mi) is strictly below `value` |
| `--fail-over <count>` | `smells` | warning count is strictly above `count`, or any critical smell exists |

Thresholds are inclusive: values exactly at the threshold pass. Only a value strictly below `--fail-under`, or a warning count strictly above `--fail-over`, fails the gate. Mismatched combinations (e.g. `--fail-under` with `--metric smells`) are a usage error.

The gate is fail-closed: if the metric is unavailable (0 types analyzed, or no method-bearing types for `mi`), the gate exits `2` with `gate failed: metric unavailable (...)` rather than passing. This catches a mistyped `-p` path that would otherwise produce a false green.

Exit codes: `0` success / gate passed, `1` usage error, `2` quality gate failed.

In CI the analysis runs at the SyntaxOnly level (no Unity installation required). Code health and MI are approximately stable across analysis levels (averages match in validation; min values can shift where `#if UNITY_EDITOR` code is excluded at SyntaxOnly). Smell counts are level-dependent: at SyntaxOnly only the syntax-level subset is reported (semantic smells such as boxing are not included), so smell badges are not comparable across levels. See [docs/metrics.md](./docs/metrics.md) for validation data.

To publish badges from GitHub Actions, generate the SVG on every push to `main` and serve it from a `badges` branch (this repository dogfoods the same workflow — see [badges.yml](./.github/workflows/badges.yml)):

```yaml
# .github/workflows/badges.yml
name: Badges
on:
  push:
    branches: [main]
permissions:
  contents: write # force-push to the badges branch
jobs:
  badges:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x
      - run: dotnet tool install --global Unilyze
      - run: |
          mkdir -p /tmp/badge-data
          unilyze badge -p . --format svg -o /tmp/badge-data/codehealth.svg
      - run: |
          cd /tmp/badge-data
          git init -q -b badges
          git config user.name "github-actions[bot]"
          git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
          git add .
          git commit -qm "update badges"
          git push -f "https://x-access-token:${GITHUB_TOKEN}@github.com/${GITHUB_REPOSITORY}.git" badges
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

Then reference it from your README:

```markdown
![Code Health](https://raw.githubusercontent.com/<owner>/<repo>/badges/codehealth.svg)
```

Prefer shields.io styling options? Generate endpoint JSON instead (the default format) and embed `https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/<owner>/<repo>/badges/codehealth.json`.

## Analysis Levels

unilyze resolves an analysis level based on which Unity DLLs it can locate. Higher levels resolve more types, so semantic metrics that depend on the `SemanticModel` (boxing, params allocations, CBO, DIT) are more complete. When Unity DLLs cannot be resolved (CI without a Unity install, missing `Library/ScriptAssemblies`), the analysis falls back to a lower level. The resolved level is reported on stderr and written to the JSON output as `analysisLevel`.

| Level | Resolved | What is accurate | What is understated |
|-------|----------|------------------|---------------------|
| `SyntaxOnly` | No Unity DLLs | CodeHealth, MI, cyclomatic/cognitive complexity, syntactic smells | Boxing, params allocations, CBO, DIT, inheritance across engine types |
| `CoreEngine` | UnityEngine core + framework | + types referencing `UnityEngine` core | Editor/module types, package assemblies |
| `FullEngine` | + engine/editor modules | + editor and module types | Compiled package assemblies (`Library/ScriptAssemblies`) |
| `Complete` | + package assemblies | full semantic resolution | — |

Pin the level with `--level <syntax|core|full|complete>` (supported by the main command, `statusline`, and `badge`). The pin caps the auto-resolved level: a higher resolved level is intentionally lowered for deterministic output, and if the requested level cannot be reached (for example `--level complete` without resolvable DLLs) the command fails with a non-zero exit code instead of silently degrading.

In the status line, a marker (`[syntax]` / `[core]` / `[full]`) is appended when the level is below `Complete`. See [docs/metrics.md](./docs/metrics.md#バリデーション-検証) for measured differences between `Complete` and `SyntaxOnly`.

## Configuration

unilyze loads settings from config files and CLI options. All scopes are merged additively (union).

| Scope | Path |
|-------|------|
| Global | `$XDG_CONFIG_HOME/unilyze/config.json` (default: `~/.config/unilyze/config.json`) |
| Project | `<project-root>/.unilyze.json` |
| CLI | `--exclude-dir <dir>` (repeatable) |

### Exclude Directories

Exclude directories from analysis (e.g., Asset Store imports, third-party code):

```jsonc
// .unilyze.json
{
  "excludeDirs": [
    "Assets/Plugins",
    "Assets/ThirdParty"
  ]
}
```

Paths are relative to the project root. Config files use JSONC (comments and trailing commas allowed).

CLI equivalent:

```bash
unilyze -p ~/MyUnityProject --exclude-dir Assets/Plugins --exclude-dir Assets/ThirdParty
```

The `statusline` subcommand automatically reads config files, so no CLI options are needed for status line integration.

### Managing Config

```bash
unilyze config list                                    # Show current configuration
unilyze config add-exclude-dir Assets/Plugins          # Add to project config
unilyze config add-exclude-dir Library --global        # Add to global config
unilyze config remove-exclude-dir Assets/Plugins       # Remove from project config
```

## Metrics

| Metric | Description | Granularity |
|--------|-------------|-------------|
| Cognitive Complexity | SonarSource-compliant cognitive complexity | Method |
| Cyclomatic Complexity | McCabe 1976-compliant cyclomatic complexity | Method |
| Halstead D/E/B | Difficulty, Effort, EstimatedBugs from operator/operand counts | Method |
| LCOM-HS | Henderson-Sellers cohesion (0.0-1.0+) | Type |
| WMC | Weighted Methods per Class (sum of CycCC) | Type |
| NOC | Number of Children (direct subclass count) | Type |
| RFC | Response For a Class (methods + unique external calls) | Type |
| CBO | Coupling Between Objects (number of coupled types) | Type |
| DIT | Depth of Inheritance (inheritance chain depth) | Type |
| Ca / Ce | Afferent / Efferent Coupling | Type |
| Instability | Ce / (Ca + Ce) (0.0: stable - 1.0: unstable) | Type |
| Maintainability Index | Computed from Halstead Volume, CycCC, LoC (0-100) | Method |
| TypeRank | PageRank-based importance score (damping=0.85) | Type |
| Code Health | Composite score (1.0: worst - 10.0: best) | Type |
| Abstractness | (abstract + interface) / total types | Assembly |
| DfMS | Distance from Main Sequence \|A + I - 1\| | Assembly |
| Relational Cohesion | (R + 1) / N internal relationship density | Assembly |

Run `unilyze metrics` for definitions and thresholds. See [docs/metrics.md](docs/metrics.md) for detailed specifications.

## Code Smell Detection

| Kind | Warning | Critical |
|------|---------|----------|
| GodClass | lines >= 500 OR methods >= 20 | lines >= 1000 |
| LongMethod | lines >= 80 OR CogCC >= 25 | lines >= 150 OR CogCC >= 40 |
| HighComplexity | CycCC >= 15 OR CogCC >= 15 | - |
| ExcessiveParameters | params > 5 | - |
| DeepNesting | depth >= 4 | depth >= 6 |
| LowCohesion | LCOM >= 0.8 | - |
| HighCoupling | CBO >= 15 | - |
| LowMaintainability | MI < 60 | - |
| DeepInheritance | DIT >= 5 | - |
| CyclicDependency | Cyclic dependencies between types/assemblies | - |

## Performance Analysis

Detects hidden heap allocations that cause GC pressure in Unity (requires SemanticModel):

| Kind | Detection |
|------|-----------|
| BoxingAllocation | Value type → object/interface, virtual method on struct without override |
| ClosureCapture | Lambda/anonymous method capturing outer scope variables |
| ParamsArrayAllocation | Implicit array allocation for params parameters |

## Exception Flow Analysis

| Kind | Detection |
|------|-----------|
| CatchAllException | `catch (Exception)` without rethrow |
| MissingInnerException | `throw new X()` in catch without passing inner exception |
| ThrowingSystemException | `throw new Exception()` directly (use specific exception types) |

## DI Container Detection

Detects type registrations in Unity DI containers and integrates them into the dependency graph. Registration endpoints are resolved to analyzed types, so the resulting edges feed cycle detection, CBO/Ca/Ce coupling, and TypeRank like any other dependency:

| Container | Patterns |
|-----------|----------|
| VContainer | `Register<T>`, `RegisterInstance`, `RegisterFactory`, `[Inject]` attribute |
| Zenject | `Bind<T>().To<T>()`, `BindInterfacesTo<T>()`, `BindInterfacesAndSelfTo<T>()` |

Endpoints that resolve to a type outside the analyzed set (e.g. a framework type), or to an ambiguous bare name shared by multiple namespaces, stay unconnected and contribute nothing to the metrics.

## Output Formats

| Format | Use Case |
|--------|----------|
| `html` | Interactive dependency graph in browser (offline fallback included) |
| `json` | Agent integration, programmatic use |
| `sarif` | GitHub Code Scanning, IDE integration |

## Diff Viewer

`unilyze diff <before.json> <after.json> -o diff.html` overlays metric deltas onto the standard analysis viewer.

Each type row gets:

- A change badge (`A` added / `M` modified / `D` removed) and color-coded left border
- Inline `▲`/`▼` deltas next to Health, Max CogCC, CBO, DIT cells
- A `Changed only` toggle in the diff summary bar
- A "Changes vs Baseline" / "Methods Changed" / "Smells Δ" section in the type detail panel

The viewer otherwise behaves like a normal `unilyze` HTML report (dependency graph, hotspots, cycles, assembly coupling).

### Regression gate

`--fail-on-regression` turns `diff` into a CI gate. The output (JSON, HTML, or stderr summary) is unchanged; only the exit code reflects the gate.

```bash
unilyze diff before.json after.json --fail-on-regression
```

A regression is any of: average or min CodeHealth dropped, warning smell count increased, or critical smell count increased (after vs before). On regression, the reason is printed to stderr on one line (e.g. `regression: min CodeHealth 7.2 -> 6.8`).

The gate is evaluated on these **project-wide aggregates**, which is intentionally distinct from the per-type `Degraded`/`Improved` counts shown in the diff summary. A single type can degrade while the aggregates stay flat (e.g. another type improves enough to offset it), so it is possible to see `Degraded: 1` in the summary yet get exit `0`. If you want to gate on any individual type degrading rather than on aggregates, judge on the per-type `Degraded` count from the summary instead.

Exit codes: `0` no regression, `1` usage error, `2` regression detected.

## Agent Workflow

```
unilyze (measure) -> identify issues -> fix -> unilyze diff (verify)
```

### Install skills

```bash
unilyze skills install --claude                   # Claude Code
unilyze skills install --claude --codex --cursor  # Multiple targets
unilyze skills install --claude --global          # Global install
```

Supported: `--claude`, `--codex`, `--cursor`, `--gemini`, `--windsurf`

### Self-documenting CLI

Agents can discover metrics and schema without external docs:

```bash
unilyze metrics   # Definitions & thresholds
unilyze schema    # JSON field reference
```

## Known Limitations

- HTML graph loads Cytoscape from CDN. Falls back to offline report when unavailable.
- Windows is untested.

## License

MIT

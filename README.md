# unilyze

[![CI](https://github.com/bigdra50/unilyze/actions/workflows/ci.yml/badge.svg)](https://github.com/bigdra50/unilyze/actions/workflows/ci.yml)
[![Code Health](https://img.shields.io/endpoint?url=https%3A%2F%2Fraw.githubusercontent.com%2Fbigdra50%2Funilyze%2Fbadges%2Fcodehealth.json)](./docs/metrics.md)
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
| `MI:n` | Average Maintainability Index (green >=80, yellow >=60, red <60) |
| `Nsmells` | Warning-level code smells |
| `🔴N` | Critical-level code smells (hidden if 0) |
| `📦N` | Boxing allocations (hidden if 0) |
| `♻N` | Cyclic dependencies (hidden if 0) |

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
```

| Metric | Label | Message | Color |
|--------|-------|---------|-------|
| `codehealth` | `code health` | `avg / min` (e.g. `9.2 / 6.1`) | by min: green >=8.0, yellow >=5.0, red below |
| `mi` | `maintainability` | average MI | green >=80, yellow >=60, red below |
| `smells` | `smells` | warning count | red if critical > 0, yellow if warnings > 0, green if 0 |

In CI the analysis runs at the SyntaxOnly level (no Unity installation required). Code health and MI are stable across analysis levels, while smell counts are syntax-level (semantic smells such as boxing are not included). See [docs/metrics.md](./docs/metrics.md) for validation data.

To publish badges from GitHub Actions, generate the JSON on every push to `main` and serve it from a `badges` branch (this repository dogfoods the same workflow — see [badges.yml](./.github/workflows/badges.yml)):

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
          unilyze badge -p . -o /tmp/badge-data/codehealth.json
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
![Code Health](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/<owner>/<repo>/badges/codehealth.json)
```

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

Detects type registrations in Unity DI containers and adds them to the dependency graph:

| Container | Patterns |
|-----------|----------|
| VContainer | `Register<T>`, `RegisterInstance`, `RegisterFactory`, `[Inject]` attribute |
| Zenject | `Bind<T>().To<T>()`, `BindInterfacesTo<T>()`, `BindInterfacesAndSelfTo<T>()` |

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

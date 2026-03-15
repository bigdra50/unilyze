# unilyze

[![NuGet](https://img.shields.io/nuget/v/Unilyze.svg)](https://www.nuget.org/packages/Unilyze)

A CLI tool for static analysis and visualization of type dependencies and code quality in Unity projects.

[日本語版 / Japanese](README_JP.md)

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
# Analyze current directory and open in browser
unilyze

# Specify project path
unilyze -p ~/MyUnityProject

# Save HTML + JSON to file
unilyze -p ~/MyUnityProject -o graph.html

# Generate HTML/JSON without opening browser
unilyze -p ~/MyUnityProject --no-open

# JSON output only
unilyze -p ~/MyUnityProject -f json -o result.json

# SARIF output (GitHub Code Scanning integration)
unilyze -p ~/MyUnityProject -f sarif -o report.sarif

# Regenerate HTML from existing JSON
unilyze -i result.json -o graph.html

# Filter by assembly name
unilyze -p ~/MyUnityProject -a App.Domain

# Filter by prefix
unilyze -p ~/MyUnityProject --prefix "App."
```

### Subcommands

```bash
# Compare analysis results between two points in time (before/after)
unilyze diff <before.json> <after.json>
unilyze diff <before.json> <after.json> -o diff.json

# Hotspot analysis (git churn x complexity)
unilyze hotspot -p ~/MyUnityProject
unilyze hotspot -p ~/MyUnityProject --since 6.month -n 10

# Quality trend (time-series comparison)
unilyze trend <dir-of-jsons>
unilyze trend <dir-of-jsons> -o trend.json

# Show metric definitions and code smell thresholds
unilyze metrics

# Show JSON output field reference
unilyze schema
```

`hotspot` requires the `git` command and assumes the target path is a Git repository.

## Options

| Option | Description | Default |
|--------|-------------|---------|
| `-p, --path` | Unity project root | `.` |
| `-i, --input` | Use existing JSON as input | - |
| `-o, --output` | Output destination (format inferred from extension) | Open in browser |
| `-f, --format` | Output format: `html`, `json`, `sarif` | `html` |
| `-a, --assembly` | Assembly name to analyze | All assemblies |
| `--prefix` | Filter prefix for asmdef names | Auto-detected |
| `--no-open` | Do not open browser after HTML generation | `false` |

## Metrics

| Metric | Description | Granularity |
|--------|-------------|-------------|
| Cognitive Complexity | SonarSource-compliant cognitive complexity | Method |
| Cyclomatic Complexity | McCabe 1976-compliant cyclomatic complexity | Method |
| LCOM-HS | Henderson-Sellers cohesion (0.0-1.0+) | Type |
| CBO | Coupling Between Objects (number of coupled types) | Type |
| DIT | Depth of Inheritance (inheritance chain depth) | Type |
| Ca / Ce | Afferent / Efferent Coupling | Type |
| Instability | Ce / (Ca + Ce) (0.0: stable - 1.0: unstable) | Type |
| Maintainability Index | Computed from Halstead Volume, CycCC, LoC (0-100) | Method |
| Code Health | Composite score (1.0: worst - 10.0: best) | Type |

Run `unilyze metrics` for detailed definitions and thresholds from the CLI. See also [docs/metrics.md](docs/metrics.md).

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
| CyclicDependency | Cyclic dependencies between types/assemblies (Tarjan SCC) | - |

## Output Formats

| Format | Use Case |
|--------|----------|
| `html` | Visualize dependency graphs and metrics in browser |
| `json` | Agent integration, programmatic use |
| `sarif` | GitHub Code Scanning, IDE integration |

The `html` output normally displays an interactive dependency graph. In environments where external graph assets cannot be loaded, it automatically falls back to a built-in report view showing type lists, dependencies, hotspots, cyclic dependencies, and assembly coupling.

## Known Limitations

- The interactive `html` graph loads Cytoscape scripts from CDN. In environments where these cannot be fetched, it falls back to a built-in offline report. This is by design.
- Windows has not been tested. Environments not listed in this README are not yet guaranteed to work.

## Analysis Levels

Analysis precision is maximized through a 3-level fallback based on project configuration:

| Priority | Source | Information Obtained |
|----------|--------|---------------------|
| 1 | `.csproj` / `.sln` | DLL reference paths, preprocessor symbols, C# version |
| 2 | `.asmdef` + Unity DLL | Unity Engine/Editor DLLs, package DLLs |
| 3 | SyntaxOnly | SyntaxTree only (no SemanticModel) |

When `.csproj` files exist, reference information is automatically obtained, improving accuracy of SemanticModel-based analysis (LCOM, CBO, DIT, bool &/| in CycCC, etc.). Without `.csproj`, DLLs are auto-resolved from `.asmdef` files and Unity installations. If neither is found, it operates with SyntaxTree only.

Projects without `.asmdef` files are also supported. The entire directory is analyzed as a single assembly.

## Agent Workflow

Designed to drive quality improvement loops with coding agents:

```
unilyze (measure) -> identify issues -> fix -> unilyze diff (verify) -> confirm improvement
```

### Install skills for your agent

```bash
# Claude Code
unilyze skills install --claude

# Multiple targets at once
unilyze skills install --claude --codex --cursor

# Global install (available across all projects)
unilyze skills install --claude --global
```

Supported targets: `--claude`, `--codex`, `--cursor`, `--gemini`, `--windsurf`

Skills provide structured workflows (`/quality-audit`, `/refactor-loop`) that guide agents through measure-fix-verify cycles.

### Self-documenting CLI for agents

Agents can discover metrics and JSON schema without external docs:

```bash
unilyze metrics   # Metric definitions, CodeSmell thresholds
unilyze schema    # JSON output field reference (for jq queries)
```

### Measure-fix-verify loop

```bash
# 1. Measure
unilyze -p ~/MyProject -f json -o /tmp/before.json

# 2. Agent fixes problem areas
# ...

# 3. Re-measure & verify improvement with diff
unilyze -p ~/MyProject -f json -o /tmp/after.json
unilyze diff /tmp/before.json /tmp/after.json
```

## License

MIT

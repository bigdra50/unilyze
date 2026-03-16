# unilyze

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
unilyze diff <before.json> <after.json>           # Compare snapshots
unilyze hotspot -p ~/MyUnityProject               # Git churn x complexity
unilyze trend <dir-of-jsons>                      # Quality trend
unilyze metrics                                   # Metric definitions & thresholds
unilyze schema                                    # JSON field reference
```

Run `unilyze --help` for all options.

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

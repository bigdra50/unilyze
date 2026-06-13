# crossval — official Roslyn metrics cross-validation harness

Re-measures cyclomatic complexity with the official Roslyn metrics engine
(`CodeAnalysisMetricData` from `Microsoft.CodeAnalysis.AnalyzerUtilities`, the
implementation behind `Metrics.exe` and CA1502) and emits per-type / per-member
data plus per-method counts of the constructs where the two conventions differ
(switch expression arms, catch, goto, default labels, deconstruction foreach,
bool `&` / `|`).

This is the reproduction harness for the convention-difference table in
[docs/metrics.md](../../docs/metrics.md) (issue #4).

## Usage

```bash
dotnet run --project scripts/crossval -c Release -- src/Unilyze/Unilyze.csproj > official-cc.json
dotnet run --project src/Unilyze -c Release --framework net10.0 -- -p src/Unilyze -f json > unilyze-cc.json
```

Then compare per type: official `OfficialCycCC` vs the sum of
`types[].members[].cyclomaticComplexity` over `memberKind == "Method"`.
Expected relationship (verified over 339 methods with zero residual):

```
unilyze − official = switchArms + catches + gotos − defaultLabels − memberBase + boolAmpOr
```

where `memberBase` is `OfficialCycCC` minus the sum of official method CC for symbols
unilyze aggregates (excluding constructors, operators, and property accessors counted
only on the official side). `boolAmpOr` counts boolean-typed `&` / `|` operands via
semantic model (same rule as unilyze Complete analysis).

Exclude source-generated `JsonSerializerContext` partials (types ending in
`JsonContext`) when matching types: the official engine sees the generated
members, unilyze's source-level view does not. Also exclude compiler `Program`
(top-level statements compile to `Program.<Main>$`, which unilyze does not model as a type).

### Automated compare (CI)

On pushes to `main` and version tags, [`.github/workflows/crossval.yml`](../../.github/workflows/crossval.yml)
runs self-analysis, official metrics, and residual validation:

```bash
dotnet run --project src/Unilyze -c Release --framework net10.0 -- -p src/Unilyze -f json > unilyze-cc.json
dotnet run --project scripts/crossval -c Release -- src/Unilyze/Unilyze.csproj > official-cc.json
dotnet run --project scripts/crossval -c Release -- compare official-cc.json unilyze-cc.json
```

Exit code 0 means every compared type's delta is explained by the residual identity
(known ±1 types `HalsteadWalker`, `State`, and nested `Walker` are allowlisted per
[docs/metrics.md](../../docs/metrics.md)). Non-zero exit lists unexplained types on stderr.

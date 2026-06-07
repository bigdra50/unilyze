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
unilyze − official = switchArms + catches + gotos − defaultLabels − memberBaseOnlyInOfficial
```

where `memberBaseOnlyInOfficial` is the official engine's base-1 (plus internal
branches) for member symbols unilyze does not aggregate: constructors
(including implicit/primary), property accessors, and operators.

Exclude source-generated `JsonSerializerContext` partials (`AnalysisJsonContext`,
`BadgeJsonContext`) when matching types: the official engine sees the generated
members, unilyze's source-level view does not.

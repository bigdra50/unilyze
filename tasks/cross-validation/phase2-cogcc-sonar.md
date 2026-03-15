# Phase 2: CogCC - Unilyze vs SonarAnalyzer S3776

SonarAnalyzer.CSharp version: 10.20.0.135146

## Summary

| Project | Matched | Non-trivial | Exact% | Within1% | MAE | MaxDelta | Spearman | Pearson |
|---------|---------|-------------|--------|----------|-----|----------|----------|---------|
| HelloMarioFramework | 418 | 207 | 100.0% | 100.0% | 0.00 | 0 | 1.000 | 1.000 |
| VContainer | 575 | 170 | 96.5% | 99.1% | 0.07 | 14 | 0.968 | 0.995 |

## HelloMarioFramework

- Matched methods: 418
- Non-trivial (CogCC > 0): 207
- SonarAnalyzer raw diagnostics: 207
- Sonar-only methods (unmatched): 0
- Exact match rate: 100.0% (418 / 418)
- Within +/-1: 100.0%
- MAE: 0.00
- Spearman rho: 1.000
- Pearson r: 1.000

## VContainer

- Matched methods: 575
- Non-trivial (CogCC > 0): 170
- SonarAnalyzer raw diagnostics: 178
- Sonar-only methods (unmatched): 19
- Exact match rate: 96.5% (555 / 575)
- Within +/-1: 99.1%
- MAE: 0.07
- Spearman rho: 0.968
- Pearson r: 0.995

### Divergences (20 methods)

| Method | Unilyze | Sonar | Delta |
|--------|---------|-------|-------|
| `TypeAnalyzer.Analyze` | 77 | 91 | -14 |
| `ObjectResolverUnityExtensions.InjectGameObject` | 5 | 9 | -4 |
| `LifetimeScope.Find:2` | 3 | 0 | +3 |
| `LifetimeScope.Find:1` | 1 | 3 | -2 |
| `FixedTypeObjectKeyHashtable<TValue>.TryGet` | 11 | 13 | -2 |
| `ScopedContainer.Resolve:2` | 3 | 2 | +1 |
| `Container.Resolve:2` | 3 | 2 | +1 |
| `RegistrationBuilder.As:0` | 1 | 0 | +1 |
| `RegistrationBuilder.As:0` | 1 | 0 | +1 |
| `RegistrationBuilder.As:0` | 1 | 0 | +1 |
| `RegistrationBuilder.As:0` | 1 | 0 | +1 |
| `RegistrationBuilder.WithParameter:1` | 1 | 0 | +1 |
| `RegistrationBuilder.WithParameter:1` | 1 | 0 | +1 |
| `RegistrationBuilder.WithParameter:1` | 1 | 0 | +1 |
| `LifetimeScope.Find:0` | 1 | 0 | +1 |
| `PrefabDirtyScope.Dispose` | 0 | 1 | -1 |
| `ListPool<T>.Get:1` | 1 | 0 | +1 |
| `CollectionInstanceProvider.GetEnumerator:0` | 0 | 1 | -1 |
| `CollectionInstanceProvider.GetEnumerator:0` | 1 | 0 | +1 |
| `CollectionInstanceProvider.SpawnInstance:1` | 3 | 2 | +1 |

### Sonar-only methods (not matched to Unilyze)

- `CappedArrayPool<T>.CappedArrayPool`
- `DiagnosticsInfoTreeViewItem.ContractTypesSummary`
- `DiagnosticsInfoTreeViewItem.RegisterSummary`
- `FixedTypeObjectKeyHashtable<TValue>.FixedTypeObjectKeyHashtable`
- `HasAbstractCircularDependency1.HasAbstractCircularDependency1`
- `HasAbstractCircularDependency2.HasAbstractCircularDependency2`
- `HasCircularDependency1.HasCircularDependency1`
- `HasCircularDependency2.HasCircularDependency2`
- `HasCircularDependencyMsg1.HasCircularDependencyMsg1`
- `InjectFieldInfo.InjectFieldInfo`
- ... (9 more)

## Methodology

1. SonarAnalyzer S3776 run programmatically via Roslyn CompilationWithAnalyzers API
2. No full compilation required - parsed syntax trees with minimal references
3. S3776 threshold=0 via SonarLint.xml AdditionalText, severity via SpecificDiagnosticOptions
4. Unity preprocessor symbols (UNITY_EDITOR etc.) passed to parse options
5. Matching by TypeName.MethodName; overloaded methods matched by line proximity
6. Methods not reported by SonarAnalyzer assumed CogCC=0

## Divergence analysis (VContainer)

### TypeAnalyzer.Analyze (Unilyze=77, Sonar=91, delta=-14)

This method contains 2 `goto` statements and `#if UNITY_2018_4_OR_NEWER` preprocessor guards. The delta likely comes from `goto` nesting increment differences: SonarAnalyzer may apply nesting increments to `goto` target labels that are inside deeply nested blocks, while Unilyze counts `goto` as a flat +1.

### ObjectResolverUnityExtensions.InjectGameObject (Unilyze=5, Sonar=9, delta=-4)

Contains a non-static local function `InjectGameObjectRecursive` with a recursive call. Unilyze counts static local functions independently from the parent method, but this function is non-static. The delta may come from how SonarAnalyzer attributes the local function's full complexity (including nesting from the enclosing scope) to the parent method.

### FixedTypeObjectKeyHashtable.TryGet (Unilyze=11, Sonar=13, delta=-2)

Contains `goto` statements inside nested `while` loops. The 2-point delta aligns with SonarAnalyzer applying nesting increments to the `goto`s.

### Overloaded method matching artifacts

Several `RegistrationBuilder.As:0` and `RegistrationBuilder.WithParameter:1` entries appear as duplicates with delta=+1, likely from imprecise overload matching where multiple Unilyze overloads share the same parameter count but only one has a Sonar match.

## Known differences

| Source | Unilyze | SonarAnalyzer | Impact |
|--------|---------|---------------|--------|
| `goto` | +1 (flat) | +1 + nesting increment | Sonar higher on nested goto |
| Non-static local functions | Complexity attributed to parent | May attribute differently | Varies |
| `#if`/`#elif` (preprocessor) | Parsed with UNITY_EDITOR | Parsed with UNITY_EDITOR | Matches |
| Constructors | Included in methods list | Detected but key format differs | Sonar-only |
| Properties (get/set) | Not always in methods list | Detected separately | Sonar-only |

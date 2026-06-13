# Unilyze Metric Definitions

Definitions, compliance specifications, and known differences for each metric computed by Unilyze.

## Cognitive Complexity (CogCC)

Compliance specification: [SonarSource Cognitive Complexity Whitepaper](https://www.sonarsource.com/docs/CognitiveComplexity.pdf)

### Rules

| Category | Target | Increment |
|---------|------|-------------|
| Structural | `if`, `else if`, `else`, `switch`, `for`, `foreach`, `while`, `do`, `catch` | +1 + nesting |
| Fundamental | `goto`, direct recursion | +1 |
| Logical operators | `&&`, `||`, `or`, `and` | +1 (only when the operator kind changes; consecutive same-kind operators stay at +1. `or` is same-kind as `||`, `and` as `&&`) |
| Nesting increase | lambda, anonymous method | nesting +1 (no structural increment) |
| Shorthand | `??`, `?.` | 0 (no increment) |

### Differences from SonarAnalyzer.CSharp (S3776)

Cross-validation results against SonarAnalyzer.CSharp 10.20.0 (70 methods from Unilyze's own source):

| Metric | Value |
|------|-----|
| Spearman rank correlation | 1.000 |
| Exact match rate | 100.0% (70/70) |
| Within ±1 rate | 100.0% (70/70) |

| Syntax | SonarAnalyzer | Unilyze | Notes |
|------|-------------|---------|------|
| `or` pattern combinator | +1 | +1 | Supported (treated same-kind as `||`) |
| `and` pattern combinator | +1 | +1 | Supported (treated same-kind as `&&`) |
| Direct recursion | +1 | +1 | Supported (method-name-based detection) |
| static local function | Independent calculation | Included in method | Specification difference |
| `??` (null coalesce) | 0 | 0 | Match (fixed in v0.2.0) |
| `switch` expression | +1 + nesting | +1 + nesting | Match |

## Cyclomatic Complexity (CycCC)

Compliance specification: McCabe, T.J. (1976) "A Complexity Measure"

Each predicate node (branch point) is counted as +1. Base paths are 1.

### Count targets

| Node | Increment |
|--------|-------------|
| `if` | +1 |
| `case` label / `case` pattern | +1 |
| `for`, `foreach` (including deconstruction foreach), `while`, `do` | +1 |
| `catch` | +1 |
| `? :` (ternary operator) | +1 |
| `?.` (null conditional) | +1 |
| `??` (null coalescing) | +1 |
| `&&`, `||` | +1 each |
| `&`, `|` on bool operands | +1 each (semantic-model analysis levels only; not counted under SyntaxOnly where types cannot be resolved) |
| `goto` | +1 |
| `switch` expression arm | +1 |

`??=`, the switch expression itself, catch `when` filters, and `and` / `or` patterns are not counted.

### Convention differences from the official Roslyn engine (CodeAnalysisMetricData / Metrics.exe / CA1502)

Cross-validation of all 339 methods in src/Unilyze against the official engine established the following convention-difference table (zero residual for 97/100 types; remaining 3 types decomposed to ±1)
(reproduction steps: [scripts/crossval](https://github.com/bigdra50/unilyze/blob/main/scripts/crossval/); validation data in the [Validation](#validation) section):

| Syntax | Official engine | Unilyze |
|------|------------|---------|
| `if` / `? :` / loops / `case` label·pattern / `?.` / `??` / `&&` / `\|\|` | +1 | +1 |
| `default` label | +1 | not counted |
| `catch` | not counted | +1 |
| `switch` expression arm | not counted | +1 |
| `goto` | not counted | +1 |
| `??=` | not counted | not counted |
| bool `&` / `\|` | +1 | +1 only with semantic model |
| Type aggregation | base 1 per member symbol (implicit ctor, accessor, operator included) | base 1 per declared method only |

Notes:

- This document previously stated "the official engine does not count `?.` `??`"; that was incorrect (disproved by implementation cross-validation). Both engines count them
- Do not apply CA1502's default threshold of 25 directly to unilyze CycCC. Switch expression arms and similar additions make unilyze values systematically higher; no approximate conversion formula is provided (re-analysis with the official engine is required for accurate comparison)
- Design decision: unilyze intentionally maintains the extended interpretation (counting catch / arm / goto as real branches). `catch` and switch arms are faithful to McCabe's branch-point definition and preserve compatibility with existing baselines (refactor loop, trends, badges). Use CA1502 / Metrics.exe directly when official-compatible values are needed. For modern complexity gates, CogCC (100% aligned with SonarAnalyzer S3776) is recommended

## LCOM-HS (Henderson-Sellers)

Compliance specification: Henderson-Sellers, B. (1996) "Object-Oriented Metrics: Measures of Complexity"

### Formula

```
LCOM-HS = (avg(mA) - M) / (1 - M)

mA(f) = number of methods that access field f
avg(mA) = average of mA across all fields
M = number of instance methods (constructors included)
```

### Interpretation

| Value | Meaning |
|-----|------|
| 0.0 | Perfect cohesion (all methods access all fields) |
| 1.0 | Perfect separation (each method accesses only a distinct field) |
| null | Not computable (0 fields, or 0–1 methods) |

### Differences from NDepend / CK

| Item | NDepend (latest) | CK | Unilyze |
|------|--------------|-----|---------|
| auto-property | Excluded from F | Included in F | Excluded from F (fixed in v0.2.0) |
| Constructor | Included in M | Included in M | Included in M (fixed in v0.2.0) |
| static members | Excluded | Excluded | Excluded |

## WMC (Weighted Methods per Class)

Compliance specification: Chidamber, S.R. & Kemerer, C.F. (1994) "A Metrics Suite for Object Oriented Design"

### Formula

```
WMC = Σ CycCC(method_i)  for all methods in class
```

Sum of Cyclomatic Complexity for all methods in the class. Weighting uses CycCC.

### Interpretation

| Value | Meaning |
|-----|------|
| 0 | No methods (data class, enum, etc.) |
| 1-20 | Typical range |
| > 20 | Refactoring candidate |

## NOC (Number of Children)

Compliance specification: Chidamber & Kemerer (1994)

Count of direct subclasses. Derived by reverse lookup from DependencyBuilder Inheritance dependencies.

### Interpretation

| Value | Meaning |
|-----|------|
| 0 | Not inherited |
| High | Reusable base class; large blast radius on change |

## RFC (Response For a Class)

### Formula

```
RFC = M + R

M = number of methods in the class (constructors included)
R = number of unique external methods invoked from within M
```

### Semantic / Syntactic paths

| Path | Resolution method |
|------|---------|
| Semantic | Resolve InvocationExpression symbols via SemanticModel. Accurate |
| Syntactic (fallback) | Approximate by InvocationExpression method name string. Cannot distinguish overloads |

### Interpretation

| Value | Meaning |
|-----|------|
| <= 50 | Typical range |
| > 50 | Tends to be difficult to test and understand |

## CBO (Coupling Between Objects)

Compliance specification: Chidamber & Kemerer (1994)

### Formula

```
CBO = number of unique external types coupled to type T
```

Count of types referenced from T's declaration, members, and method bodies, excluding self and excluded types.

### Counting conventions

Implementation: `CboCalculator.cs`

| Path | Resolution method |
|------|---------|
| Semantic | Walk descendants of the type declaration for `TypeSyntax` / `ObjectCreationExpression` / `CastExpression`, resolve `ITypeSymbol` via `SemanticModel`, add `INamedTypeSymbol.OriginalDefinition` to the set (generic type arguments and array element types collected recursively) |
| Syntactic (fallback) | Collect type name strings from base list, field/property types, method/constructor signatures and bodies (local variable declarations, `new`, cast, `typeof`) |

Common exclusions:

- Self type
- Semantic: built-in types where `SpecialType` is not `None`, `System.ValueType` / `System.Enum` / `System.Delegate` / `System.MulticastDelegate` / `System.Attribute` / `System.Void`
- Syntactic: C# primitive names (`int`, `string`, `object`, etc.)

CBO is computed directly from the type-declaration AST, independent of the `TypeDependency` graph. DI registration edges are not included in CBO.

### Thresholds (code smell)

| Level | Condition |
|--------|------|
| Warning (`HighCoupling`) | CBO >= 15 |
| Critical (`HighCoupling`) | CBO >= 25 |

Constants: `SmellThresholds.HighCouplingCboWarning` / `HighCouplingCboCritical`

### Notes

- Under `SyntaxOnly` analysis without SemanticModel, CBO is underestimated (coupling to external engine types is invisible)
- Differs from the official Metrics engine ClassCoupling in counting scope and granularity (see [Validation](#validation))

## DIT (Depth of Inheritance)

Compliance specification: Chidamber & Kemerer (1994)

### Formula

```
DIT = length of inheritance chain from type T up to (but not including) `System.Object`
```

interface / struct → 0. class / record counts direct and indirect base classes.

### Counting conventions

Implementation: `DitCalculator.cs`

| Path | Convention |
|------|------|
| Semantic | interface → 0. struct → 0. Otherwise walk `INamedTypeSymbol.BaseType` until `System.Object` and count steps (`System.Object` itself is not counted) |
| Syntactic (fallback) | interface / struct / record struct → 0. No base list → 0. First base is `QualifiedNameSyntax` (external type) → 1. Same-name interface declaration in the same syntax tree → 0; otherwise → 1 |

When semantic calculation fails, `SemanticEnricher` falls back to syntactic fallback or `TypeNodeInfo.BaseType` presence (0/1).

### Thresholds (code smell)

| Level | Condition |
|--------|------|
| Warning (`DeepInheritance`) | DIT >= 5 |

Constant: `SmellThresholds.DeepInheritanceDitWarning`

### Notes

- The official Metrics engine counts `object` inheritance as 1, producing a uniform offset (see [Validation](#validation))
- Inheritance spanning engine types (`UnityEngine.MonoBehaviour`, etc.) requires Semantic analysis; `SyntaxOnly` underestimates

## Ca / Ce (Afferent / Efferent Coupling)

Type-level coupling based on Martin's stability analysis.

### Formula

```
Ca(T) = number of unique directed edges with T as dependency target (To)
Ce(T) = number of unique directed edges with T as dependency source (From)
```

Input is the `TypeDependency` list from `DependencyBuilder.Build` (inheritance, interface implementation, member types, constructor/method parameters, generic constraints), plus resolved DI registration edges (VContainer / Zenject) and `SerializedReference` edges resolved from Unity scene/prefab/`.asset` YAML (`[SerializeField]` or public fields matched against Inspector wiring).

### Counting conventions

Implementation: `CouplingMetricsCalculator.cs`

- Count only `FromTypeId` / `ToTypeId` present in the analysis target type set (`allTypes`)
- Exclude self-reference (`From == To`)
- Each `(From, To)` pair counted once (no duplication across multiple `DependencyKind` values)
- Exclude edges where `FromTypeId` or `ToTypeId` is null (unresolved edges to types outside the analysis scope)

No threshold-based code-smell detection for Ca / Ce.

### Notes

- Ca / Ce are edge counts on the dependency graph; definition differs from CBO (type-reference set from type-declaration AST)
- DI registrations to types outside the analysis scope are not connected as edges and do not contribute to Ca / Ce
- `SerializedReference` represents concrete-type Inspector wiring. It is distinct from `FieldType` edges on the declared field type (e.g. base class) and affects Ca/Ce/Instability/TypeRank/cycles/DfMS but not declaration-based CBO

## Instability (I)

Martin's Instability. Computed at type level and assembly level with different granularity.

### Formula (type level)

```
I(T) = Ce(T) / (Ca(T) + Ce(T))     when Ca + Ce > 0
I(T) = null                        when Ca + Ce = 0
```

Implementation: `CouplingMetricsCalculator.cs` (per type). JSON output rounds to 2 decimal places.

### Formula (assembly level)

```
I(assembly) = Σ Ce / (Σ Ca + Σ Ce)
```

Sum Ca / Ce across all types in the assembly. Implementation: `AssemblyMetrics.ComputeAssemblyInstability`. Distance from Main Sequence uses this assembly-level I.

### Interpretation

| Value | Meaning |
|-----|------|
| 0.0 | Fully stable (depended on by other types only) |
| 1.0 | Fully unstable (depends on other types only) |
| null (type only) | Zero inbound and outbound coupling |

No code-smell thresholds for Ca / Ce / Instability.

## Halstead Complexity Measures

Compliance specification: Halstead, M.H. (1977) "Elements of Software Science"

### Base measures

| Symbol | Meaning |
|------|------|
| n1 (UniqueOperators) | Number of unique operators |
| n2 (UniqueOperands) | Number of unique operands |
| N1 (TotalOperators) | Total operator count |
| N2 (TotalOperands) | Total operand count |

### Derived metrics

| Metric | Formula | Description |
|-----------|------|------|
| Volume (V) | `(N1 + N2) * log2(n1 + n2)` | Implementation size |
| Difficulty (D) | `(n1 / 2) * (N2 / n2)` | Difficulty of understanding. 0 when n2=0 |
| Effort (E) | `D * V` | Mental effort required to implement |
| EstimatedBugs (B) | `E^(2/3) / 3000` | Estimated bug count |

## Maintainability Index (MI)

Compliance specification: Oman & Hagemeister (1992) — normalized MI in the Visual Studio / Microsoft Code Metrics family

### Formula (method level)

```
loc = max(1, line span of method declaration)
V   = Halstead Volume (HalsteadCalculator.cs)

raw = 171 - 5.2 × ln(V) - 0.23 × CycCC - 16.2 × ln(loc)
MI  = max(0, raw × 100 / 171)        when V > 0
MI  = 100                            when V <= 0
```

`ln` is the natural logarithm (`Math.Log`). CycCC is unilyze Cyclomatic Complexity (McCabe extended interpretation). MI is computed with CycCC from the initial syntactic parse; MI itself is not recomputed when Semantic enrich updates CycCC.

### Type-level aggregation

Implementation: `CodeHealthCalculator.cs`

```
AverageMaintainabilityIndex = arithmetic mean of method MI in the type (1 decimal place)
MinMaintainabilityIndex     = minimum method MI in the type (1 decimal place)
```

Types without methods are not MI targets. Project average (statusline / badge) uses only types with methods as the denominator.

### Thresholds (code smell)

| Level | Condition |
|--------|------|
| Warning (`LowMaintainability`) | method MI < 60 |

Constant: `SmellThresholds.LowMaintainabilityMiWarning`

badge / statusline color bands (reference): green >= 80, yellow >= 60, red < 60 (`BadgeFormatter.cs` / `StatuslineFormatter.cs`)

### Notes

- Line count spans the full declaration including the signature, not just the method body (`MemberExtractor.cs`)
- The official Metrics engine aggregates at type level; convention difference from method average yields high correlation but not exact match (see [Validation](#validation))
- Approximately stable under SyntaxOnly, same as CodeHealth

### Validity limits

MI relies on fixed coefficients (171, 5.2, 0.23, 16.2) from a 1992 regression on Visual Basic code and has limited applicability to modern C# / Unity codebases.

- Arie van Deursen "Think Twice Before Using the Maintainability Index" (https://avandeursen.com/2014/08/29/think-twice-before-using-the-maintainability-index/)
- Borg et al. "Ghost Echoes Revealed: Benchmarking Maintainability Metrics and Machine Learning Predictions Against Human Assessments" (ICSME 2024, arXiv:2408.10754)

Recent evaluations, including the latter, show that classical metrics including MI correlate weakly with human maintainability assessments. unilyze outputs MI as a reference value but does not recommend it as a standalone quality-gate metric. Phase 3 consolidates on CodeHealth as the primary indicator; MI is scaled back to backward compatibility or supplementary display.

## TypeRank

PageRank-based type importance score equivalent to NDepend TypeRank.

Resolved DI registration edges (VContainer / Zenject) are included in the `TypeDependency` graph and counted in CBO (Ca/Ce), cycle detection, and TypeRank. Unresolved edges to types outside the analysis scope are excluded.

### Algorithm

- Input: DependencyBuilder TypeDependency list → adjacency list
- damping factor: 0.85
- convergence threshold: 1e-6 (L1 norm)
- max iterations: 100
- Dangling nodes (out-degree 0) distribute rank equally to all nodes
- Result normalized (sum = 1.0)

### Interpretation

Higher values indicate types depended on by more other types. Value objects and infrastructure types tend to rank high.

## Abstractness (A)

Compliance specification: Martin, R.C. "Agile Software Development" (Stable Abstractions Principle)

### Formula

```
A = (abstract class count + interface count) / total type count
```

Computed at assembly granularity. 0.0 = all concrete, 1.0 = all abstract.

## Distance from Main Sequence (DfMS)

### Formula

```
D = |A + I - 1|

A = Abstractness
I = Instability (assembly granularity: sum of Ce / (sum of Ca + sum of Ce))
```

Distance from the Main Sequence line (A + I = 1). 0.0 is ideal.

| Position | Meaning |
|------|------|
| D ≈ 0 | Good balance of stability and abstractness |
| A=0, I=0 (D=1) | Stable and concrete → Zone of Pain (hard to change) |
| A=1, I=1 (D=1) | Unstable and abstract → Zone of Uselessness |

## Relational Cohesion (H)

Compliance specification: NDepend - Relational Cohesion

### Formula

```
H = (R + 1) / N

R = number of inter-type dependency edges in the assembly (deduplicated, self-reference excluded)
N = number of types in the assembly
```

null when N <= 1. Higher values indicate tighter collaboration among types in the assembly. Recommended range: 1.5–4.0.

## DOTS / ECS

unilyze does **not** duplicate Roslyn analyzers / source generators inside the `com.unity.entities` package (SGJE diagnostics: SystemAPI misuse, invalid Entities.ForEach chains, etc.).
Those fail the Editor build and therefore target problems that never reach CI. unilyze detects only what remains after compilation:

| Rule | Target | Intent |
|--------|------|------|
| UNI024 MissingBurstCompile | `ISystem` / `IJobEntity` / `IJobChunk` struct | ECS types eligible for Burst without `[BurstCompile]` |
| UNI025 ManagedReferenceInComponentData | `struct IComponentData` | Component struct with reference-type fields (`class IComponentData` excluded as intentional managed) |

`SystemBase`-derived classes are not Burst targets and are not reported by UNI024.

### Burst coverage (`burstCoverage`)

Assembly-level Burst adoption rate. JSON `.assemblies[].metrics.burstCoverage`.

```
eligible = count of ISystem struct + IJobEntity/IJobChunk struct
covered  = eligible types with [BurstCompile] on the type or all lifecycle methods
burstCoverage = covered / eligible
```

`burstCoverage` is null when eligible is 0 (same nullable-when-undefined pattern as `RelationalCohesion`).
`ecsTypeCount` is the total of ECS-classified types (`EcsSystem` / `EcsJob` / `EcsComponentData`). null when the assembly has no ECS types.

### Analysis-level dependency

| Level | ECS classification | UNI024/UNI025 |
|--------|----------|---------------|
| Complete (`Unity.Entities` resolvable via `Library/ScriptAssemblies`) | Validates `Unity.Entities` namespace | Semantic type detection (reference-type fields via `IsReferenceType`) |
| SyntaxOnly | Interface name match on base list | Non-Unity `ISystem` etc. with the same name may false-positive. UNI025 uses a conservative list (string/object/array/BCL collections, etc.) |

## Code Health

Proprietary metric. Type-level score (1.0 - 10.0).

### Weighting

| Element | Weight |
|------|------|
| Average CogCC | 25% |
| Max CogCC | 20% |
| Line count | 15% |
| Method count | 10% |
| Max nesting depth | 15% |
| Excessive parameter count | 15% |

## Code Smell

Detects known code smells with rule-based heuristics.

Smell detection is threshold-dependent heuristics, not ground truth.
Paiva, Damasceno, Figueiredo & Sant'Anna (2017) "On the evaluation of code smells and detection tools" (JSERD) report inter-tool agreement of 67–100%, recall 0–58%, precision 0–100%; threshold differences alone can split results.
Thresholds are listed in the table below (**default values**). Projects can override per smell kind via the `smells` section in `.unilyze.json`. Overrides affect runtime detection only; the table below remains the single source of truth for defaults.
For measured-value compatibility, see the [Metric Compatibility Policy](#metric-compatibility-policy).

### Inline suppression (`unilyze-disable`)

ESLint-style comments to silence a single occurrence. Can be combined with baseline (project freeze) or `rules` (rule-wide off); counted in root `suppressedCount` (same smell matching both counts once).

```csharp
// unilyze-disable-next-line UNI014 -- intentional guard
catch { }

// unilyze-disable UNI002
void LongButJustified() { /* ... */ }
```

| Form | Scope |
|------|----------|
| `unilyze-disable-next-line UNIxxx` | **Next line** after the comment (line-numbered detector smells such as UNI011–UNI025) |
| `unilyze-disable UNIxxx` (leading trivia on type/method declaration) | Within that declaration scope |

Omitting the rule ID suppresses all rules in scope. Unknown IDs and `UNI009` log a stderr warning and are ignored (analysis exit code unchanged). Suppressed smells remain in JSON with `"suppressed": true`; SARIF uses `suppressions: [{ "kind": "inSource" }]`. Excluded from statusline / badge / `diff --fail-on-regression`.

**Known constraints**

1. **Same-name overload indistinguishability (metric smells):** UNI001–UNI008, UNI010 match directives by method/type name, so all overloads with the same method name are suppressed. Detector smells (UNI011–UNI025) can be distinguished by line position.
2. **partial types:** The suppression index is built from one declaration indexed by `SyntaxLookups.BuildTypeDeclLookup`. Place type-scope directives on the indexed partial declaration (scanning all partials is a follow-up).
3. **UNI009:** Reported per dependency cycle; inline suppression not supported. `rules` only.

### Threshold profiles (`profile`)

Source: Aniche, Treude, Zaidman et al., "SATT: Tailoring Code Metric Thresholds for Different Software Architectures" (SCAM 2016) — metric distributions differ by architecture (role); a single threshold over-detects specific roles.

Select a built-in profile via `"profile"` in `.unilyze.json` or CLI `--profile`. CLI `--profile` overrides project config; project config overrides global config (same higher-scope-wins as `baseline`). **Threshold numeric values:** user overrides in the `smells` section take highest priority over `profile` base values (`profile` < user thresholds).

| Profile | Description |
|-------------|------|
| `default` (when omitted) | Global default thresholds (as before). `profile` is not emitted at JSON root. `metricsVersion` unchanged. |
| `unity` | Role-specific thresholds for Unity. JSON root records `"profile": "unity"`. |

#### Type roles (`role`)

Each type has `role` in JSON `types[]` (camelCase enum):

| `role` | Detection |
|--------|------|
| `MonoBehaviour` | Base-type chain includes `UnityEngine.MonoBehaviour` |
| `ScriptableObject` | Base-type chain includes `UnityEngine.ScriptableObject` |
| `EditorExtension` | Base is `UnityEditor.Editor` / `EditorWindow`, or `[CustomEditor]` attribute |
| `PlainCSharp` | None of the above |

**SyntaxOnly limitation:** Syntactic fallback matches only the direct base name in `TypeNodeInfo.BaseType`. Indirect derivation such as `Player : BaseView` (`BaseView : MonoBehaviour`) is not classified as `MonoBehaviour` without Semantic analysis.

#### Role-specific thresholds for `unity` profile (provisional)

Provisional values from literature (SATT SCAM 2016; Alves ICSM 2010). Final values planned from role-specific distributions via `unilyze calibrate` (#86).

| Role | GodClass (Warning) | Notes |
|--------|-------------------|------|
| `MonoBehaviour` | lines >= 800 or methods >= 30 | Relaxed to reduce over-detection from lifecycle method proliferation (Nardone et al. TOSEM 2023) |
| `ScriptableObject` | lines >= 650 or methods >= 25 | |
| `EditorExtension` | lines >= 700 or methods >= 25 | |
| `PlainCSharp` | Same as default table | |

Other smells (LongMethod, HighCoupling, etc.) use the default table for all roles except `PlainCSharp` for now.

#### LowCohesion informational handling (`unity` profile)

Source: Palomba, Bavota, Di Penta, Oliveto, De Lucia, "Do They Really Smell Bad?" (ICSME 2014) — cohesion smells have low developer problem recognition.

Under the `unity` profile, `LowCohesion` (UNI006) is **not emitted as a warning smell**; when the threshold is met, it is added to `typeMetrics[].informationalCount`. Not included in `badge --fail-over` or `diff --fail-on-regression` warning counts. Under `default`, remains Warning as before.

`unilyze metrics --profile unity` displays the active profile and role-specific thresholds.

<!-- smell-thresholds:start -->
| Smell | Warning condition | Critical condition |
|--------|-------------------|-------------------|
| GodClass | lines >= 500 or methods >= 20 | lines >= 1000 |
| LongMethod | lines >= 80 or CogCC >= 25 | lines >= 150 or CogCC >= 40 |
| ExcessiveParameters | parameter count > 5 | — |
| HighComplexity | CycCC >= 15 or CogCC >= 15 | — |
| DeepNesting | nesting depth >= 4 | nesting depth >= 6 |
| LowCohesion | LCOM >= 0.8 | — |
| HighCoupling | CBO >= 15 | CBO >= 25 |
| LowMaintainability | MI < 60 | — |
| DeepInheritance | DIT >= 5 | — |
| CatchAllException | `catch (Exception)` without rethrow (excluding `when` filtered catches) | — |
| AsyncVoidMethod | `async void` method (excluding Unity message methods and event handlers) | — |
| BlockingTaskWait | `.Result` / `.Wait()` / `.GetAwaiter().GetResult()` on Task/ValueTask/UniTask | — |
| MissingBurstCompile | `ISystem` / `IJobEntity` / `IJobChunk` struct without `[BurstCompile]` | — |
| ManagedReferenceInComponentData | `struct IComponentData` with reference-type field | — |
<!-- smell-thresholds:end -->

### Unity hot-path severity escalation

`BoxingAllocation`, `ClosureCapture`, and `ParamsArrayAllocation` are normally Warning-level smells. When the enclosing type derives from `UnityEngine.MonoBehaviour` and the smell occurs inside a Unity hot-path method, severity escalates to Critical.

Hot-path methods are:

- `Update`, `FixedUpdate`, `LateUpdate`, `OnGUI`
- Coroutines: methods whose return type is `System.Collections.IEnumerator`

Lifecycle methods such as `Awake`, `Start`, `OnEnable`, `OnDisable`, and `OnDestroy` are **not** hot paths and keep Warning severity.

Escalation rewrites only `Severity` (Warning → Critical); `Kind` is unchanged so boxing/closure/params counts and CodeHealth are unaffected.

#### SyntaxOnly caveats

Under `SyntaxOnly` analysis:

- **ClosureCapture only:** Boxing and Params require a `SemanticModel` and emit nothing under SyntaxOnly, so hot-path escalation applies to ClosureCapture only.
- **MonoBehaviour detection:** the syntactic fallback matches the direct base-list type name against `MonoBehaviour` and cannot see through intermediate project base classes (e.g. `Player : BaseView` where `BaseView : MonoBehaviour` is not recognized without semantic resolution).

### Energy proxy (hot-path smell density)

`energyPressure` is a static proxy derived from source-code smell counts.
It is not measured energy, power, battery consumption, joules, or watts.

For Unity projects with at least one detected hot-path method:

```text
energyPressure = hot-path performance smell count / Unity hot-path method count
```

The numerator counts UNI017-UNI021 plus `BoxingAllocation`, `ClosureCapture`, and `ParamsArrayAllocation` when they occur in a detected hot path.
UNI022 and UNI023 are excluded because they are correctness and responsiveness findings, not per-frame work indicators.
Suppressed and gate-excluded triage findings are excluded; `--baseline` also excludes baselined findings from badge gates.

The denominator is the number of distinct hot-path method names detected on `MonoBehaviour` types.
It includes `Update`, `FixedUpdate`, `LateUpdate`, `OnGUI`, and coroutine methods returning `IEnumerator`.
Overloads with the same method name are merged, and the analysis does not follow transitive call graphs.

The field is emitted only for Unity projects with a non-zero denominator.
`badge --metric energy` reports `n/a` for non-Unity projects or Unity projects without hot paths.
`--fail-over <density>` passes at or below the threshold and fails above it.
Badge colors are provisional: `0` bright green, below `1.0` yellow, and `1.0` or above red.

The proxy is independent of CodeHealth and is not included in CodeHealth aggregation.
Because Boxing and Params detection requires a `SemanticModel`, values from different `analysisLevel` values are not directly comparable.
Old trend snapshots without energy counts render as missing (`-` or chart gaps), not as `0.00`.

Research grounding: Pérez Caseiras, Veron, Perez, Moraga, Calero, and Cetina, "Towards green game software engineering: A comparative analysis of energy consumption between the widespread Unity and Unreal video game engines", Information and Software Technology (2025), arXiv:2402.06346.
The study shows that code and engine implementation choices can produce measurable energy differences, but it does not calibrate this static density to physical energy units.

### Detection responsibility routing

Each smell's detection responsibility is split between deterministic rule detection (structural, graph, semantic) and LLM delegation (semantic intent judgment).
Souza et al. (arXiv:2601.09873) report that the optimal detector differs by smell kind: structural smells favor deterministic rules; semantic smells favor LLMs.
Wu, Mu et al. (iSMELL, ASE 2024) report that combining metric tools with LLMs outperforms LLM-only approaches, supporting a split between deterministic detection and LLM interpretation.
See [quality-audit blind-spots](https://github.com/bigdra50/unilyze/blob/main/src/Unilyze/Skills/quality-audit/references/blind-spots.md) for LLM-delegated items; confirm in the Phase 3 checklist.

| Smell | SARIF rule | Detection responsibility | Rationale |
|--------|-------------|---------|------|
| GodClass | UNI001 | Rule detection (metric threshold) | Structural; stable threshold detection |
| LongMethod | UNI002 | Rule detection (metric threshold) | Structural; stable threshold detection |
| ExcessiveParameters | UNI003 | Rule detection (metric threshold) | Structural; stable threshold detection |
| HighComplexity | UNI004 | Rule detection (metric threshold) | Structural; stable threshold detection |
| DeepNesting | UNI005 | Rule detection (metric threshold) | Structural; stable threshold detection |
| LowCohesion | UNI006 | Rule detection (metric threshold) | Structural; stable threshold detection |
| HighCoupling | UNI007 | Rule detection (metric threshold) | Structural; stable threshold detection |
| LowMaintainability | UNI008 | Rule detection (metric threshold) | Structural; stable threshold detection |
| CyclicDependency | UNI009 | Rule detection (graph analysis) | Dependency graph analysis required |
| DeepInheritance | UNI010 | Rule detection (metric threshold) | Structural; stable threshold detection |
| BoxingAllocation | UNI011 | Rule detection (semantic analysis) | SemanticModel required |
| ClosureCapture | UNI012 | Rule detection (semantic analysis) | SemanticModel required |
| ParamsArrayAllocation | UNI013 | Rule detection (semantic analysis) | SemanticModel required |
| CatchAllException | UNI014 | Rule detection (semantic analysis) | SemanticModel required |
| MissingInnerException | UNI015 | Rule detection (semantic analysis) | SemanticModel required |
| ThrowingSystemException | UNI016 | Rule detection (semantic analysis) | SemanticModel required |
| AsyncVoidMethod | UNI022 | Rule detection (syntax + semantic analysis) | Detects async void; excludes Unity message methods and event handlers |
| BlockingTaskWait | UNI023 | Rule detection (syntax + semantic analysis) | Blocking wait on Task/ValueTask/UniTask; SyntaxOnly detects GetAwaiter().GetResult() only |
| MissingBurstCompile | UNI024 | Rule detection (syntax + semantic analysis) | Missing `[BurstCompile]` on `ISystem`/`IJobEntity`/`IJobChunk` struct. Complete validates `Unity.Entities` namespace; SyntaxOnly matches interface names (non-Unity interfaces with the same name may false-positive) |
| ManagedReferenceInComponentData | UNI025 | Rule detection (syntax + semantic analysis) | Reference-type fields in `struct IComponentData`; `class IComponentData` excluded. SyntaxOnly uses conservative list (string/object/array/BCL collections, etc.) |
| WeakTemporization | UNI021 | Rule detection (syntax analysis, semantic enrichment) | SyntaxOnly capable |
| ExpensiveUnityApiInHotPath | UNI017 | Rule detection (Unity hot-path syntax analysis) | Unity-specific; MonoBehaviour per-frame methods only |
| LinqInHotPath | UNI018 | Rule detection (Unity hot-path syntax analysis) | Unity-specific; MonoBehaviour per-frame methods only |
| CollectionAllocationInHotPath | UNI019 | Rule detection (Unity hot-path syntax analysis) | Unity-specific; MonoBehaviour per-frame methods only |
| StringConcatenationInHotPath | UNI020 | Rule detection (Unity hot-path syntax analysis) | Unity-specific; MonoBehaviour per-frame methods only |
| Feature Envy | — | LLM delegation | Requires intent/context judgment; not thresholdable |
| Naming quality | — | LLM delegation (inputs: `--include-api-surface` identifiers / publicSignatures) | Requires intent/context judgment; not thresholdable |
| Intent–code divergence | — | LLM delegation (inputs: `--include-api-surface` docSummary / identifiers) | Requires intent/context judgment; not thresholdable |
| Comment–code inconsistency | — | LLM delegation (inputs: `--include-api-surface` docSummary / publicSignatures) | Requires intent/context judgment; not thresholdable |
| Top-level statements | — | LLM delegation | Requires intent/context judgment; not thresholdable |
| Runtime risk (Dispose leak / deadlock) | — | LLM delegation | Requires intent/context judgment; not thresholdable |

## Validation

### Complete vs SyntaxOnly analysis differences

When Unity DLLs cannot be resolved (CI environments, etc.), analysis falls back to SyntaxOnly.
Measured differences on the same real project (oculus-samples/Unity-Decommissioned, 283 types) analyzed at Complete and SyntaxOnly:

| Metric | Complete | SyntaxOnly | Notes |
|------|----------|------------|------|
| CodeHealth avg | 9.6 | 9.6 | Identical (computed from syntax information only) |
| CodeHealth min | 4.8 | 5.0 | Difference due to `#if UNITY_EDITOR` define presence |
| Dependency count | 452 | 429 | -5% |
| Cyclic dependencies | 6 | 6 | Identical |
| Total smells | 885 | 289 | Breakdown in table below |
| DIT max | 7 | 1 | Inheritance spanning engine types requires semantic analysis |
| CBO avg | 13.7 | 5.7 | Coupling to UnityEngine types invisible |
| Analysis time | 4.6s | 0.6s | |

Smell breakdown differences:

| Smell | Complete | SyntaxOnly |
|--------|----------|------------|
| BoxingAllocation | 312 | 0 |
| ClosureCapture | 181 | 81 |
| ParamsArrayAllocation | 30 | 0 |
| DeepInheritance | 38 | 0 |
| HighCoupling | 111 | 19 |

Under SyntaxOnly, SemanticModel-dependent detection (Boxing / Params / DIT / CBO) is underestimated.
Therefore `unilyze badge` targets CodeHealth / MI (stable across levels) and smells limited to the syntax-level subset.
As shown above, total smell counts vary greatly across levels (885 → 289); documentation states that smell badges must not be used for cross-level comparison.

### Cross-validation with Microsoft.CodeAnalysis Metrics (official engine)

Measured src/Unilyze with the official Metrics tool's `CodeAnalysisMetricData` (Microsoft.CodeAnalysis.AnalyzerUtilities) and cross-validated against unilyze SyntaxOnly analysis
(100 type matches; 2 JsonSerializerContext types from source generators excluded. Reproduction: [scripts/crossval](https://github.com/bigdra50/unilyze/blob/main/scripts/crossval/)):

| Metric | Pearson correlation | Mean absolute difference | Notes |
|------|-------------|-----------|------|
| CycCC | 0.983 | 2.0 | Compared as type-level totals. Divergence explainable by convention differences for 97/100 types (below) |
| MI | 0.870 | 5.4 | Official aggregates at type level; unilyze uses method average (43 types without methods are not MI targets in unilyze; statusline / badge MI average also uses only types with methods as denominator) |
| Coupling | 0.817 (rank) | — | Official ClassCoupling avg 14.0 vs unilyze CBO 3.6 (underestimated under SyntaxOnly) |
| DIT | — | — | Official counts `object` inheritance as 1; uniform offset on all items |

Structure of CycCC divergence (proven on all 339 methods; investigation completed in issue #4):

Divergence Δ = unilyze − official decomposes strictly by convention-difference syntax occurrences (97/100 types: Δ = arm + catch + goto − default − member base difference matches exactly).
Top divergent types and decomposition:

| Type | Official | Unilyze | Δ | Breakdown |
|----|------|---------|---|------|
| BadgeFormatter | 14 | 28 | +14 | switch arm ×14 |
| HalsteadCalculator | 16 | 30 | +14 | switch arm ×14 |
| DIContainerAnalyzer | 42 | 55 | +13 | switch arm ×14 − default ×1 |
| BadgeSvgRenderer | 12 | 22 | +10 | switch arm ×10 |
| ClosureDetector | 30 | 40 | +10 | switch arm ×10 |
| BloomFilter128 | 26 | 17 | −9 | Official member base (ctor / accessor each 1) ×9 |

Record / DTO types without methods uniformly have Δ = −1 (official counts implicit ctor as base 1).
The 3 types with residual (HalsteadWalker / State / Walker) differ by ±1 due to bool `&` `|` not resolvable under SyntaxOnly and nested-type member name matching.

The pre-investigation hypothesis that "`?.` `??` are unilyze-specific additions" was disproved (the official engine also counts both).
Actual divergence factors are switch expression arms (dominant in this codebase), catch, goto, default, and member base differences.
As a by-product of this investigation, a bug was found and fixed: deconstruction foreach (`foreach (var (a, b) in ...)`) was not counted in CycCC / CogCC / nesting-depth walkers.

## Threshold Calibration (`unilyze calibrate`)

Source: Alves, Ypma & Visser, "Deriving Metric Thresholds from Benchmark Data", ICSM 2010.

### Same-tool principle (Alves Section VII-D)

Thresholds must be derived with the same tool and scope used at application time. unilyze CycCC uses the extended interpretation counting switch expression arms and `catch` as branches (see Cyclomatic Complexity notes above); CA1502's default 25 and benchmark values from other tools cannot be reused directly. `calibrate` accepts only JSON snapshots produced by unilyze itself, keeping derivation and analysis within one tool.

### Procedure

1. Prepare multiple systems (one `unilyze -f json` snapshot each). All inputs must share the same `metricsVersion` (error exit on mismatch).
2. Within each system, weight by method LOC ratio (weight = method LOC / total method LOC in that system).
3. Across systems, divide weights by system count so each system contributes equally (large repos do not dominate the distribution).
4. Read percentiles from the pooled weighted distribution to obtain four risk-band boundaries (low / moderate / high / veryHigh).
   - Normal metrics: 70 / 80 / 90 percentiles
   - Parameter count: 80 / 90 / 95 percentiles (per paper)

Target metrics:

| Category | Metrics | Used for |
|------|-----------|------|
| Method | LOC, CycCC, CogCC, max nesting depth, parameter count | LongMethod / HighComplexity / DeepNesting / ExcessiveParameters |
| Type | method count, type LOC | GodClass |

### CLI

```bash
unilyze calibrate <dir-of-jsons> [-o thresholds.json]
```

Output JSON includes `metrics` (percentiles and risk bands per metric), `sources` (input file names, method counts, etc.), and `unilyzeConfigFragment` (candidates pasteable into `.unilyze.json` `smells`). Built-in defaults (`SmellThresholds`) are unchanged. Applying calibration results is left to project configuration or future release decisions.

### Limitations

The Alves paper uses benchmarks of roughly 100 systems. unilyze's validation Unity OSS corpus (HelloMarioFramework, Boss Room, UniTask, VContainer, etc.) is small; derived values are provisional candidates. Re-run with the same procedure when the corpus grows.

## Code Duplication (dup)

`unilyze dup` produces a duplication report independent of the main analysis JSON. Integration into CodeHealth is planned for CodeHealth v2.

### Normalization

Scan Roslyn token sequences per `.cs` file and normalize with the following rules.

| Token kind | Normalization |
|-------------|--------|
| Identifiers | `ID` |
| Numeric/string/char/bool literals, interpolated string text | `LIT` |
| Keywords, operators, punctuation | As-is |
| Trivia (whitespace, comments) | Excluded |

Detects Type-2 (identifier/literal substitution) and Type 3-2 equivalent (identifier/literal blind) clones. Known limitation: statement insertion/deletion can split matches.

### Detection algorithm

- Minimum window: 100 tokens (default; override via `--min-tokens` or `.unilyze.json` `dup.minTokens`; CLI takes priority)
- Rabin-Karp rolling hash buckets candidates; token-sequence exact match on hash collision
- Extend matching windows bidirectionally; merge overlaps within the same file

### Duplication rate

`duplicationPercent` = union of lines covered by clones / total lines in analyzed files × 100. Line-based density like SonarQube Duplicated Lines %.

### Third-party suppression

Default third-party roots: `Assets/Plugins`, `Assets/Standard Assets`, `Assets/AssetStoreTools` (extendable via `--third-party-dir` and `.unilyze.json` `dup.thirdPartyDirs`).

Pairs where both ends are within the same third-party root are suppressed by default and counted in `suppressedPairCount`. first-party ↔ third-party pairs are always reported. Disable suppression with `--include-third-party`.

### CI gate

```bash
unilyze badge -p . --metric dup --fail-over 3   # exit 2 when > 3%
```

Badge colors: green < 3%, yellow 3–10%, red ≥ 10% (aligned with SonarQube default 3% quality gate).

### Full operation under SyntaxOnly

`dup` consumes `SyntaxTree` only; no dependency on `Compilation` / `SemanticModel`. Same results in CI without Unity DLLs as at Complete level.

### Known noise

Windows dominated by `LIT` tokens (large array initializers, etc.) may detect data tables as clones.

## Assembly Mapping

Assembly-granularity metrics (Abstractness, Instability, Distance from Main Sequence, Relational Cohesion, assembly cycles) require splitting the analysis scope into assembly units before aggregation.

| Project kind | Split unit | Dependency edges |
|------------------|----------|------------|
| Unity | `.asmdef` `name` (under `Assets/`) | asmdef `references` (GUID resolution) |
| General .NET (no asmdef, non-Unity) | Discovered `.csproj` file name (extension stripped) | `ProjectReference` |
| Unity without asmdef or csproj | Single `Assembly-CSharp` | None |

Nested csproj directories add child csproj directories to the parent assembly's `ExcludeDirectories` so each `.cs` file belongs to exactly one assembly (same as nested asmdef). When loose `.cs` files exist outside all csproj directories, an `Assembly-CSharp` fallback is added.

`typeId` uses `{assembly}::{namespace.path}` format; in general .NET repos the assembly prefix changes after csproj mapping (e.g. `Assembly-CSharp::Foo.Bar` → `MyApp::Foo.Bar`). Unity (with asmdef) and paths without csproj/asmdef remain as before.

## Metric Compatibility Policy

Multiple patch releases have changed measured values via bugfixes (deconstruction foreach count omission fix, excluding method-less types from MI average denominator, removing DIT `I[A-Z]` heuristic, etc.).
These are tool-side measurement fluctuations that add noise for users of `diff` / `trend` / `badge` across versions.
The following policy clarifies which release kinds may change measured values.

Metric definition changes must be recorded in [CHANGELOG.md](https://github.com/bigdra50/unilyze/blob/main/CHANGELOG.md) with the `[metrics]` prefix (see the [metrics] tag convention in that file).

### Measured-value handling by release kind

| Release kind | Measured-value changes | Permitted changes |
|------------|------------|--------------|
| patch | None | Crash fixes, output-format additions (new fields/formats that do not change existing values) |
| minor and above | May occur | Metric definition changes (below) |

Metric definition changes are any of:

- Counting conventions (which syntax to add; e.g. deconstruction foreach, switch expression arm)
- Numerator/denominator composition (e.g. type set for MI average)
- Thresholds (code-smell Warning / Critical boundaries, etc.)
- Composite score weights (Code Health element weights, etc.)

These changes require at least a minor bump.
Patch releases must not change measured values.

### Procedure when changing definitions

Releases that change metric definitions must:

1. Document measurement impact in release notes: which metrics move in which direction (increase / decrease / value-range change)
2. Re-run [scripts/crossval](https://github.com/bigdra50/unilyze/blob/main/scripts/crossval/) cross-validation and update validation data in this document's [Validation](#validation) section
3. Update any convention-difference descriptions (e.g. "differences from official engine") to match the change

### Notes for users

`diff` / `trend` are intended to track changes in analyzed code.
However, when the two comparison points were measured with different unilyze versions, metric definition changes (possible in minor and above) can mix in.
If values move without code changes, suspect an unilyze version difference.
Pinning the unilyze version eliminates this effect.

### Mechanical detection via metricsVersion

JSON output root includes `metricsVersion` (int) and `toolVersion` (string).
`metricsVersion` is an integer representing measurement-definition compatibility; increment on every change that alters measured values.
`toolVersion` is the unilyze assembly version at snapshot generation.

`diff` and `trend` emit a one-line stderr warning when `metricsVersion` differs between inputs.
`diff --fail-on-version-mismatch` exits with code 2 on version mismatch (for CI gates).

**metricsVersion increment rules:** Any change that alters measured values (counting conventions, numerator/denominator, thresholds, weights) requires
(1) increment `AnalysisResult.CurrentMetricsVersion`, (2) at least minor bump, (3) add CHANGELOG `[metrics]` entry,
as a set. Do not raise metricsVersion in a patch release.

Within the same release window (`[Unreleased]` period), bundle `metricsVersion` bumps into one increment.
Definitions for unreleased version numbers are fluid; multiple `[metrics]` changes before tagging do not trigger renumbering.

### Reference analysis opt-ins (NuGet / generated code)

`--resolve-nuget` and `--include-generated` are off by default. Enabling them can resolve package types and source-generator output for semantic metrics such as CBO / DIT / boxing, which may change values. `metricsVersion` does not increase (default output remains byte-identical).

- `--resolve-nuget` / `"resolveNuget": true` — inject NuGet compile assemblies from `obj/project.assets.json` (after `dotnet restore`). Used with BCL runtime injection (#84).
- `--include-generated` / `"includeGenerated": true` — add `obj/<Config>/<TFM>/generated/**/*.cs` from `EmitCompilerGeneratedFiles=true` builds to compilation only (not included in type count, LineCount, or smells).
- `--tfm` / `"targetFramework"` — explicit single TFM. When omitted: first `TargetFramework(s)` in csproj, then running runtime, then highest version.

JSON output echoes active opt-in settings (`resolveNuget`, `includeGenerated`, `targetFramework`). `diff` / `trend` / baseline comparison is valid only between snapshots with **identical opt-in settings**.

### Future work

(Resolved: issue #30 implemented `metricsVersion` / `toolVersion`. See "Mechanical detection via metricsVersion" above.)

# Metrics Thresholds

> CLI から `unilyze metrics` で定義・検出閾値を確認可能。以下はレーティング基準の補足。

## CodeHealth

| Range | Rating | Action |
|-------|--------|--------|
| >= 8.0 | Good | Maintain |
| 5.0 - 7.9 | Warning | Monitor, improve when possible |
| < 5.0 | Poor | Prioritize refactoring |

## Cognitive Complexity (CogCC)

| Range | Rating |
|-------|--------|
| <= 10 | Good |
| 11 - 24 | Warning |
| >= 25 | Poor |

## Cyclomatic Complexity (CycCC)

| Range | Rating |
|-------|--------|
| <= 10 | Good |
| 11 - 19 | Warning |
| >= 20 | Poor |

## LCOM-HS

| Range | Rating |
|-------|--------|
| <= 0.5 | Good (cohesive) |
| 0.5 - 0.8 | Warning |
| > 0.8 | Poor (split class) |

## CBO (Coupling Between Objects)

| Range | Rating |
|-------|--------|
| <= 13 | Good |
| 14 - 24 | Warning |
| >= 25 | Poor |

## Maintainability Index (MI)

| Range | Rating |
|-------|--------|
| >= 40 | Good |
| 20 - 39 | Warning |
| < 20 | Poor |

## Lines

| Target | Good | Warning | Poor |
|--------|------|---------|------|
| Method | <= 30 | 31-60 | > 60 |
| Type | <= 300 | 301-500 | > 500 |

## WMC (Weighted Methods per Class)

| Range | Rating |
|-------|--------|
| <= 20 | Good |
| 21 - 40 | Warning |
| > 40 | Poor |

## RFC (Response For a Class)

| Range | Rating |
|-------|--------|
| <= 50 | Good |
| 51 - 80 | Warning |
| > 80 | Poor |

## NOC (Number of Children)

Higher NOC means more subclasses depend on this type. No fixed threshold; context-dependent.

## TypeRank

Normalized PageRank score (sum = 1.0). Higher = more depended upon. Top 5% are critical infrastructure types.

## Assembly Metrics

| Metric | Ideal | Concern |
|--------|-------|---------|
| Abstractness (A) | 0.2 - 0.8 | 0.0 (Zone of Pain) or 1.0 (Zone of Uselessness) |
| DfMS | 0.0 | > 0.5 |
| Relational Cohesion (H) | 1.5 - 4.0 | < 1.0 (loosely related) or > 4.0 (over-coupled) |

## CodeSmell Severity

| Kind | Severity |
|------|----------|
| GodClass | Critical/Warning |
| LongMethod | Critical/Warning |
| HighComplexity | Critical/Warning |
| HighCoupling | Critical/Warning |
| LowMaintainability | Critical/Warning |
| ExcessiveParameters | Warning |
| DeepNesting | Warning |
| LowCohesion | Warning |
| DeepInheritance | Warning |
| BoxingAllocation | Warning |
| ClosureCapture | Warning |
| ParamsArrayAllocation | Warning |
| CatchAllException | Warning |
| MissingInnerException | Warning |
| ThrowingSystemException | Warning |

## Metric Direction

Higher-is-better: CodeHealth, MaintainabilityIndex, TypeRank
Lower-is-better: CogCC, CycCC, LCOM, CBO, DIT, WMC, RFC, LineCount, BoxingCount, ClosureCaptureCount

# Metrics Thresholds

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

## Metric Direction

Higher-is-better: CodeHealth, MaintainabilityIndex
Lower-is-better: CogCC, CycCC, LCOM, CBO, DIT, LineCount

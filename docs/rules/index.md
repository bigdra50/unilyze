<!-- docsgen:start -->
# Rules

Unilyze reports the following SARIF rules.

| ID | Name | Severity entry points | Tags | Link |
|----|------|-----------------------|------|------|
| UNI001 | God class detected | Warning: lines >= 500 or methods >= 20; Critical: lines >= 1000 | `maintainability` | [Details](UNI001.md) |
| UNI002 | Long method detected | Warning: lines >= 80 or CogCC >= 25; Critical: lines >= 150 or CogCC >= 40 | `maintainability` | [Details](UNI002.md) |
| UNI003 | Excessive parameters | Warning: parameter count > 5 | `maintainability` | [Details](UNI003.md) |
| UNI004 | High complexity | Warning: CycCC >= 15 or CogCC >= 15 | `maintainability` | [Details](UNI004.md) |
| UNI005 | Deep nesting | Warning: nesting depth >= 4; Critical: nesting depth >= 6 | `maintainability` | [Details](UNI005.md) |
| UNI006 | Low cohesion | Warning: LCOM >= 0.8 | `maintainability` | [Details](UNI006.md) |
| UNI007 | High coupling | Warning: CBO >= 15; Critical: CBO >= 25 | `maintainability` | [Details](UNI007.md) |
| UNI008 | Low maintainability | Warning: MI < 60 | `maintainability` | [Details](UNI008.md) |
| UNI009 | Cyclic dependency | Warning | `maintainability` | [Details](UNI009.md) |
| UNI010 | Deep inheritance hierarchy | Warning: DIT >= 5 | `maintainability` | [Details](UNI010.md) |
| UNI011 | Boxing allocation detected | Warning; Critical in Unity hot paths | `performance`, `gc-pressure` | [Details](UNI011.md) |
| UNI012 | Closure variable capture detected | Warning; Critical in Unity hot paths | `performance`, `gc-pressure` | [Details](UNI012.md) |
| UNI013 | Implicit params array allocation | Warning; Critical in Unity hot paths | `performance`, `gc-pressure` | [Details](UNI013.md) |
| UNI014 | Catch-all exception without rethrow | Warning | `reliability`, `exceptions` | [Details](UNI014.md) |
| UNI015 | Missing inner exception in rethrow | Warning | `reliability`, `exceptions` | [Details](UNI015.md) |
| UNI016 | Throwing System.Exception directly | Warning | `reliability`, `exceptions` | [Details](UNI016.md) |
| UNI017 | Expensive Unity API in hot path | Warning | `performance`, `unity` | [Details](UNI017.md) |
| UNI018 | LINQ in hot path | Warning | `performance`, `unity` | [Details](UNI018.md) |
| UNI019 | Collection allocation in hot path | Warning | `performance`, `unity` | [Details](UNI019.md) |
| UNI020 | String concatenation in hot path | Warning | `performance`, `unity` | [Details](UNI020.md) |
| UNI021 | Frame-rate dependent update | Warning | `performance`, `unity` | [Details](UNI021.md) |
| UNI022 | async void method | Warning | `reliability`, `async` | [Details](UNI022.md) |
| UNI023 | Blocking wait on Task | Warning | `reliability`, `async` | [Details](UNI023.md) |
| UNI024 | Missing BurstCompile on ECS type | Warning | `performance`, `unity`, `dots` | [Details](UNI024.md) |
| UNI025 | Managed reference in IComponentData | Warning | `performance`, `unity`, `dots` | [Details](UNI025.md) |
<!-- docsgen:end -->

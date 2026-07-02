# #216 Semantic incremental — measurement

Records the warm-edit measurement for the semantic-incremental serve path (issue #216), per the
#203 measurement gate and the e882abd "no wall-clock timing assertions in CI" lesson.

## Method

- Corpus: the unilyze analyzer's own `src/Unilyze` (224 `.cs` files, 395 types) — a real .NET
  codebase available in-repo. (The #203 Unity corpora — UnityReversi / DragonCrashers / wallhackar
  — are not in this repo; rerun there when available, the numbers should be more favourable because
  those projects carry heavier per-type smell/MonoBehaviour enrichment.)
- `analyze -p <copy> --level core -f json` (semantic level), median of 5 runs.
- Warm = cache seeded, then a body-only edit (append a comment to one method) before each run, so
  exactly one type is re-enriched.
- Net10.0 Release build, Apple silicon.

## Result (work elision — the gating, non-flaky signal)

After a body-only edit, the incremental run re-enriches exactly the edited type:

```
[incremental] re-enrich types: 1/395
```

This is asserted deterministically in `SemanticIncrementalEquivalenceTests.BodyOnlyEdit_ReEnrichesOnlyTheEditedType`
(and the structural fallback in `SignatureChange_ForcesFullReEnrich`). No wall-clock assertion gates CI.

## Result (wall clock — reported, non-gating)

| | median |
| --- | --- |
| full (non-incremental) | 4021 ms |
| warm incremental (1 type re-enriched) | 3190 ms |
| warm / full | 79.3 % (≈21 % faster, 832 ms saved) |

Per-phase (single warm run, via the pipeline phase timers):

| phase | full | warm incremental |
| --- | --- | --- |
| parse | 0.2 s | 0.4 s |
| compile | 0.0 s | 0.0 s |
| semantic | 3.6 s | 2.5 s |
| aggregate | 0.0 s | 0.0 s |

In a live `serve` session the effect is larger because the process and reference metadata are
already warm: a body-only edit re-analyzed in **85 ms** (gen 4) vs **886 ms** on the cold initial
build (gen 2), with `[incremental] re-enrich types: 1/2`.

## Interpretation & follow-ups

The win comes entirely from eliding `SemanticEnricher.Enrich` (per-type LCOM/CBO/DIT/RFC + smell /
feature detection) for the unchanged types — about 1.1 s of the 3.6 s semantic phase here. The
remaining semantic cost is the full-set work that v1 deliberately keeps full for correctness:
`BaseTypeResolver` and `TypeRoleStamper` bind every type against the compilation each generation,
and the dependency/coupling graph + global aggregation are always rebuilt full. That is why the
self-analysis figure is ~21 % rather than the order-of-magnitude a "1/395 re-enriched" count might
suggest.

Deferred follow-ups that would capture more of the semantic phase (out of #216 v1 scope, which is
local-enrichment reuse with full aggregation):

- Reuse cached resolved base/interface relationships and type roles for unchanged types on the
  body-only fast path (skip `BaseTypeResolver` / `TypeRoleStamper` per-type binding).
- Keep parsed syntax trees and loaded `MetadataReference`s warm in-process across serve generations
  (today each generation reparses unchanged files and rebuilds the compilation).
- Incremental global aggregation (rank/cycles), as #216 itself notes for a later issue.

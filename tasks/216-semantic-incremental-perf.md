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

## Track-2 rerun — Unity corpus (wallhackar), RDI Phase A+B (2026-07-07)

Measured at the v0.6.0 code line (RDI Phase 0+A+B merged) on the wallhackar corpus — 1,994 types,
1,095 scanned files, 33 assemblies — with `analyze -p <copy> --level core -f json`, median of 5,
Release net10.0, Apple silicon. Edit target: a first-party type with 5 recorded dependents
(`Assets/RemoteScanner/Scripts/Meshing/MeshCreator.cs`).

| case | median | vs full | re-enrich |
| --- | --- | --- | --- |
| full (non-incremental) | 14757 ms | 100 % | — |
| warm body-only edit | 11380 ms | 77.1 % | 1/1994 |
| warm member add | 12415 ms | 84.1 % | 6/1994 `(rdi: members=1)` |
| warm signature modify | 11374 ms | 77.1 % | 6/1994 `(rdi: sig=1)` |

Zero full-re-enrich fallbacks across every warm run — all structural edits took the precise
(RDI) path. Work elision is the headline result: structural edits that re-enriched 1994/1994
under the v1 binary classifier now re-enrich 6/1994 (the edited type plus its recorded
dependents). The remaining wall-clock gap to full (only ~15–23 % saved) is bounded by the
known full-every-generation floor — `BaseTypeResolver`, `TypeRoleStamper`, dependency-graph
build, DI analysis, and aggregation — i.e. the Phase C candidates in
`tasks/reverse-dependency-index-design.md` §6; the `SemanticEnricher` slice itself is now
~0.3 % of types on a structural edit. Phase C is where the next order of magnitude lives.

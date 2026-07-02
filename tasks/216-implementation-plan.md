# Implementation Plan — #216 Semantic Incremental for the Live Serve Loop (closes epic #217)

> Source: dynamic workflow (7 parallel subsystem readers → 3 design approaches → judge → synthesis),
> code-grounded against `main` @ c5576e5. Gated GO by #203 measurement.

## 1. Goal & verdict recap

We make the live `serve` loop re-enrich only the types whose semantic metrics could have actually
changed, instead of running a full semantic enrichment of every type on every source edit. This is GO
because #203 measured analysis at 94-97% of warm-edit cost (large warm edit 5042ms, medium 1503ms) and
the per-type `SemanticEnricher` work (LCOM/CBO/DIT/RFC + smell detectors over method bodies) is the
dominant slice of that analysis phase; #214 (transfer) and #215 (rebuild) are NO-GO, so this is the only
remaining Phase 2 work. We ship by generalizing the already-shipped, already-equivalence-tested
`SyntaxIncrementalSemanticPhase`/`SyntaxIncrementalCollector` machinery to semantic analysis levels,
adding a reverse-dependent re-enrichment closure plus a conservative whole-assembly/whole-project
fallback, and flipping serve from `incremental:false` to `incremental:true`. Global aggregation
(rank/cycles/NOC/assembly health) stays FULL, so the result is observationally identical to a clean full
analysis. Expected impact: warm-edit analysis time on a large project drops materially (target < 70% of
the full baseline) for the common case of editing one type with few dependents, with zero change to the
produced `AnalysisResult`.

## 2. Chosen approach

Reuse the existing on-disk `TypeId`-keyed enrichment cache (`SyntaxIncrementalCollector` /
`SyntaxIncrementalSemanticPhase` / `SyntaxCacheStore`) verbatim, including its iron contract that the
incremental semantic phase ALWAYS rebuilds `deps` + `couplingMap` fully
(`SyntaxIncrementalSemanticPhase.cs:25-32`) and hands `AnalysisPipelineAggregation.Run` a complete,
snapshot-consistent 4-tuple (`AnalysisPipeline.cs:102-103`). Three changes turn this into a correct
semantic-level path:

- (a) make `SyntaxIncrementalCollector.Collect` return the COMPLETE syntax-tree set — cached-file trees
  reparsed/rehydrated alongside reparsed-file trees — so `CompilationFactory.Create` can build a real
  `CSharpCompilation` over all trees at semantic levels (today it returns only reparsed trees,
  `SyntaxIncrementalCollector.cs:33,165-190`, harmless only because `CompilationFactory` returns a null
  `Compilation` at syntax level, `CompilationFactory.cs:32-33`);
- (b) widen `DetermineTypesToReEnrich` (`SyntaxIncrementalSemanticPhase.cs:78`) from "reparsed files +
  partial closure" to "reparsed files + partial closure + transitive reverse-dependents over the full dep
  graph", with a conservative whole-assembly/whole-project fallback for the global-context changes the
  declaration-centric dep graph cannot see;
- (c) widen the gate (`AnalysisBuildOptions.UseSyntaxIncrementalCache`, `AnalysisBuildOptions.cs:39-40`,
  and the `AnalysisPipeline.Build` downgrade at `AnalysisPipeline.cs:42-47`) to permit semantic levels,
  then flip serve.

Grafts from the judge: fold resolved-reference DLL identity + TFM + effective `AnalysisLevel` into the
fingerprint, a per-assembly global-using-set hash as the first-choice global-context fallback, a
content-hash confirmation so a touch does not re-enrich, a standalone pure unit test for the closure, a
deterministic re-enriched-count signal, and add/delete unique-name widening. v1 accepts a full
`CSharpCompilation` rebuild per edit (defers `ReplaceSyntaxTree` warm-binder reuse) because
correctness-first.

## 3. Correctness model

### Change closure (which types are re-enriched)

Let `ReparsedFiles` be the set of files the collector reparsed (content-hash mismatch vs the on-disk
manifest; `SyntaxIncrementalCollector.cs:38-48`). The re-enrich set is:

```
SEED        = { typeId | type.FilePath ∈ ReparsedFiles }            (the user's edits; content-hash driven, NOT edge-driven)
            ∪ partial-type closure                                   (existing: SyntaxIncrementalSemanticPhase.cs:96-103)
            ∪ interface-set / global-using fallback seeds            (whole affected assembly — see fallbacks)
REVERSE     = transitive reverse-dependents of SEED over the FULL dep graph
RE_ENRICH   = SEED ∪ REVERSE
```

`REVERSE` is computed by inverting the flat `List<TypeDependency>` (built FULL at
`SyntaxIncrementalSemanticPhase.cs:25` + DI edges line 26 + SerializedReference edges line 29) into
`Map<ToTypeId, List<FromTypeId>>`, then BFS from every `SEED` typeId following reverse edges to a fixed
point, deduping by `typeId` (`TypeIdentity.GetTypeId`, syntax-stable across body edits). The seed MUST
come only from `ReparsedFiles`, never from edge deltas: a body-only edit changes the editing type's
CBO/RFC/LCOM/CycCC (`CboCalculator` walks `DescendantNodes()` including method bodies) but emits zero
graph-edge change, so the dep graph cannot tell which type changed — it only supplies the dependent
expansion. The inversion includes DI + SerializedReference edges so DI/prefab-driven dependents are
covered. Every type not in `RE_ENRICH` reuses its cached, coupling-stripped metrics from
`collect.CachedEnrichmentByTypeId` (`SyntaxIncrementalSemanticPhase.cs:49-50`).

### Conservative fallback triggers (bail to wider re-enrich)

| Trigger | Detection | Scope of forced re-enrich |
| --- | --- | --- |
| First run / cache miss / global fingerprint mismatch | `SyntaxCacheStore.TryLoad` returns null | Full (every file reparsed; existing cold path) |
| Reference set / TFM / resolved level change | NEW: fold `discover.ResolvedReferences.Paths` (ordered, with size or content hash), `discover.SelectedTargetFramework`, and the effective `AnalysisLevel` into `SyntaxCacheFingerprint.ComputeGlobalFingerprint` | Full (fingerprint flip invalidates whole manifest) |
| Preprocessor symbols / profile / thresholds / excludes / targets change | Already in fingerprint (`SyntaxCacheFingerprint.cs:24-40`) | Full |
| Per-assembly interface-set hash change | Existing `ExpandInterfaceInvalidations` (`SyntaxIncrementalCollector.cs:212-241`) | Whole assembly: reparse AND mark all its types as `SEED` |
| Per-assembly global-using-set change | NEW: hash each assembly's `global using` directive set, store in manifest (mirrors interface-set hashing) | Whole assembly: reparse AND mark all its types as `SEED` |
| Any file ADD or DELETE | collector detects a scanned file with no cached entry, or a cached entry with no scanned file | Whole assembly of the added/deleted type (unique-match name resolution `KnownTypeIndex.ResolveUnique` can flip a previously-ambiguous edge in an unchanged file) |
| Non-`.cs` analysis input change (`.csproj`/`.asmdef`/`.dll`/config/triage/baseline) | Flows through fingerprint (config) or reference set (refs) | Full or reference-trigger as above — never the viewer-filtered `ChangedFileIds` set (`ServeChangedFiles.cs:31-36` drops non-`.cs` by design) |

### Why this equals full analysis

The aggregation phase is byte-for-byte untouched and FULL (`AnalysisPipelineAggregation.Run`,
`AnalysisPipeline.cs:102-103`), exactly as the syntax-level incremental path already guarantees.
Correctness rests on three invariants the existing phase already enforces and we preserve:

1. `deps` and `couplingMap` are rebuilt FULL every generation (`SyntaxIncrementalSemanticPhase.cs:25-32`),
   never delta-cached — so rank (PageRank fixpoint), cycles (Tarjan SCC), NOC, and assembly
   internal-relation counts are always computed over the complete, consistent edge set.
2. Cached enrichment is stored coupling-STRIPPED (`SyntaxCacheMetrics.StripCouplingFields`, applied at
   `SyntaxIncrementalSemanticPhase.cs:62` and `SyntaxIncrementalCollector.cs:90`) and re-stamped from the
   fresh `couplingMap` for EVERY type via `ApplyCouplingFields` (`SyntaxIncrementalSemanticPhase.cs:70`);
   `Noc`/`TypeRank` are owned and overwritten for all types inside aggregation. So no global-derived field
   is ever served stale, even for reused types.
3. The phase returns a COMPLETE `typeMetrics` list with one entry per type — fresh for `RE_ENRICH`,
   cached-but-coupling-refreshed for the rest (`SyntaxIncrementalSemanticPhase.cs:65-72`). Aggregation
   cannot distinguish reused from fresh.

The ONLY field class that may be reused without recomputation is the per-type SMELL/cohesion payload
(LCOM/CBO/DIT/RFC/CycCC/WMC/boxing/closure/role), which is local to a type's own body+signature — sound to
mix PROVIDED `RE_ENRICH` captures every type whose cross-tree metric could have changed. The
reverse-dependent closure covers cross-tree signature/base changes; the conservative fallback covers the
declaration-graph blind spots (method-body symbol use into changed types, global usings, references).
Global-aggregation-stays-full is the load-bearing invariant — it is what lets a partially-re-enriched
semantic result feed a correct full aggregation.

## 4. Implementation steps

Each commit is independently revertible and keeps all three TFMs green. Steps 1-3 land while still gated to
syntax level (reverse-dependent over-inclusion only re-enriches MORE, never less, so syntax-level
`Normalize(full)==Normalize(incremental)` is preserved). Step 4 is the gate flip that exposes semantic
cross-tree staleness; the closure from Step 3 is what makes it pass.

Commit bodies: English (public repo per commit.md; recent history confirms English commits).

| # | Commit subject | Files | What it does | Independently testable |
| --- | --- | --- | --- | --- |
| 1 | `🔧 chore: fold reference/TFM/level identity into the incremental cache fingerprint` | M `src/Unilyze/Incremental/SyntaxCacheFingerprint.cs` | Append `discover.ResolvedReferences.Paths` (ordered, each with size or SHA over the DLL), `discover.SelectedTargetFramework`, and the resolved level to `ComputeGlobalFingerprint` (currently ends at targets, `:38-41`). Inert at syntax level; strengthens keying ahead of the widen and guarantees a syntax-built manifest is never reused for a semantic run. | Existing `IncrementalAnalysisTests` stay green; add a unit asserting `ComputeGlobalFingerprint` differs when only the reference set or TFM or level changes. |
| 2 | `♻️ refactor: have the incremental collector return the complete syntax-tree set` | M `src/Unilyze/Pipeline/AnalysisPipelineDiscovery.cs`, M `src/Unilyze/Incremental/SyntaxIncrementalCollector.cs`, M `src/Unilyze/Incremental/SyntaxCacheModels.cs` | Make `Collect` emit a `SyntaxTree` for every scanned file: reparse the cached-but-unchanged files too (or cache+rehydrate trees) so `collect.SyntaxTrees` (`:78`, via `CollectTypesIncremental` `:108`) covers all files, not just `filesToParse`. Until the gate widens, the compilation is still null at syntax level, so behavior is unchanged. THE BLOCKER FIX. | Unit: `Collect` over a multi-file project returns `SyntaxTrees.Count == scannedFiles.Count` and every scanned path is present; existing syntax equivalence tests unchanged. |
| 3 | `✨ feat: add reverse-dependent and global-context closure to incremental re-enrichment` | C `src/Unilyze/Incremental/ReverseDependentClosure.cs`, M `src/Unilyze/Incremental/SyntaxIncrementalSemanticPhase.cs`, M `src/Unilyze/Incremental/SyntaxIncrementalCollector.cs`, M `src/Unilyze/Incremental/SyntaxCacheModels.cs` | New pure `ReverseDependentClosure`: invert `List<TypeDependency>` → reverse-adjacency, BFS transitive closure from seeds. Wire into `DetermineTypesToReEnrich` (`:78`, take `deps`). Add per-assembly global-using-set hashing + add/delete-widening + interface-set whole-assembly SEED marking. Emit a re-enriched-count log line. Still gated to syntax. | Pure `ReverseDependentClosureTests` (hand-built `List<TypeDependency>`, no subprocess). Syntax-level `IncrementalAnalysisTests` still pass (over-inclusion is equivalence-safe). |
| 4 | `✨ feat: enable semantic-level incremental enrichment` (`Closes #216`) | M `src/Unilyze/Pipeline/AnalysisBuildOptions.cs`, M `src/Unilyze/Pipeline/AnalysisPipeline.cs` | Widen the gate: `UseSyntaxIncrementalCache` → `UseIncrementalCache => Incremental` (drop the `== Syntax` pin), update the `BuildCore` dispatch (`:88,:106`) and `CollectTypes` (`AnalysisPipelineDiscovery.cs:78`). Relax the `Build` guard (`:42-47`) so non-Syntax incremental is honored. Now the incremental path runs with a real Compilation. | New `SemanticIncrementalEquivalenceTests` at `--level core`/`full`: `Normalize(full)==Normalize(incremental)` over the full mutation matrix (§5). |
| 5 | `✨ feat: run the serve loop with semantic incremental analysis` (`Closes #216`) | M `src/Unilyze/Serve/SnapshotBuilder.cs`, M `src/Unilyze/Serve/ServeOptions.cs` | `RunAnalysis`: `incremental:false` → `incremental:true` (`SnapshotBuilder.cs:107`); pin `RequestedLevel` to the resolved semantic level so the gate engages; update the class-doc invariant note (`:12-14`) and `ServeOptions` "incremental out of scope" comment. The single `AnalysisCoordinator` worker thread means no locking; manifest I/O is already atomic. `_previousStamps`/`DetectChangedFileIds` viewer-focus path stays untouched. | Serve test asserting the deterministic re-enriched-count signal after a localized edit; existing `ServeAnalysisLoopTests` (coalescing/stale/discard) stay green. One screenshot pass to confirm live updates still render (no UI change). |
| 6 | `📝 docs: record the semantic-incremental serve measurement` | C `tasks/216-semantic-incremental-perf.md` | Document the Track-2 serve-metrics procedure and the measured numbers against #203's corpora (not code; not a CI gate). | n/a (doc). |

## 5. Equivalence test harness

File: `tests/Unilyze.Tests/Incremental/SemanticIncrementalEquivalenceTests.cs` (new), plus the pure
`ReverseDependentClosureTests.cs` from Step 3.

Normalization — reuse the existing `IncrementalAnalysisTests.Normalize` helper verbatim
(`IncrementalAnalysisTests.cs:277-312`); it already strips the only two non-deterministic `AnalysisResult`
fields, `analyzedAt` (`AnalysisPipeline.cs:131`) and `toolVersion` (`AnalysisPipeline.cs:139`), and
resolves each `fileRef` integer back to its `sourceTable` path string. `sourceTable` is already
ordinal-sorted (`BuildSourceTableAndFixRefs`, `AnalysisPipeline.cs:173`), so `fileRef` indices match
full-vs-incremental. If any order-only diff appears from subset-parallel `Enrich`, add a sort-by-`typeId`
normalization to the helper rather than weakening the string equality.

Minimal fixture — the inline `Gamma : Delta` reverse-dep corpus already in `WriteInitialProject`
(`IncrementalAnalysisTests.cs:210-219`): `Gamma.cs` (`class Gamma : Delta`) + `Delta.cs` (`class Delta`).
`Gamma → Delta` is a real reverse-dependency edge. The `.csproj` targets `net8.0` so `.NET` runtime refs
resolve and CBO/DIT/RFC are semantically real at `--level core`/`full`.

Assertion — parameterized `[Theory]` running the CLI subprocess at a semantic level (NOT `--level syntax`)
twice — cold full (no `--incremental`) vs warm incremental (`--incremental` after a warm run) — asserting
`Normalize(full) == Normalize(incremental)`, over:

- existing mutations: edit / add / delete / partial / interface-flip / threshold / define;
- NEW: method-body-only edit of `Delta` (changes Delta's CBO/RFC, no edge) — assert `Gamma` (reverse
  dependent) is re-enriched and full==incremental;
- NEW: base-class change of `Delta` — `Gamma`'s DIT must refresh (`DitCalculator` base-chain walk);
- NEW: add a `global using` — whole-assembly fallback fires and full==incremental;
- NEW: reference/TFM change — fingerprint flips, full re-enrich, full==incremental;
- NEW: touch with no content change — produces all `[incremental] cache hit:`, zero re-parse (proves
  stamps are not the trigger).

Pair every equivalence assert with a deterministic work-elision assert on the `[incremental]` stderr
signals (cache-hit / re-enrich / re-parse counts) — never a wall-clock comparison.

## 6. Performance validation

Two tracks, following the e882abd lesson (the old test asserted warm < cold wall-clock and flaked CI,
blocking v0.4.0):

Track 1 — gating, deterministic, non-flaky. Assert the WORK-ELISION property directly, with no clock.
After a body-only edit of one leaf type with no dependents on an N-type fixture, assert the re-enriched
count == `|SEED ∪ REVERSE|` (== 1 here) and all other files report `[incremental] cache hit:` — via the
re-enriched-count signal emitted in Step 3 and the existing `[incremental]` stderr lines
(`SyntaxIncrementalCollector.cs:44,188,234`). This is exactly the property the latency improvement derives
from. This is the gating signal.

Track 2 — reported, non-gating, run manually. Reuse the #205 server-side per-stage
`ServeAnalysisMetrics.AnalysisMillis` (`SnapshotBuilder.cs:38`, surfaced at `GET /api/state` via
`ServeStateJson`). Drive serve over #203's dominant-cost corpus (large warm edit baseline 5042ms, medium
1503ms), apply one localized edit, collect `AnalysisMillis` over N ≥ 5 post-warmup generations, EXCLUDE
the first cold generation, report the median. Acceptance: median warm-incremental `AnalysisMillis`
materially below the full baseline — target < 70% of full on the large corpus for a single-type edit with
few dependents. Record the numbers in `tasks/216-semantic-incremental-perf.md`.

## 7. Risks & open questions

- Compilation rebuild share — Roslyn compilations are immutable; any `.cs` edit forces a NEW
  `CSharpCompilation` over all trees (`CompilationFactory.cs:61`). If the `compile` sub-phase (not
  `semantic`) is a large share of the 94-97%, eliding enrich yields less than hoped. Mitigation: read the
  existing `PhaseStarted`/`PhaseCompleted` timers (`AnalysisPipeline.cs:77-99`) to split compile vs
  semantic on the #203 corpus before over-investing; `ReplaceSyntaxTree` warm-binder reuse is explicitly
  DEFERRED to a follow-up (Steps 1-5 deliver the enrich-scope win regardless).
- Declaration-only dep graph blind spots — the graph misses method-body symbol use and
  global-using/reference changes. Bounded by: the seed (reparsed file) always re-enriches, and the
  conservative whole-assembly/whole-project fallbacks. Verified by the body-only, global-using, and
  reference mutations in §5.
- Over-inclusion regresses speed — aggressive fallbacks can re-enrich most of a single big assembly,
  erasing the win. Recommendation: collapse a reverse-dependent closure that selects > ~60% of an
  assembly's types to a whole-assembly re-enrich (correctness-equivalent superset, cheaper bookkeeping);
  track the re-enriched count and tune. Mark this threshold as tunable.
- Fingerprint completeness — a transitive reference content change with no path change could leave cached
  metrics stale. Mitigation: fold DLL size or content hash (not just path) into the fingerprint (Step 1);
  the reference-swap mutation tests it.
- Manifest round-trip / size at semantic level — `SyntaxCacheJsonContext` (`SyntaxCacheModels.cs:34-55`)
  already serializes `TypeMetrics`; at semantic levels more fields are populated. Verify round-trip and a
  reasonable manifest size on a large project.
- OPEN (owner confirm) — serve level pinning: serve currently leaves `RequestedLevel` null → `Complete`.
  Step 5 must pin to the RESOLVED level so the gate engages and the fingerprint records it. Recommendation:
  pin to the level `Compile` actually resolved for the session, and fall back to full (discard cache) if a
  mid-session reference loss degrades the level.

## 8. Definition of done

#216 acceptance criteria:

- [ ] (a) Semantic-incremental re-enrichment runs in the serve loop: `SnapshotBuilder.RunAnalysis` passes
  `incremental:true` at a pinned semantic level; the gate honors it.
- [ ] (b) Equivalence: normalized incremental payload == clean full analysis for every mutation in §5, at a
  semantic level, via the extended `Normalize` helper.
- [ ] (c) Change closure correct: re-enrich set = reparsed types + partial closure + transitive
  reverse-dependents, with the conservative fallbacks; proven by `ReverseDependentClosureTests` + the
  deterministic `[incremental]` signal asserts.
- [ ] (d) Global aggregation stays FULL and the 4-tuple contract is preserved.
- [ ] (e) Measured improvement: Track-1 work-elision assertion gating in CI; Track-2 median warm
  `AnalysisMillis` < 70% of full on #203's large corpus, recorded in
  `tasks/216-semantic-incremental-perf.md`.

Project gates:

- [ ] All three TFMs green: `dotnet test -f net8.0`, `-f net9.0`, `-f net10.0` (CI also runs windows net10.0).
- [ ] No compiler/linter warnings introduced.
- [ ] Serve viewer still renders after the flip (one screenshot pass).
- [ ] Path-scrubbing invariant intact (serve still scrubs to opaque `fileId`s; incremental adds no new
  client-facing absolute paths).

Epic #217 closure:

- [ ] With #216 merged and #214/#215 closed NO-GO and #203/#205 closed, close epic #217 referencing the
  #216 PR and the recorded measurement.

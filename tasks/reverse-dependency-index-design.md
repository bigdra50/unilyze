# Design — Reverse Dependency Index (RDI) for precise semantic-incremental invalidation

> Status: DESIGN (no implementation). Baseline: `main` @ fb1cbb8 + PR #222 (`feat/216-semantic-incremental`,
> unmerged, MERGEABLE). Authored 2026-07-02 from a design-only session; adversarially reviewed in a
> 3-round LLM debate (see §9 decision log). Supersedes the retracted `ReverseDependentClosure` plan in
> `tasks/216-implementation-plan.md` §2-3 (that closure ran over the declaration-based dep graph, which
> misses body-symbol use; #222 as shipped replaced it with a binary body-only/full classifier).

## 1. Problem

PR #222 (semantic incremental v1) caches per-type enrichment (LCOM/CBO/DIT/RFC + smells) keyed by
syntax-stable `TypeId` and classifies each warm generation with `StructuralChangeDetector`:

- body-only edit → re-enrich only the edited file's types (+ partial closure). Measured: 1/395 types,
  serve warm edit 85 ms vs 886 ms cold (`tasks/216-semantic-incremental-perf.md`).
- ANY structural change → `RequiresFullReEnrich` → 395/395. Triggers (`SyntaxIncrementalCollector.HasStructuralChange`):
  file add/delete, declaration-shape change in any reparsed file, per-assembly global-using-set change.

The gap: the single most common structural edit while coding — adding/renaming a member, changing a
signature — always pays the full re-enrich. On the #203 Unity corpora (large warm edit ~5 s) every such
keystroke-burst costs seconds of stale live view. v1 chose this deliberately (correctness-first);
the deferred follow-up is recorded in `tasks/216-semantic-incremental-perf.md`.

The fix this document designs: record, per enriched type T, which in-source types T's enrichment
actually resolved (`UsedTypes(T)`); invert it (`RDeps(B) = {T | B ∈ UsedTypes(T)}`); on a structural
delta to B, re-enrich `SEED ∪ RDeps(closure of B)` instead of everything.

## 2. Pre-existing correctness hole in #222 (fix before/with merge — independent of RDI)

`TypeStructureSignature` (`StructuralChangeDetector.cs:21-33`) covers base/interfaces/members/attributes/
constraints but NOT the file's `using` directives, and `HasStructuralChange` only hashes GLOBAL usings.
So a per-file using change — most sharply an alias retarget — is classified body-only:

```csharp
// F.cs (edited)                         // G.cs (unchanged, cached)
using A = Foo.Bar;   →  using A = Baz.Qux;
class X : A { }                          class G : X { }   // cached DIT walked X's OLD base chain
```

F reparsed, signature strings identical → body-only path → only X re-enriched. G's cached DIT (and any
inherited-member-sensitive metric) is stale; a full run disagrees. Equivalence broken. Same applies to
plain `using Foo;` → `using Bar;` retargeting an ambiguous base name.

Recommended #222 hotfix (small, conservative): fold the file's normalized using-directive set (regular,
static, alias — reuse `NormalizeUsingDirective`) into `BuildFileSignature`, so any using change in F ⇒
structural ⇒ full re-enrich. Add the alias-retarget case to the equivalence-test mutation matrix. RDI
(§4) later refines this from "full" to "F's types ∪ RDeps(F's types)".

## 3. Goals and non-goals

Goals:

- (G1) Precise invalidation for structural edits to EXISTING types (signature modify; member add/remove;
  base-list change), sound w.r.t. full-analysis equivalence.
- (G2) The index persists in the existing on-disk manifest so both `serve` and future semantic-level
  `analyze --incremental` (CI badges/diff warm cache) benefit.
- (G3) Deterministic work-elision signals (`[incremental] re-enrich types: n/m (reason)`) — no
  wall-clock CI gates (e882abd lesson).

Non-goals (explicit, with reasons):

- Incremental global aggregation (rank/cycles/NOC): measured 0.0 s of a 4 s run — no payoff; the
  "aggregation stays FULL" invariant is load-bearing for correctness and stays. Revisit only if a corpus
  shows aggregate >5% of warm time.
- Type add/delete/rename precision: a never-before-referenced symbol can capture name/overload/extension
  resolution anywhere; RDI records what WAS referenced, not what WOULD now be. These stay full-fallback
  in v2.0. (A per-type syntactic identifier-name intersection index is sketched in §6 Phase C as an
  optional refinement — may never be needed.)
- `ReplaceSyntaxTree`/warm-binder compilation reuse and cross-generation tree/MetadataReference warmth:
  separate follow-up (also listed in the perf doc); orthogonal to invalidation correctness.
- Source generators / `dynamic`-heavy code: no generators run in this compilation; `dynamic` binds at
  runtime and contributes no compile-time resolution the enricher consumes beyond expression types,
  which are recorded.

## 4. Design

### 4.1 Recording: one IOperation walk per re-enriched type

Do NOT instrument individual calculators (CBO's TypeSyntax walk, RFC's invocation walk, detectors).
Syntax-kind-specific hooks can never be exhaustive — the LLM debate (§9) produced concrete misses:
user-defined operators, `foreach`/`await`/deconstruction pattern members, collection-initializer `Add`,
indexers, and argument types under implicit-conversion capture. Instead, when (and only when) a type T
is being re-enriched, run one dedicated usage-collection pass over T's member bodies (reuse
`MemberBodyEnumerator` for the member set) using `SemanticModel.GetOperation`:

For every `IOperation` node in each body, record into `UsedTypes(T)`:

- `op.Type` and `ConvertedType` where present; for conversions, the user-defined conversion operator's
  containing type;
- the containing type (and `OriginalDefinition`'s containing type) of every bound symbol an operation
  exposes: `IInvocationOperation.TargetMethod`, `IMemberReferenceOperation.Member` (covers properties,
  events, fields, indexers), `IObjectCreationOperation.Constructor`, `IUnaryOperation`/`IBinaryOperation.
  OperatorMethod`, `IForEachLoopOperation`'s enumerator-pattern members, `IAwaitOperation`'s awaiter
  pattern members, deconstruction `Deconstruct` targets, collection-initializer `Add` invocations;

Declaration-side surfaces are recorded from declared symbols directly (no IOperation): base list,
interface list, member signature types, attribute types, constraint types, and the full base-chain walk
that `DitCalculator` performs (every chain type ∈ `UsedTypes(T)`).

File-scope environment: for each file F, record the TARGETS of F's using directives (`using static B` →
B; `using A = Foo.Bar` → Foo.Bar; plain `using N` → namespace names are NOT types — covered instead by
the add/delete full fallback) into `UsedTypes` of every type declared in F. This covers unqualified
capture through `using static`.

Symbol → key mapping: `INamedTypeSymbol.DeclaringSyntaxReferences` → declaring file → the parse-time
`TypeId` (`TypeIdentity`). Symbols with no in-source declaration are metadata references — ignored:
they cannot change mid-session, and reference-set/TFM changes already flip the global fingerprint
(#222 Step 1) → full rebuild. Partial types: all declaring references map to the same `TypeId`.

Cost: one `GetOperation` realization per re-enriched member body, only for types being re-enriched
anyway (which already pay per-node `GetTypeInfo`/`GetSymbolInfo` walks in CBO/RFC/boxing/closure
detectors). Set-insert per node; `UsedTypes` dedups to tens of TypeIds per type.

### 4.2 Index and persistence

- `UsedTypes(T)` stored per type in the existing manifest (`SyntaxCacheModels`), next to the cached
  enrichment payload — a `string[]` of TypeIds, sorted ordinal. Bump `SyntaxCacheFingerprint.SchemaVersion`
  (old manifests then load as null → cold path — safe).
- `RDeps` (the inversion) built in-memory per generation from the manifest + fresh recordings; not
  persisted (cheap: one pass over all `UsedTypes`).
- Estimated size: ~tens of TypeIds × N types (self-corpus 395 types → low tens of KB). Verify manifest
  round-trip size on a #203 corpus (same check #222 already carries).

### 4.3 Structural delta classification (replaces the binary classifier)

`StructuralChangeDetector` already builds canonical per-type signature strings; extend it to emit a
per-TypeId delta instead of a per-file boolean, by diffing cached raw types vs reparsed raw types:

| Delta class | Definition | Invalidation (∪ SEED, always) |
| --- | --- | --- |
| body-only | file reparsed, no per-type deltas, usings unchanged | — (v1 behavior kept) |
| Δusing(F) | file F's using-directive set changed | F's types ∪ RDeps(F's types) |
| Δsig(B) | existing B, signature changed, member SET unchanged (member modify / type modifiers / attributes / constraints) | RDeps(B) |
| Δmembers(B) | member added to / removed from B | RDeps(B ∪ InhDesc(B)); if the member is an extension method with `this P` → also RDeps(P ∪ InhDesc(P)) |
| Δbase(B) | base-list / interface-list change on B | InhDesc(B) ∪ RDeps(B ∪ InhDesc(B)) |
| Δadd / Δdel / rename (type or file) | TypeId appears/disappears | FULL re-enrich (v2.0) |
| global usings / preprocessor / references / TFM / config | fingerprint flip (unchanged from #222) | FULL (cold path) |

`InhDesc(B)` = transitive inheritance/interface-implementation descendants of B, from the declaration
graph (already built full every generation). Rationale for the Desc closure: a receiver statically
typed `D : B` that bound to an ancestor member may re-bind to B's new member (hiding/capture); callers
with receivers of type B or D carry those types in their operation trees, so they sit in RDeps of the
closure.

Collapse threshold: if the computed invalidation set exceeds ~60% of all types, fall back to full
(correctness-equivalent superset, cheaper bookkeeping; threshold tunable, log the collapse reason).

### 4.4 What stays full every generation (unchanged invariants)

Parse of changed files + tree completion, `CSharpCompilation` rebuild, `BaseTypeResolver`,
`DependencyBuilder` + DI + SerializedReference edges, `CouplingMetricsCalculator`, `TypeRoleStamper`,
aggregation, fingerprints, coupling re-stamp of cached metrics (`ApplyCouplingFields`). RDI narrows
ONLY the `SemanticEnricher.Enrich` subset — the same single lever v1 pulls, so the #222 "aggregation
consumes a complete, consistent 4-tuple" contract is untouched.

## 5. Soundness model

Invariant: an unchanged type T's cached enrichment may be reused iff no change in this generation can
alter any value `Enrich(T)` computes. `Enrich(T)` is a function of (a) T's own syntax, (b) the resolved
surfaces of symbols T's computation binds to, (c) T's resolution environment (usings, visible symbol
set). Coverage argument per change vector:

| Change vector | Covered by |
| --- | --- |
| T's own file edited | SEED (reparsed files always re-enrich) |
| surface of a bound type B modified | RDeps(B) — T recorded B (operation type, bound-member container, base chain, or declaration surface) |
| member add/remove on B captures/releases a binding in T | receiver/argument/operand types are recorded as operation types → T ∈ RDeps(B ∪ InhDesc(B)); `using static` capture → using-target recording |
| base-list change on B shifts DIT / inherited binding of descendants | InhDesc closure + RDeps of it |
| per-file using change in F shifts resolution inside F | F's types are SEED; cross-file effect flows through F's types' changed surfaces → RDeps(F's types) (Δusing rule) |
| new/deleted/renamed type visible anywhere | FULL fallback |
| global usings / references / TFM / preprocessor / config | fingerprint → FULL |

Known residual risks (why the fallbacks and the harness in §7 exist):

- The Δmembers coverage claim ("any capturable binding surfaces B or InhDesc(B) in T's operation tree
  or using targets") was adversarially challenged in the LLM debate; round 3 sought a counterexample
  under IOperation-based recording — see §9 for the outcome and any residual carve-outs.
- Cross-type CONST value / default-argument value changes: `MemberSignature` does not include those
  initializer values, so they classify body-only today. VERIFIED 2026-07-02 (Phase A review): zero
  uses of `ConstantValue`/`GetConstantValue`/`HasConstantValue` anywhere in `src/Unilyze` — no
  metric or smell detector reads a foreign constant's VALUE, so the carve-out is sound as shipped.
  Re-run that grep if a future detector starts reading constant values. Enum MEMBER values turned
  out to already be part of the member signature (the raw member's type field carries the
  initializer text), so enum value changes classify as Δsig and invalidate RDeps(B) — safer than
  this section originally assumed.
- Compilation-error transitions: an edit that makes an UNCHANGED file stop compiling can change what a
  full run would produce for that file's types. The delta classes that can cause this are structural
  (signature/member/base/using) — covered by their invalidation rules; body-only edits cannot break
  other files. Document as an explicit test case (mutation harness: introduce a signature change that
  breaks a caller; assert caller re-enriched).

## 6. Phasing

Each phase is independently shippable and equivalence-gated. Phase order is risk-ascending.

- Phase 0 — correctness pre-work (target: into #222 or immediately after merge)
  - Fix the using-directive hole (§2) with the conservative full-fallback + alias-retarget test.
  - Land the mutation-differential harness SKELETON (§7.2) with the existing mutation matrix ported.
  - Exit: #222 mergeable with the hole closed; harness runs in CI on the small fixture.
- Phase A — RDI for Δsig + Δusing (lowest hazard surface)
  - IOperation UsageRecorder + manifest `UsedTypes` + schema bump + RDeps inversion + per-type delta
    classifier. Δsig(B) → RDeps(B); Δusing(F) → RDeps(F's types); EVERYTHING else (member add/remove,
    base change, add/delete) still falls back FULL. CORRECTION (2026-07-02, found during Phase A
    implementation review): the original "strict superset of v1" justification here was wrong —
    for Δsig, SEED ∪ RDeps(B) is a SUBSET of v1's full re-enrich, so Phase A's safety rests on
    recording soundness for the narrowest hazard class (a caller that bound any member of B
    necessarily surfaced B in its operation tree or declaration surfaces) plus the equivalence
    matrix and the mutation harness, which lands in Phase 0 and gates Phase A too.
  - Exit: equivalence matrix + Track-1 elision assertions green; self-corpus signature-modify warm
    edit re-enriches |RDeps| ≪ 395.
- Phase B — Δmembers + Δbase precision (the main prize; gated on the mutation harness)
  - InhDesc closure, extension this-param rule, using-static target recording.
  - Gate: mutation harness extended with the debate's hazard list (operator add, implicit-conversion
    capture, foreach/await/deconstruct pattern member add, collection-initializer Add capture, indexer
    add, interface default member add, hiding via new derived member) — all green full==incremental.
  - 📝 STARTED 2026-07-03: InhDesc(B) closure built from the per-generation declaration graph +
    RDeps(B ∪ InhDesc(B)) resolution landed for Δmembers/Δbase. Extension-method `this`-param
    capture (RDeps(P ∪ InhDesc(P))) is NOT implemented — raw ParameterInfo carries no `this`
    modifier, so a static class's member-set change stays a conservative full fallback instead
    (documented deviation in `StructuralChangeDetector.ClassifyFileTypeDelta`).
- Phase C — optional refinements (decide later, data-driven)
  - Name-intersection index to soften the Δadd/Δdel full fallback.
  - `BaseTypeResolver`/`TypeRoleStamper` per-type reuse for unchanged types; share the per-generation
    `SemanticModel` cache across the 4 phases that each build their own today
    (`BaseTypeResolver.cs:21`, `SemanticEnricher.cs:53`, `TypeRoleStamper.cs:21`, `DIContainerAnalyzer.cs:27`);
    per-file DI-registration cache. These attack the remaining ~2.5 s warm semantic floor the perf doc
    documents and are pure perf (no invalidation semantics).

## 7. Test strategy

### 7.1 Equivalence matrix (extends `SemanticIncrementalEquivalenceTests`)

Keep `Normalize(full) == Normalize(incremental)` as the oracle. New mutations, each paired with a
deterministic elision assert on the `[incremental] re-enrich types: n/m (reason: …)` signal:

alias retarget (§2, Phase 0); plain-using retarget of an ambiguous base; signature modify with 2+
dependents (assert exactly SEED∪RDeps re-enriched); member add that hides a base member used by an
unchanged caller; extension-method add capturing a previous extension binding; implicit-operator add
shifting a caller's overload choice (the debate's argument-type case); base-list change asserting a
GRANDchild's DIT refresh; interface default member add; type add / file delete (assert FULL reason);
const value change (assert body-only AND equivalence — guards the §5 const carve-out); touch with no
content change (all cache hits).

### 7.2 Mutation-differential harness (new; REQUIRED, gates Phase B)

The debate established that static exhaustiveness audits (asserting the recorder saw every
`GetSymbolInfo` result) are methodologically insufficient — they cannot see "types that influenced a
resolution without being the resolution result". So: a generative harness that applies scripted
semantic-shifting mutations (add member / add operator / add extension / change base / retarget alias /
add conversion, drawn from a small grammar) to a fixture corpus, runs full vs warm-incremental after
each, and asserts normalized equality. Deterministic seed list in CI (~500 LOC, minutes not hours);
larger randomized sweeps runnable locally/nightly. Any divergence is a P0 on the invalidation rules.

### 7.3 Shadow verification in serve (dogfood safety net)

`serve --verify-incremental` (or env var): every Nth generation, run a full analysis alongside and diff
normalized snapshots; log `[incremental] DIVERGENCE` with the differing TypeIds. Off by default; on for
unilyze self-serve during Phase A/B development. Catches unknown-unknowns in real editing sessions
where the mutation grammar is blind.

## 8. Performance validation

Track 1 (gating, deterministic): elision-count asserts per §7.1 — e.g. signature-modify of a type with
k dependents re-enriches exactly 1+k of N.

Track 2 (reported, non-gating, manual — perf-doc procedure): median warm `AnalysisMillis` over ≥5
generations on the #203 large corpus for (a) member-add edit, (b) signature-modify edit. Acceptance
target: structural-edit warm cost drops from ~full (5 s) to the same order as body-only (100–300 ms)
for low-fan-in types; record in `tasks/216-semantic-incremental-perf.md`. The win scales with corpus
size — on the 395-type self-corpus the absolute saving is ~1 s; the design is justified by Unity-scale
corpora and CI cache reuse (G2), not by self-analysis.

## 9. Decision log (LLM debate, 3 rounds, 2026-07-02)

Debated with GitHub Copilot CLI (auto model; GPT-5.5 unavailable on the account's plan — noted for
provenance). Positions that shaped this document:

- AGREED both sides: type-level granularity for `UsedTypes` (member-level index deferred indefinitely);
  aggregation stays full permanently (measured 0.0 s); on-disk manifest persistence with schema bump.
- Reviewer found, designer accepted: syntax-site instrumentation is unfixably non-exhaustive (operators,
  enumerator/awaiter/deconstruct patterns, initializer `Add`, indexers, argument types under conversion
  capture) → recording redesigned around IOperation (§4.1). Static exhaustiveness audits insufficient →
  mutation-differential harness made a REQUIRED gate for Phase B (§7.2).
- Reviewer proposed deferring RDI entirely ("async full recompute + stale indicator; revisit at 2K–5K
  types / 30–60 s warm"). Designer rebuttal accepted into the record: serve is ALREADY async (worker +
  stale-snapshot publishing) so that alternative is the status quo; `analyze --incremental` exists as a
  product surface and CI badge/diff warm cache is an explicit direction (G2); primary corpora are
  Unity-scale. Resolution: proceed, but with the risk-ascending phasing of §6 (Phase A is
  low-hazard-only precision; Phase B is gated on the harness) instead of a single big-bang PR.
- Round 3 (counterexample challenge against IOperation-based Δmembers coverage): the reviewer could
  not construct a counterexample after working through operators, generic inference via helper types,
  extension methods, interface default members, explicit interface implementation, nullable
  annotations, `dynamic` (statically unresolved → metric-invisible), ref structs, local functions,
  tuple deconstruction, method groups, `nameof`, constraints, and iterators — each reduces to "B or
  InhDesc(B) surfaces in T's operation types, bound-member containers, using targets, or declaration
  surfaces". The reviewer conceded soundness for the in-scope delta classes, RETRACTED the deferral
  recommendation ("ship RDI in v2.0"), assessed the IOperation walk as no more expensive than the
  existing detector walks (likely cheaper: property reads over an already-built tree vs per-node
  SemanticModel queries), and accepted the gating split: Phase A ships without the mutation harness
  (strict-superset argument), Phase B is gated on it. Debate settled; this design is final.
  [Post-debate correction 2026-07-02: the "strict superset" premise both sides accepted was wrong —
  Δsig refinement shrinks the re-enrich set below v1's full fallback. Resolved operationally by
  landing the mutation harness in Phase 0 (done, commit 92654e0), so it gates Phase A as well.]

## 10. Open questions for the owner

1. Scope check: is CI warm-cache (`analyze --incremental` at semantic level for badges/diff) a real
   product goal? It is the second leg of the justification (G2); if it is not wanted, Phase B's
   value case weakens and stopping after Phase 0+A is reasonable.
   RESOLVED 2026-07-03: CI warm-cache is confirmed as a product goal; Phase B started.
2. RESOLVED 2026-07-02: §5 const/default/enum-value carve-out verified during Phase A review — no
   detector reads foreign constant values (see §5).
3. Collapse threshold default (60%) and whether the reason string should surface in the serve UI
   (viewer currently shows only timing).
4. Should Phase 0's using-hole fix go into #222 itself (recommended: it is a v1 equivalence bug) or as
   an immediate follow-up PR?

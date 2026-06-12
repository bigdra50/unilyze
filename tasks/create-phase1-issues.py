#!/usr/bin/env python3
"""Create Phase 1 issues for the 2026-06 improvement roadmap.

Issues are created in work order. Dependencies reference earlier issues by
actual number (substituted at creation time via {P1_NN} placeholders).
Outputs tasks/phase1-issues.json mapping order code -> issue number.
"""
import json
import subprocess
import tempfile
import os
import re
import sys

REPO = "bigdra50/unilyze"
MILESTONE_P1 = "Phase 1 - Measurement reliability & quick wins"

ISSUES = [
    # ---------------------------------------------------------------- P1-01
    dict(
        code="P1-01",
        title="[Bug] Unify the exit-code contract: unknown subcommands/options must be usage errors (config/skills exit 0 today)",
        labels="bug,quick-win",
        body="""Order: P1-01 | Effort: S-M | Impact: high | Verdict: valid (code-verified)

## Summary

The documented exit-code contract (`0` success / `1` usage error / `2` gate failure) is only honored by `badge` and `diff`. Unknown subcommands and unknown options elsewhere are silently accepted; `config` and `skills` return exit `0` for unknown subcommands. In CI this produces false greens.

## Evidence

- `unilyze config nonexistent` and `unilyze skills nonexistent` exit `0` (verified against current `Program.cs` dispatch).
- README documents exit codes only for `badge`/`diff`; other subcommands have no defined contract.

## Plan

1. Audit argument parsing in `Program.cs` / `ProgramHelpers.cs` for every subcommand (`config`, `skills`, `diff`, `hotspot`, `trend`, `statusline`, `badge`, `metrics`, `schema`).
2. Unknown subcommand or unknown option: print a one-line usage error to stderr and exit `1`.
3. Add a did-you-mean suggestion (edit distance <= 2) for near-miss subcommands/options.
4. Document the contract once (`--help` and README): `0` success / `1` usage / `2` gate.
5. Add `CliE2eTests` cases for each subcommand: unknown subcommand, unknown option, and happy path exit codes.

## Acceptance criteria

- [ ] `unilyze config nonexistent` exits `1` with a stderr message naming the unknown token.
- [ ] `unilyze --no-such-flag` and `unilyze diff --no-such-flag a.json b.json` exit `1`.
- [ ] Near-miss input (e.g. `unilyze statuslin`) suggests the correct name.
- [ ] E2E tests cover the contract for all subcommands.

## Verification

```bash
dotnet test
unilyze config nonexistent; echo $?   # expect 1
```

## Notes

This changes behavior for scripts that accidentally relied on silent acceptance. Call it out in CHANGELOG (see P1-05) under a minor bump.
""",
    ),
    # ---------------------------------------------------------------- P1-02
    dict(
        code="P1-02",
        title="[Bug] statusline --help output-format description is out of sync with the actual format",
        labels="bug,documentation,quick-win",
        body="""Order: P1-02 | Effort: S | Impact: medium | Verdict: valid (code-verified)

## Summary

`unilyze statusline --help` describes an output format that no longer matches what `StatuslineFormatter` emits (`CH:avg/min MI:n Nsmells [crit] [boxing] [cycles] [level]`). Agents and users reading `--help` get a wrong spec.

## Plan

1. Compare the `--help` text against `StatuslineFormatter` output token by token.
2. Rewrite the help text to describe every token, including conditional ones (critical, boxing, cycles, level marker) and the cache behavior.
3. Add a test asserting the help text mentions each token emitted by the formatter (guards future drift).

## Acceptance criteria

- [ ] `--help` format section matches actual output for: CH, MI, smells, critical, boxing, cycles, level marker.
- [ ] Drift-guard test added.

## Verification

```bash
dotnet test
unilyze statusline --help
```
""",
    ),
    # ---------------------------------------------------------------- P1-03
    dict(
        code="P1-03",
        title="[Perf] Assembly metric aggregation is O(assemblies x dependencies x types); build reverse indexes",
        labels="enhancement,quick-win",
        body="""Order: P1-03 | Effort: S | Impact: high | Verdict: valid (code-verified)

## Summary

Assembly-level aggregation (Ca/Ce/Abstractness/Relational Cohesion) re-scans all type dependencies for each assembly, giving O(A x D x T) behavior. On multi-asmdef projects this dominates the 12-14s analysis time observed in the benchmark.

## Evidence

- Triple nested scan in the assembly aggregation path (`AssemblyMetrics` / `CouplingMetricsCalculator`).
- `artifacts/unilyze-bench-results.md`: multi-asmdef projects are the slow tail.

## Plan

1. Build `typeId -> assembly` and `assembly -> types` dictionaries once after type analysis.
2. Single pass over dependencies to accumulate per-assembly Ca/Ce and internal-relation counts.
3. Assert output JSON is byte-identical (after key ordering) on a fixture before/after — this is a pure performance change; metric values must not move.
4. Record before/after timing on 2-3 benchmark projects in the PR description.

## Acceptance criteria

- [ ] Identical analysis JSON on fixtures (excluding timestamps).
- [ ] Measured speedup reported on a multi-asmdef project.
- [ ] All existing tests pass.

## Verification

```bash
dotnet test
# timing comparison on a sample Unity project
time unilyze -p <project> -f json -o /tmp/out.json
```
""",
    ),
    # ---------------------------------------------------------------- P1-04
    dict(
        code="P1-04",
        title="[Test] Strengthen CogCC cross-validation: line-based diagnostic matching and realistic pass thresholds",
        labels="enhancement,quick-win",
        body="""Order: P1-04 | Effort: S | Impact: medium | Verdict: valid (code-verified)

## Summary

`CogCCCrossValidationTests` matches SonarAnalyzer diagnostics to methods by name suffix (`StartsWith`/`EndsWith`), which mis-pairs overloads (`SonarCogCCHelper.cs:152` overwrites same-name keys), and passes at `exact >= 50%` — far below the measured 96.5-100% agreement. A real regression in CogCC counting would not fail CI today.

## Evidence

- `tests/Unilyze.Tests/CrossValidation/CogCCCrossValidationTests.cs:78-92` (suffix matching), `:169-172` (exact >= 0.5 / within1 >= 0.8).
- `SonarCogCCHelper.cs:125-146` already resolves diagnostic line/column to syntax nodes, so line-based pairing is cheap.

## Plan

1. Replace name-suffix pairing with line-based pairing using the already-available diagnostic locations.
2. Measure the current exact/within1 agreement on this test corpus (unilyze's own source) and set thresholds just below measured values (e.g. measured 0.98 -> threshold 0.95).
3. Keep a `within1` secondary threshold.

## Acceptance criteria

- [ ] No name-based pairing remains; overloads pair correctly.
- [ ] Thresholds derived from this corpus's measured agreement (documented in the test).

## Verification

```bash
dotnet test --filter CogCCCrossValidation
```

## Notes

Do not reuse the 96.5% figure from external projects (tasks/cross-validation-report.md) as the threshold — measure on this corpus (reviewer note).
""",
    ),
    # ---------------------------------------------------------------- P1-05
    dict(
        code="P1-05",
        title="[Docs] Introduce CHANGELOG.md and wire it into the metric-compatibility policy",
        labels="documentation,quick-win",
        body="""Order: P1-05 | Effort: S | Impact: medium | Verdict: valid

## Summary

The repo has 13 tags but only 3 GitHub Releases, and `docs/metrics.md`'s compatibility policy requires metric-definition changes to be documented in release notes — but there is no canonical place to record them. Introduce `CHANGELOG.md` (Keep a Changelog format) as that place.

## Plan

1. Add `CHANGELOG.md` with sections for Unreleased and recent versions (backfill 0.3.0 and 0.2.x highlights from `git log` + release notes; older versions get a single summary line).
2. Add a "metric-definition changes" convention: any entry that changes measured values is tagged `[metrics]` and requires a minor bump (link `docs/metrics.md` policy).
3. Add a release checklist step to `README.dev.md`: update CHANGELOG before tagging.

## Acceptance criteria

- [ ] `CHANGELOG.md` exists with backfilled recent versions.
- [ ] README.dev.md release flow references it.
- [ ] Convention for `[metrics]`-tagged entries documented.
""",
    ),
    # ---------------------------------------------------------------- P1-06
    dict(
        code="P1-06",
        title="[CI] Dogfood the quality gate: run unilyze badge --fail-under against this repo in ci.yml",
        labels="enhancement,quick-win",
        body="""Order: P1-06 | Effort: S | Impact: medium | Verdict: valid (code-verified)

## Summary

`ci.yml` runs unit tests only. The README sells `badge --fail-under` as a CI gate, but this repo does not use it on itself. Add a self-analysis gate job.

## Evidence

- `.github/workflows/ci.yml:34-35` (tests only).
- `badges.yml` already runs self-analysis to publish the badge, so the pattern exists.

## Plan

1. Add a CI job: build, run `unilyze badge -p . --metric codehealth --fail-under <threshold>` at SyntaxOnly.
2. Measure the current min CodeHealth of this repo first and set the threshold at the current value floor (do not aspirationally overshoot; the gate should pass on day one and catch regressions).
3. Document the gate in README.dev.md.

## Acceptance criteria

- [ ] CI fails if min CodeHealth regresses below the recorded floor.
- [ ] Gate passes on current main.

## Verification

```bash
dotnet run --project src/Unilyze -- badge -p . --metric codehealth --fail-under <threshold>; echo $?
```
""",
    ),
    # ---------------------------------------------------------------- P1-07
    dict(
        code="P1-07",
        title="[Feature] Add metricsVersion/toolVersion to JSON output and warn on cross-version diff/trend",
        labels="enhancement,metrics-compat",
        body="""Order: P1-07 | Effort: M | Impact: medium | Verdict: valid (code-verified)

## Summary

Implement the `metricsVersion` field deferred from #20. Comparing snapshots produced by different unilyze versions silently mixes tool-induced metric changes into diff/trend results; the compatibility policy (`docs/metrics.md:391-395`) names this as the open gap. This issue is the gate for all later metric-changing work in Phase 1.

## Evidence

- `docs/metrics.md:386-395`: users must self-defend against cross-version comparisons today.
- Precedent: `diff` already warns on `analysisLevel` mismatch (`Program.cs` analysisLevel warning; tested in `CliE2eTests.cs:269-284`) — same pattern applies.

## Plan

1. Add `metricsVersion` (int, start at `1`) and `toolVersion` (assembly version) to the JSON root; expose both in `unilyze schema`.
2. `diff`/`trend`: when inputs disagree on `metricsVersion`, print a one-line stderr warning; add `--fail-on-version-mismatch` opt-in flag (exit `2`).
3. Document the bump rule: any change that alters measured values increments `metricsVersion` and requires a minor version bump + CHANGELOG `[metrics]` entry (links P1-05).
4. Release checklist: verify `metricsVersion` bump when metric-calculation files changed.

## Acceptance criteria

- [ ] JSON contains `metricsVersion` and `toolVersion`; `schema` documents them.
- [ ] diff/trend warn on mismatch; opt-in flag fails with exit `2`.
- [ ] docs/metrics.md updated (policy now mechanically enforceable).

## Verification

```bash
dotnet test --filter CliE2e
```

## Dependencies

Blocks {P1_08}, {P1_09}, {P1_12} (all metric-changing changes must land after this).
""",
    ),
    # ---------------------------------------------------------------- P1-08
    dict(
        code="P1-08",
        title="[Feature] Exclude Library/Temp/obj/bin/.git by default and stop double-parsing nested asmdef directories",
        labels="enhancement,metrics-compat",
        body="""Order: P1-08 | Effort: S-M | Impact: high | Verdict: needs-revision (corrections below)

## Summary

`.cs` enumeration has no default exclusions (`TypeInfo.cs:90` uses `Directory.EnumerateFiles(.., "*.cs", AllDirectories)`); `CsprojParser.cs:52-54` excludes only `Library/`. Built repositories pull `obj/` generated sources into analysis, and the benchmark's only 120s timeout came from scanning build output. Nested asmdef directories are parsed twice (parent + own assembly), double-counting types.

## Reviewer corrections (from plan verification)

- The headline benefit is the timeout fix and WPF-style `*.g.cs` / `EmitCompilerGeneratedFiles` outputs; plain `obj/` artifacts (`AssemblyInfo.cs`, `GlobalUsings.g.cs`) contain no type declarations and do not skew type metrics — do not oversell impact in docs.
- This changes measured values for affected projects => `metricsVersion` bump required (see dependency).

## Plan

1. Central default-exclusion list: `Library/`, `Temp/`, `obj/`, `bin/`, `.git/`, `Logs/`, `UserSettings/` — applied to both `.cs` enumeration and asmdef discovery.
2. Config escape hatch (e.g. `"disableDefaultExcludes": true` in `.unilyze.json`).
3. Skip files whose first lines contain `<auto-generated>` or with `.g.cs`/`.generated.cs` suffixes (config opt-out too).
4. Fix nested-asmdef ownership so each source file belongs to exactly one assembly (nearest asmdef wins).
5. Bump `metricsVersion`; CHANGELOG `[metrics]` entry.
6. Tests: fixture with `obj/` + nested asmdef proving exclusion and single-parse.

## Acceptance criteria

- [ ] Benchmark timeout case completes.
- [ ] Nested asmdef types counted once.
- [ ] Opt-outs work; `metricsVersion` bumped; CHANGELOG entry present.

## Dependencies

Requires {P1_07} (metricsVersion) to land first.
""",
    ),
    # ---------------------------------------------------------------- P1-09
    dict(
        code="P1-09",
        title="[Bug] Smell thresholds drift between code and docs; single-source the threshold registry",
        labels="bug,metrics-compat",
        body="""Order: P1-09 | Effort: S-M | Impact: medium | Verdict: needs-revision (scope note below)

## Summary

Thresholds documented in `docs/metrics.md` / README and the constants in `CodeSmellDetector` disagree in several places (audit found CBO 14 vs 15, MI 20 vs 60 boundary confusion, DIT 6 vs 5, plus missing Critical-side documentation). There is no single source, so drift will recur.

## Plan

1. Audit every threshold: detector constants vs `docs/metrics.md:278-286` vs README table vs `unilyze metrics` output. Produce the discrepancy table in the PR.
2. Create a single `SmellThresholds` registry (one static class) consumed by detectors, `unilyze metrics`, and SARIF rule metadata.
3. For each discrepancy decide the canonical value (default: the documented value is the contract; changing code values is a `[metrics]` change).
4. Add a test that renders the docs threshold table from the registry and compares against `docs/metrics.md` content (drift guard).
5. If any code-side values change: bump `metricsVersion`, CHANGELOG `[metrics]` entry.

## Acceptance criteria

- [ ] One registry; detectors/metrics/SARIF read from it.
- [ ] Drift-guard test fails if docs and registry diverge.
- [ ] Discrepancies resolved and documented.

## Dependencies

Requires {P1_07} if code-side values change.

## Notes

Threshold *calibration* (Alves 2010 percentile derivation, role-based thresholds) is Phase 2 — out of scope here. This issue only removes drift.
""",
    ),
    # ---------------------------------------------------------------- P1-10
    dict(
        code="P1-10",
        title="[Test] Golden-corpus tests that mechanically enforce the metric-compatibility policy",
        labels="enhancement,metrics-compat",
        body="""Order: P1-10 | Effort: M | Impact: high | Verdict: needs-revision (corrections below)

## Summary

`docs/metrics.md:353-374` records that measurement-changing bugfixes shipped in patch releases more than once, and defines a policy forbidding it — but enforcement is manual review. Add golden tests: a fixture project with known smells whose full metric JSON is pinned; any unintended value change fails CI.

## Reviewer corrections (from plan verification)

- In CI the fixture resolves to SyntaxOnly, so boxing/CBO/DIT pin near zero. Use the Reference-HintPath level-elevation pattern from `CliE2eTests.cs:483-498` to pin semantic metrics too.
- Normalize `projectPath` (absolute) and `analyzedAt` (timestamp) before comparison or strict equality will never hold.
- The originally separate "SyntaxOnly band-monitoring of a public Unity project" proposal folds into this issue as an optional second golden (pinned-commit shallow clone, per-PR — analysis takes ~0.6s). Start with the local fixture; the OSS golden can be a follow-up commit in the same issue.

## Plan

1. `tests/fixtures/golden/` pseudo-Unity project with intentional findings: each smell kind, boxing, closure, params, cycles, DI registrations.
2. Run full analysis (`-f json`), normalize volatile fields, compare against `expected.json` with exact equality.
3. Expected-value updates are manual: regenerate via a documented command, review the diff in PR (no auto-regen in CI).
4. Document the workflow in README.dev.md, linking the compatibility policy.

## Acceptance criteria

- [ ] CI fails when any pinned metric value moves.
- [ ] Semantic metrics (boxing/CBO/DIT) pinned at an elevated analysis level.
- [ ] Expected-update flow documented.

## Dependencies

Land after {P1_08} and {P1_09} so the pinned values do not immediately churn.
""",
    ),
    # ---------------------------------------------------------------- P1-11
    dict(
        code="P1-11",
        title="[CI] Automate the official-Roslyn CycCC residual validation (scripts/crossval) on main pushes",
        labels="enhancement",
        body="""Order: P1-11 | Effort: M | Impact: high | Verdict: needs-revision (corrections below)

## Summary

The CycCC counting convention is validated against the official Roslyn metrics engine via a documented manual procedure (`scripts/crossval/README.md:18-26`); `scripts/crossval/Program.cs:98-103` only dumps JSON. Automate the comparison so a broken counting convention fails CI.

## Reviewer corrections (from plan verification)

- A strict zero-residual gate is wrong: `docs/metrics.md:347` documents three types (HalsteadWalker/State/Walker) with known +-1 residuals from SyntaxOnly bool `&`/`|` type resolution and nested-type member matching. Implement the residual identity (delta = switchArms + catches + gotos - defaultLabels - memberBase) **with** the already-emitted `BoolAmpOr` term (`crossval Program.cs:91`) and an allowlist for the known +-1 types.
- Exclude source-generated partials per `scripts/crossval/README.md:32-35`.
- MSBuildWorkspace makes this heavy; run on main pushes and release tags, not every PR.

## Plan

1. Extend `scripts/crossval/Program.cs`: load unilyze JSON, compute per-type residuals, exit non-zero on unexplained residual.
2. Allowlist file for known +-1 types with reason strings.
3. New workflow `crossval.yml`: on push to main + release tags.

## Acceptance criteria

- [ ] Workflow green on current main.
- [ ] Injecting a counting change (e.g. drop catch increment locally) turns it red.
""",
    ),
    # ---------------------------------------------------------------- P1-12
    dict(
        code="P1-12",
        title="[Feature] Extend allocation detectors to property accessors, operators, local functions, and field initializers",
        labels="enhancement,metrics-compat",
        body="""Order: P1-12 | Effort: S-M | Impact: medium | Verdict: valid (code-verified)

## Summary

Boxing/Closure/Params detectors only walk method and constructor bodies; allocations in property accessors, indexers, operators, local functions, and field/property initializers are invisible. Shared member enumeration is also the groundwork for the detector registry ({P1_13}).

## Plan

1. Extract a shared "member bodies with executable code" enumerator (methods, ctors, accessors, operators, local functions, initializers) used by all detectors.
2. Extend the three allocation detectors to the new member kinds; method attribution for accessors uses `get_X`/`set_X` style names consistent with existing RFC/WMC conventions (verify and align).
3. Detected counts will increase on real projects => `metricsVersion` bump + CHANGELOG `[metrics]` entry.
4. Tests per new member kind.

## Acceptance criteria

- [ ] Boxing inside a property getter and a field initializer is detected (new tests).
- [ ] `metricsVersion` bumped; CHANGELOG entry.

## Dependencies

Requires {P1_07}.
""",
    ),
    # ---------------------------------------------------------------- P1-13
    dict(
        code="P1-13",
        title="[Refactor] Detector registry + per-smell line numbers (fix SARIF locations collapsing to method start)",
        labels="enhancement",
        body="""Order: P1-13 | Effort: M | Impact: medium | Verdict: valid (code-verified)

## Summary

Adding one detector today means editing the `CodeSmellKind` enum, three hardcoded spots in `SemanticEnricher` (`SemanticEnricher.cs:164-222`), the SARIF rule table (`SarifFormatter.cs:14-27`), and docs. Detector results carry line numbers but `SemanticEnricher.cs:204` drops them (`CodeSmell` record has no `Line`), so SARIF locations collapse to the method start (`SarifFormatter.cs:155-161`). This refactor is the prerequisite for the Phase 2 Unity detector family (UNI017+).

## Plan

1. Define `ISmellDetector` (input: type syntax + optional SemanticModel; output: occurrences with Kind/Method/Line/Severity/Message).
2. Registry replaces the hardcoded `DetectorResults` / `RunFeatureDetectors` / conversion blocks in `SemanticEnricher`.
3. Add `int? Line` to `CodeSmell`; SARIF regions use the smell's own line, falling back to method start.
4. Keep `diff` smell identity stable: verify the diff `SmellKey` does not incorporate Line (or version the key) so diffs across this change do not report phantom added/removed smells.
5. JSON schema: new optional `line` field on smells; document in `schema`.

## Acceptance criteria

- [ ] New detector addable by implementing one interface + registry entry + SARIF metadata in one place.
- [ ] SARIF reports point at the offending line for boxing/closure/params/exception smells.
- [ ] `diff` between pre/post-change snapshots of the same code reports no smell churn.

## Dependencies

Best after {P1_12} (shared member enumerator becomes a registry utility).
""",
    ),
    # ---------------------------------------------------------------- P1-14
    dict(
        code="P1-14",
        title="[Feature] unilyze diff -f markdown for PR comments and GITHUB_STEP_SUMMARY",
        labels="enhancement,quick-win",
        body="""Order: P1-14 | Effort: S | Impact: medium | Verdict: valid (code-verified)

## Summary

`DiffResult` is already fully structured; only a formatter is missing. A Markdown summary makes the diff gate visible in PRs (paste into `$GITHUB_STEP_SUMMARY` or a PR comment) and is the building block for the Phase 2 GitHub Action.

## Plan

1. Add `-f markdown` to `diff`: header verdict line (gate result if `--fail-on-regression`), table of CH avg/min delta, smell warning/critical delta, degraded/improved type counts, top-5 degraded types with their worst metric movement.
2. Keep it deterministic and compact (<= ~40 lines) for comment embedding.
3. Document a CI recipe in README (step summary + `gh pr comment` example).

## Acceptance criteria

- [ ] `unilyze diff a.json b.json -f markdown` renders a valid GFM table.
- [ ] Recipe documented; E2E test asserts key fields appear.
""",
    ),
    # ---------------------------------------------------------------- P1-15
    dict(
        code="P1-15",
        title="[Bug] refactor-loop skill: snapshot prefix mismatch fabricates Added/Removed entries; add diff --changed-only summary mode",
        labels="bug",
        body="""Order: P1-15 | Effort: S | Impact: medium | Verdict: valid (code-verified)

## Summary

The bundled refactor-loop skill's `--prefix` snapshot naming does not match what the skill later globs, so diffs can compare wrong snapshot pairs and report phantom Added/Removed types, breaking the loop's convergence check. Additionally agents consume full diff JSON when they usually need only changed entries.

## Plan

1. Fix the snapshot naming/glob mismatch in `src/Unilyze/Skills/` (refactor-loop) and document the naming convention.
2. Add `--changed-only` to `diff` JSON output: emit only added/removed/degraded/improved types plus the aggregate header (cuts agent token cost).
3. Update the skill to use `--changed-only`.

## Acceptance criteria

- [ ] Loop over an unchanged project reports zero Added/Removed.
- [ ] `--changed-only` output contains no unchanged types; E2E test added.
""",
    ),
    # ---------------------------------------------------------------- P1-16
    dict(
        code="P1-16",
        title="[Test] Cover test-less integration points: SemanticEnricher catch-all fallbacks and SkillInstaller E2E",
        labels="enhancement",
        body="""Order: P1-16 | Effort: M | Impact: medium | Verdict: needs-revision (scope note below)

## Summary

Test-reference audit found classes with zero direct test references. Highest risk: `SemanticEnricher`'s two catch-all fallback paths (semantic enrichment silently degrades on exception — exactly the kind of silent failure that corrupts metrics without anyone noticing), and `SkillInstaller` (file-writing logic, no E2E).

## Plan

1. Tests forcing both `SemanticEnricher` catch-all paths (e.g. a type that throws during semantic enrichment) asserting: analysis completes, degradation is reported (stderr/level), and remaining types are unaffected.
2. `SkillInstaller` E2E: install into a temp dir for each supported target; assert file layout and idempotent re-install.
3. While here, list remaining zero-reference classes in the PR for follow-up triage (do not try to cover everything in this issue).

## Acceptance criteria

- [ ] Both fallback paths exercised with explicit assertions on degradation behavior.
- [ ] SkillInstaller E2E for at least `--claude` target, temp-dir based.
""",
    ),
    # ---------------------------------------------------------------- P1-17
    dict(
        code="P1-17",
        title="[Refactor] Split Program.cs into per-subcommand runners (self-analysis blind spot)",
        labels="enhancement",
        body="""Order: P1-17 | Effort: M | Impact: medium | Verdict: needs-revision (corrections below)

## Summary

`Program.cs` concentrates subcommand dispatch and logic as top-level statements/methods, which unilyze's own analysis under-measures (the dogfooding blind spot called out in the quality-audit skill). Split into per-subcommand runner classes following the existing `StatuslineRunner`/`BadgeRunner` pattern.

## Reviewer corrections

- Extract `PrintSchema`'s large definition text into an embedded resource (or generated constant) rather than moving it verbatim into a runner — otherwise the refactor creates a new LongMethod finding in self-analysis.
- Pure refactor: byte-identical CLI behavior, no metric changes. Golden tests ({P1_10}) and E2E suite are the safety net; land after they exist.

## Plan

1. One runner class per subcommand (`config`, `skills`, `diff`, `hotspot`, `trend`, `metrics`, `schema`), mirroring `StatuslineRunner`/`BadgeRunner`.
2. `Program.cs` reduces to parse + dispatch.
3. Schema/metrics definition text to embedded resources.
4. Self-analysis after: no new Critical findings on unilyze itself.

## Acceptance criteria

- [ ] All E2E tests pass unchanged.
- [ ] Self-analysis CodeHealth does not regress (CI gate from {P1_06} stays green).

## Dependencies

After {P1_10} (golden tests protect behavior).
""",
    ),
    # ---------------------------------------------------------------- P1-18
    dict(
        code="P1-18",
        title="[Bug] Investigate rc=134 (SIGABRT) on a large benchmark project; remove dead components; add peak-RSS to the bench harness",
        labels="bug",
        body="""Order: P1-18 | Effort: M | Impact: medium | Verdict: needs-revision (notes below)

## Summary

The 132-project benchmark records one rc=134 (SIGABRT — likely OOM or runtime abort) that has never been root-caused. The bench harness has no memory measurement, so scalability claims cannot be verified. Separately, `BloomFilter128` and `LinearAllocator` appear unused (dead weight from an earlier optimization attempt).

## Plan

1. Reproduce the rc=134 case locally; capture peak RSS (`/usr/bin/time -l` on macOS, `-v` on Linux) and crash details; file findings in this issue.
2. Fix or mitigate (streaming, capping, or documented minimum memory) depending on root cause.
3. Confirm `BloomFilter128`/`LinearAllocator` are unreferenced (`rg` + compile) and delete them.
4. Add peak-RSS capture to the bench script output table.

## Acceptance criteria

- [ ] rc=134 root cause documented (and fixed, or a clear mitigation issue filed).
- [ ] Dead components removed; build green.
- [ ] Bench output includes peak RSS per project.
""",
    ),
    # ---------------------------------------------------------------- P1-19
    dict(
        code="P1-19",
        title="[CI] Add a windows-latest job and fix what actually breaks",
        labels="enhancement",
        body="""Order: P1-19 | Effort: M | Impact: medium | Verdict: needs-revision (approach note below)

## Summary

README lists "Windows is untested" as a known limitation. The audit found path-separator and OS-dependent assumptions are *possible* but unproven. Reviewer guidance: do not pre-emptively rewrite path handling — add the CI job first and fix only the failures it reveals.

## Plan

1. Extend `ci.yml` matrix with `windows-latest` (build + `dotnet test`).
2. Triage failures: path separators, temp-dir conventions, line endings in E2E asserts are the likely suspects.
3. Fix revealed issues; keep the job required once green.
4. Update README Known Limitations.

## Acceptance criteria

- [ ] windows-latest job green and required.
- [ ] Known Limitations updated.
""",
    ),
    # ---------------------------------------------------------------- P1-20
    dict(
        code="P1-20",
        title="[Docs] Define the .NET / Roslyn version support policy",
        labels="documentation",
        body="""Order: P1-20 | Effort: S | Impact: low-medium | Verdict: valid

## Summary

The tool targets .NET versions without a stated support policy (net9.0 is EOL in the support matrix; Roslyn package updates are ad hoc). Define and document: which TFMs are supported, when they are dropped, and how Roslyn (Microsoft.CodeAnalysis) versions are tracked.

## Plan

1. Decide TFM policy (proposal: track latest LTS + current STS; drop EOL within one minor release).
2. Document in README (Requirements) + README.dev.md (upgrade procedure).
3. Apply: align `Unilyze.csproj` targets with the policy; update Roslyn packages to the chosen line; full test pass.

## Acceptance criteria

- [ ] Written policy in docs.
- [ ] csproj matches the policy; tests green.
""",
    ),
    # ---------------------------------------------------------------- P1-21
    dict(
        code="P1-21",
        title="[Docs] Document missing metric definitions in docs/metrics.md (MI/CBO/DIT/Ca/Ce/Instability) with validity caveats",
        labels="documentation",
        body="""Order: P1-21 | Effort: M | Impact: medium | Verdict: valid

## Summary

`docs/metrics.md` documents the calculation and validation of several metrics in depth, but MI, CBO, DIT, Ca/Ce, and Instability lack definition sections (formula, counting convention, thresholds, caveats). Additionally, research consolidation flagged that MI's validity is weak (van Deursen's critique; Borg et al. ICSME 2024 benchmark shows MI aligns poorly with human assessment) — document this limitation explicitly as groundwork for the Phase 3 plan to demote MI to a reference metric.

## Plan

1. Add a definition section per missing metric: formula, exact counting convention as implemented (cite source file), thresholds from the registry ({P1_09}), known caveats.
2. Add a "validity notes" subsection for MI citing: van Deursen "Think Twice Before Using the Maintainability Index"; Borg et al., "Ghost Echoes Revealed" (ICSME 2024, arXiv:2408.10754).
3. Cross-link from README metric table.

## Acceptance criteria

- [ ] Every metric in the README table has a definition section in docs/metrics.md.
- [ ] MI validity caveat present with citations.

## Dependencies

After {P1_09} so thresholds quoted come from the single source.
""",
    ),
]

EPIC_TITLE = "[Epic] 2026-06 improvement roadmap - Phase 1: measurement reliability & quick wins"


def run(cmd, **kw):
    return subprocess.run(cmd, capture_output=True, text=True, **kw)


def main():
    created = {}
    for spec in ISSUES:
        body = spec["body"]
        # substitute {P1_NN} placeholders with already-created issue numbers
        for code, num in created.items():
            body = body.replace("{" + code.replace("-", "_") + "}", f"#{num}")
        # any unresolved placeholder refers to a later issue -> keep order code text
        body = re.sub(r"\{P1_(\d\d)\}", lambda m: f"P1-{m.group(1)} (later in this epic)", body)

        with tempfile.NamedTemporaryFile("w", suffix=".md", delete=False) as f:
            f.write(body)
            path = f.name
        r = run([
            "gh", "issue", "create", "--repo", REPO,
            "--title", spec["title"],
            "--body-file", path,
            "--label", spec["labels"],
            "--milestone", MILESTONE_P1,
        ])
        os.unlink(path)
        if r.returncode != 0:
            print(f"FAILED {spec['code']}: {r.stderr}", file=sys.stderr)
            sys.exit(1)
        url = r.stdout.strip()
        num = int(url.rsplit("/", 1)[-1])
        created[spec["code"]] = num
        print(f"{spec['code']} -> #{num} {url}")

    out = "/Volumes/CrucialX9/dev/github.com/bigdra50/unilyze/tasks/phase1-issues.json"
    with open(out, "w") as f:
        json.dump(created, f, indent=2)
    print(f"map written: {out}")


if __name__ == "__main__":
    main()

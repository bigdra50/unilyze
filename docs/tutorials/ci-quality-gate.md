# CI Quality Gate Tutorial

This walkthrough sets up a pull-request check that **fails when code quality regresses** and **posts a markdown diff summary** to the PR. It complements the command reference in [README.md](../../README.md) (Badges, Diff Viewer) and, when available, [docs/ci-integration.md](../ci-integration.md) for canonical workflow YAML and option tables.

unilyze runs at **SyntaxOnly** in CI (no Unity install required). Code Health and Maintainability Index are stable at this level; smell counts are level-dependent — see [docs/metrics.md](../metrics.md).

## Prerequisites

```bash
dotnet tool install --global Unilyze
```

When developing unilyze from source, prefix commands with:

```bash
dotnet run --project src/Unilyze -f net10.0 --
```

## Exit codes

All unilyze commands share one contract:

| Code | Meaning |
|------|---------|
| `0` | Success / gate passed |
| `1` | Usage error (unknown option, missing file, invalid argument) |
| `2` | Quality gate failed (`badge` with `--fail-under` / `--fail-over`, or `diff` with `--fail-on-regression`) |

CI should treat exit `2` as a failed check, not a crash.

## Step 1: Capture an after snapshot

On every PR, analyze the checked-out tree and write JSON:

```bash
unilyze -p . -f json -o after.json
```

Example output (stderr):

```
Analysis level: SyntaxOnly
Written to after.json
```

The resolved `analysisLevel` is also stored in the JSON (`"analysisLevel": "SyntaxOnly"`).

## Step 2: Absolute threshold gate (badge)

Block merges when minimum Code Health drops below a floor. This gate is independent of a baseline file — useful as a repo-wide policy line.

```bash
unilyze badge -p . --metric codehealth --fail-under 7
echo $?   # 0 = pass, 2 = fail
```

On pass, stdout is shields.io endpoint JSON (unchanged by gate flags):

```json
{"schemaVersion":1,"label":"code health","message":"9.7 / 8.0","color":"brightgreen","analysisLevel":"SyntaxOnly"}
```

On fail, stderr explains the reason and exit code is `2`:

```
gate failed: min CodeHealth 8 < 10
```

Other gate combinations:

```bash
unilyze badge -p . --metric mi --fail-under 70          # average MI
unilyze badge -p . --metric smells --fail-over 5        # warning count (any critical always fails)
```

Thresholds are **inclusive at the boundary**: a value exactly equal to `--fail-under` passes; strictly below fails.

## Step 3: Regression gate (diff)

Compare the PR against a baseline and fail when **project-wide aggregates** worsen: average or min Code Health dropped, or warning/critical smell counts increased.

### Option A: Cached baseline JSON (fastest)

Store a baseline snapshot on a branch or in CI artifacts (this repo dogfoods a `badges` branch — see [.github/workflows/badges.yml](../../.github/workflows/badges.yml)):

```bash
# Assume baseline.json was produced earlier with the same -p path and filters
unilyze diff baseline.json after.json --fail-on-regression
echo $?   # 0 = no regression, 2 = regression detected
```

Identical snapshots always pass:

```bash
unilyze diff after.json after.json -f markdown --fail-on-regression
# stderr: Diff summary; stdout: markdown tables; exit 0
```

### Option B: Git ref baseline (`--base-ref`)

No hand-built `before.json` required. unilyze materializes the git ref in a temporary worktree, analyzes it, and diffs against your after snapshot:

```bash
git fetch origin main   # or checkout with fetch-depth: 0
unilyze -p . -f json -o after.json
unilyze diff --base-ref origin/main after.json --fail-on-regression
```

Re-analysis of the base ref roughly doubles CI time; prefer a cached baseline when you control storage.

Use `-p` to override the project path (default: `projectPath` from the after snapshot). Pin the base-side level with `--level` when needed.

## Step 4: Post markdown to the PR

`diff -f markdown` emits GitHub-flavored tables for `$GITHUB_STEP_SUMMARY` and PR comments. Gate flags do not change the markdown body — only the exit code.

```bash
unilyze diff baseline.json after.json -f markdown >> "$GITHUB_STEP_SUMMARY"
unilyze diff baseline.json after.json -f markdown | gh pr comment "$PR_NUMBER" --body-file -
```

Example markdown excerpt:

```markdown
**Verdict:** PASS

### Code Health

| Metric | Before | After | Delta |
| --- | --- | --- | --- |
| Avg CH | 9.7 | 9.7 | 0 |
| Min CH | 8 | 8 | 0 |
```

With `--fail-on-regression`, a failing gate still prints the full markdown to stdout; CI marks the step failed via exit `2`.

## Step 5: Wire into GitHub Actions

Do **not** duplicate the full workflow YAML here. Canonical sources:

- Badge publishing: [.github/workflows/badges.yml](../../.github/workflows/badges.yml) and the [Badges section in README.md](../../README.md#badges)
- This repo's own gate: [.github/workflows/ci.yml](../../.github/workflows/ci.yml) (`quality-gate` job)

A minimal PR gate job runs these shell steps (adapt paths to your project):

```bash
dotnet tool install --global Unilyze
unilyze -p . -f json -o after.json
unilyze badge -p . --metric codehealth --fail-under 7
unilyze diff --base-ref origin/main after.json -f markdown --fail-on-regression >> "$GITHUB_STEP_SUMMARY"
```

For private repositories, generate SVG badges with `unilyze badge --format svg` and commit them under `.github/badges/` instead of using shields.io endpoint URLs — see [README.md](../../README.md#private-repositories).

## Aggregate vs per-type regression

`--fail-on-regression` evaluates **project-wide aggregates**. A single type can show `Degraded: 1` in the diff summary while aggregates stay flat (another type improved enough to offset it), yielding exit `0`. To gate on any individual type degrading, inspect the per-type `Degraded` count in the summary instead.

## Official GitHub Action (in progress)

A composite GitHub Action that bundles install, snapshot, gate, and PR comment steps is under development ([#79](https://github.com/bigdra50/unilyze/issues/79), coordinated with `diff --base-ref` in [#78](https://github.com/bigdra50/unilyze/issues/78)). Until it ships, use the manual steps above. When the Action lands, add it as a fast-path alternative — the exit-code contract and gate semantics stay the same.

## Quick local smoke test

Dogfood against this repository (verified commands):

```bash
mkdir -p /tmp/unilyze-gate-test
unilyze -p . -f json -o /tmp/unilyze-gate-test/after.json
unilyze badge -p . --metric codehealth --fail-under 7
unilyze diff /tmp/unilyze-gate-test/after.json /tmp/unilyze-gate-test/after.json \
  -f markdown --fail-on-regression
```

All three commands should exit `0` on a healthy tree.

# CI Integration

Reference for badges, quality gates, GitHub Actions, and diff-based PR gates. For a step-by-step walkthrough, see the [CI quality gate tutorial](./tutorials/ci-quality-gate.md).

unilyze runs at **SyntaxOnly** in CI when no Unity installation is present (no Unity DLLs resolved). Code Health and Maintainability Index are approximately stable across analysis levels; smell counts are level-dependent — only the syntax-level subset is reported at SyntaxOnly (semantic smells such as boxing are omitted). See [metrics.md](./metrics.md#validation) for measured differences.

## Badges

`unilyze badge` outputs [shields.io endpoint JSON](https://shields.io/badges/endpoint-badge) (default) or a flat SVG (`--format svg`).

```bash
unilyze badge -p ~/MyUnityProject                  # code health (default)
unilyze badge -p ~/MyUnityProject --metric mi      # maintainability index
unilyze badge -p ~/MyUnityProject --metric smells  # code smell count
unilyze badge -p ~/MyUnityProject --format svg -o .github/badges/codehealth.svg
```

| Metric | Label | Message | Color |
|--------|-------|---------|-------|
| `codehealth` | `code health` | `avg / min` (e.g. `9.2 / 6.1`) | by min: green ≥8.0, yellow ≥5.0, red below |
| `mi` | `maintainability` | average MI (method-bearing types) | green ≥80, yellow ≥60, red below |
| `smells` | `smells` | warning count | red if critical > 0, yellow if warnings > 0, green if 0 |

Use `--baseline <file>` to suppress known smells before computing badge values (pair with `unilyze baseline create`).

### Private repositories

The shields.io endpoint approach does not work in private repositories: GitHub's camo proxy and shields.io cannot fetch raw JSON from an authenticated-only repo. Generate the SVG with `unilyze badge --format svg`, commit it (e.g. under `.github/badges/`), and reference it from your README via a relative path:

```markdown
![Code Health](.github/badges/codehealth.svg)
```

Authenticated viewers see the badge inline without camo or an external fetch.

## Quality gates

`unilyze badge` can act as a CI gate. Without gate flags the output is unchanged and exit code stays `0`.

```bash
unilyze badge --metric codehealth --fail-under 7   # fail if min CodeHealth < 7
unilyze badge --metric mi --fail-under 70          # fail if average MI < 70
unilyze badge --metric smells --fail-over 5        # fail if warnings > 5 (or any critical)
unilyze badge --metric energy --fail-over 1.0      # fail if Unity hot-path smell density > 1.0
```

| Flag | Valid metrics | Fails when |
|------|---------------|-----------|
| `--fail-under <value>` | `codehealth`, `mi` | min CodeHealth (codehealth) or average MI (mi) is **strictly below** `value` |
| `--fail-over <value>` | `smells`, `energy`, `dup` | warning count, energy pressure, or duplication is **strictly above** `value` |

Thresholds are inclusive at the boundary.
Values exactly at `--fail-under` or `--fail-over` pass.
Mismatched combinations, such as `--fail-under` with `--metric energy`, are a usage error (exit `1`).
Energy pressure is a static proxy, not measured energy or power.

The gate is **fail-closed**: if the metric is unavailable (0 types analyzed, or no method-bearing types for `mi`), the gate exits `2` with `gate failed: metric unavailable (...)` rather than passing — this catches a mistyped `-p` path that would otherwise produce a false green.

Exit codes: `0` success / gate passed, `1` usage error, `2` quality gate failed.

## GitHub Action

Use the official composite action instead of copy-pasting workflow YAML:

```yaml
# .github/workflows/unilyze.yml
name: Unilyze
on:
  pull_request:
  push:
    branches: [main]

permissions:
  contents: read
  # Required only when uploading SARIF in a follow-up step:
  # security-events: write

jobs:
  quality:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0   # required when base-ref is set

      - uses: bigdra50/unilyze@v1
        id: unilyze
        with:
          project-path: .
          metric: codehealth
          fail-under: "7.0"
          base-ref: origin/main          # optional diff regression gate
          fail-on-regression: "true"
          baseline: .unilyze/baseline.json  # optional brownfield baseline
          sarif: "false"                   # set true and upload sarif-path in a later step

      # Optional: upload SARIF when sarif: true
      # - uses: github/codeql-action/upload-sarif@v3
      #   with:
      #     sarif_file: ${{ steps.unilyze.outputs.sarif-path }}
      #     category: unilyze
```

| Input | Default | Description |
|-------|---------|-------------|
| `project-path` | `.` | Project directory to analyze |
| `metric` | `codehealth` | Gate metric: `codehealth`, `mi`, or `smells` |
| `fail-under` | *(empty)* | Fail when min CodeHealth or avg MI is below this value |
| `fail-over` | *(empty)* | Fail when smell warnings exceed this count |
| `base-ref` | *(empty)* | Git ref for diff gate; writes markdown to `$GITHUB_STEP_SUMMARY` |
| `baseline` | *(empty)* | Baseline JSON path to suppress known smells |
| `sarif` | `false` | Emit SARIF; upload with `upload-sarif` using the `sarif-path` output |
| `pr-comment` | `false` | Upsert one sticky PR comment with diff markdown |

Outputs: `codehealth`, `mi`, `smells`, `gate-result` (`passed` / `failed` / `skipped`), and `sarif-path` when SARIF is generated.

## Publishing badges from Actions

Generate SVG on every push to `main` and serve from a `badges` branch (this repository dogfoods the same pattern — see [badges.yml](https://github.com/bigdra50/unilyze/blob/main/.github/workflows/badges.yml)):

```yaml
# .github/workflows/badges.yml
name: Badges
on:
  push:
    branches: [main]
permissions:
  contents: write # force-push to the badges branch
jobs:
  badges:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x
      - run: dotnet tool install --global Unilyze
      - run: |
          mkdir -p /tmp/badge-data
          unilyze badge -p . --format svg -o /tmp/badge-data/codehealth.svg
      - run: |
          cd /tmp/badge-data
          git init -q -b badges
          git config user.name "github-actions[bot]"
          git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
          git add .
          git commit -qm "update badges"
          git push -f "https://x-access-token:${GITHUB_TOKEN}@github.com/${GITHUB_REPOSITORY}.git" badges
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

Reference from your README:

```markdown
![Code Health](https://raw.githubusercontent.com/<owner>/<repo>/badges/codehealth.svg)
```

Prefer shields.io styling? Generate endpoint JSON (default format) and embed `https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/<owner>/<repo>/badges/codehealth.json`.

## Diff regression gate

`--fail-on-regression` turns `diff` into a CI gate. Output (JSON, HTML, markdown, or stderr summary) is unchanged; only the exit code reflects the gate.

```bash
unilyze diff before.json after.json --fail-on-regression
```

A regression is any of: average or min CodeHealth dropped, warning smell count increased, or critical smell count increased (after vs before). On regression, the reason is printed to stderr on one line (e.g. `regression: min CodeHealth 7.2 -> 6.8`).

The gate evaluates **project-wide aggregates**, distinct from per-type `Degraded`/`Improved` counts in the diff summary. A single type can degrade while aggregates stay flat (another type improved enough to offset it), so `Degraded: 1` in the summary can coexist with exit `0`. Gate on individual type degradation by inspecting per-type counts instead.

Exit codes: `0` no regression, `1` usage error, `2` regression detected.

### Markdown output (PR comments)

```bash
unilyze diff before.json after.json -f markdown >> "$GITHUB_STEP_SUMMARY"
unilyze diff before.json after.json -f markdown | gh pr comment "$PR" --body-file -
```

With `--fail-on-regression`, the markdown body is unchanged; only the exit code reflects the gate.

### PR workflow (`--base-ref`)

Materializes a git ref in a temporary worktree, analyzes it in-process, and diffs against your after snapshot — no hand-built `before.json` required. Re-analysis roughly doubles CI time; caching a baseline JSON on a branch is faster when you control storage.

```bash
git fetch origin main   # or use fetch-depth: 0 in checkout
unilyze -p . -o after.json
unilyze diff --base-ref origin/main after.json -f markdown --fail-on-regression
```

Use `-p` to override the project path (default: `projectPath` from the after snapshot). Use `--level` to pin the base-side analysis level. Unknown refs, non-repo directories, and a missing `git` binary exit `1` with a one-line stderr hint.

Other diff flags useful in CI: `--changed-only` (omit unchanged types from JSON output), `--fail-on-version-mismatch` (exit `2` when `metricsVersion` differs).

## Analysis levels in CI

| Level | Resolved | What is accurate | What is understated |
|-------|----------|------------------|---------------------|
| `SyntaxOnly` | No Unity DLLs | CodeHealth, MI, cyclomatic/cognitive complexity, syntactic smells | Boxing, params allocations, CBO, DIT, inheritance across engine types |
| `CoreEngine` | UnityEngine core + framework | + types referencing `UnityEngine` core | Editor/module types, package assemblies |
| `FullEngine` | + engine/editor modules | + editor and module types | Compiled package assemblies |
| `Complete` | + package assemblies | full semantic resolution | — |

Pin with `--level <syntax|core|full|complete>` on `unilyze`, `badge`, `statusline`, and `baseline create`. The pin caps auto-resolved level; if the requested level cannot be reached, the command fails instead of silently degrading.

## Monorepo

Use `--projects <glob>` (repeatable) on `unilyze` and `unilyze badge` to analyze multiple UPM packages or .NET projects in one CI step instead of copy-pasting per-package commands. Each matched directory resolves its own `.unilyze.json`, profile, and baseline before gating. Projects are processed sequentially (one Roslyn compilation at a time) to avoid multiplying memory pressure from parallel compilations.

```bash
# Analyze: per-project AnalysisResult JSON + summary.json
unilyze --projects 'packages/*' -o out/ -f json
unilyze --projects 'src/*' --projects 'tests/*' -o out/ -f json

# Badge gate: per-project badge files + stderr summary table
unilyze badge --projects 'packages/*' --metric codehealth --fail-under 7.0 -o badges/
```

| Output | Path pattern |
|--------|--------------|
| Per-project snapshot | `<out>/<name>.json` (standard `AnalysisResult`; readable by `query`, `diff`, HTML viewer) |
| Per-project SARIF | `<out>/<name>.sarif` (`runAutomationDetails.id` = project name) |
| Per-project badge | `<out>/<name>-<metric>.json` or `.svg` |
| Aggregate summary | `<out>/summary.json` |

Project `name` is the glob-relative path with directory separators replaced by `-` (e.g. `packages/a/Runtime` → `a-Runtime`). Exit codes: `0` all gates pass, `1` usage error (zero glob matches, `--projects` with `-p`/`-i`, missing `-o <dir>` when multiple projects match, `-f html`), `2` any gate failure. All badge files are written before a gate failure exits `2`.

`summary.json` shape (informal versioning via `toolVersion`):

```json
{
  "toolVersion": "0.12.0",
  "projects": [
    {
      "name": "a",
      "path": "/repo/packages/a",
      "analysisLevel": "Complete",
      "metricsVersion": 3,
      "codeHealthMin": 9.1,
      "codeHealthAvg": 9.8,
      "criticalCount": 0,
      "warningCount": 0,
      "gate": "pass"
    }
  ]
}
```

### Monorepo vs matrix strategy

| Approach | Pros | Cons |
|----------|------|------|
| Single job with `--projects` | One checkout, one tool install, aggregated stderr table and `summary.json` | Sequential analysis; longer wall time than parallel matrix |
| Matrix job per package | Parallel Roslyn runs; per-package job isolation | Duplicate setup; aggregate pass/fail yourself |

Choose `--projects` when a single failing package should fail the PR and you want one aggregated gate. Choose a matrix when packages are independent and wall time matters more than a unified summary.

### SARIF upload per project

`unilyze --projects 'packages/*' -f sarif -o sarif/` writes one SARIF file per project. On GitHub, set a distinct `category` per upload so multiple runs on one commit do not overwrite each other:

```yaml
- run: unilyze --projects 'packages/*' -f sarif -o sarif/
- uses: github/codeql-action/upload-sarif@v3
  with:
    sarif_file: sarif/a.sarif
    category: package-a
- uses: github/codeql-action/upload-sarif@v3
  with:
    sarif_file: sarif/b.sarif
    category: package-b
```

`upload-sarif`'s `category` input injects `runAutomationDetails.id` at upload time. The CLI also sets `runAutomationDetails.id` to the project name for non-GitHub upload paths.

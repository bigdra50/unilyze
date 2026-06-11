# Continuous Refactoring Tutorial

Sustained improvement needs **prioritization** (where to spend effort) and **trend tracking** (whether the codebase is actually getting healthier). This tutorial covers `hotspot` and `trend`, and how they fit the refactor-loop skill's iterative workflow.

## Prerequisites

```bash
dotnet tool install --global Unilyze
```

From source:

```bash
dotnet run --project src/Unilyze -f net10.0 --
```

`hotspot` requires **git history** in the project path. `trend` requires a directory of analysis JSON snapshots.

## Hotspot analysis

`unilyze hotspot` ranks types by **git churn × complexity × low Code Health**. Types that change often *and* are hard to maintain deliver the highest return on refactoring effort (Tornhill & Borg, TechDebt 2022).

### Command reference

```bash
unilyze hotspot --help
```

| Option | Description | Default |
|--------|-------------|---------|
| `-p`, `--path` | Project root (also used for `git log`) | `.` |
| `-i`, `--input` | Reuse an existing analysis JSON (skip re-analysis) | — |
| `--since` | Git log period | `12.month` |
| `-n` | Top N results | `20` |
| `--exclude-dir` | Exclude directory (repeatable) | — |
| `--no-bot-filter` | Include bot-authored commits in churn | bots excluded |
| `--bot-pattern` | Additional bot author regex (repeatable) | — |
| `--half-life` | Exponential decay half-life (e.g. `90.day`, `6.month`) | off |
| `--methods` | Method-level X-Ray for one `.cs` file | — |
| `-o`, `--output` | Write JSON to file | stdout |

### Basic run

```bash
unilyze hotspot -p .
```

Example stderr table (this repository, top 5):

```
Hotspot analysis: . (since 12.month)
  Total hotspots: 5

  Rank  Score   Churn  Health  Type
  ----  ------  -----  ------  ----
     1    34.2     18     8.1  Unilyze.AnalysisPipeline
     2    28.5     15     8.1  Unilyze.ProgramHelpers
     3    20.0     10     8.0  Unilyze.StatuslineRunner
```

### All options in one command

```bash
unilyze hotspot -p . \
  -i snapshot.json \
  --since 6.month \
  -n 10 \
  --exclude-dir tests \
  -o hotspots.json
```

Reusing `-i` skips a second full analysis pass — useful when you already have a CI snapshot.

### Bot commit exclusion

By default, `hotspot` excludes commits from known automation accounts (GitHub `[bot]` suffix, dependabot, renovate, github-actions, and others). Research shows bot traffic can dominate hotspot churn counts. Stderr reports how many commits were excluded; JSON includes `botFilter` and `botCommitsExcluded`.

```bash
unilyze hotspot -p .                        # bots excluded (default)
unilyze hotspot -p . --no-bot-filter        # raw counts, matches pre-upgrade behavior
unilyze hotspot -p . --bot-pattern 'ci-.*'  # extend built-in matcher
```

### Time-decay weighting

Opt in with `--half-life` so recent commits weigh more than old ones. Weight per commit is `2^(-age/halfLife)` where age is measured from the newest in-window commit (deterministic for a fixed history). `changeCount` stays the raw count; `weightedChurn` and scores use decay when enabled.

```bash
unilyze hotspot -p . --half-life 90.day
unilyze hotspot -p . --half-life 6.month -n 10
```

### Method-level X-Ray

`--methods <file>` ranks individual methods in one file by churn × cognitive complexity using `git log -L` over Roslyn-derived line ranges. Works at SyntaxOnly level (no Unity DLLs required).

```bash
unilyze hotspot -p . --methods src/Unilyze/AnalysisPipeline.cs -o method-hotspots.json
```

Bot filtering and decay both apply in method mode. JSON output includes `methodHotspots[]` alongside the usual type-level `hotspots[]`.

### Interpreting hotspot scores

Each hotspot entry includes:

| Field | Meaning |
|-------|---------|
| `changeCount` | Raw git commits touching the type's file(s) in `--since` |
| `weightedChurn` | Present when `--half-life` is set; decay-weighted commit sum |
| `codeHealth` | Composite health (1.0 worst – 10.0 best) |
| `hotspotScore` | `churn × (10.0 - codeHealth)`; churn is `weightedChurn` when decay is on |
| `averageCognitiveComplexity` / `maxCognitiveComplexity` | Complexity context |

**Prioritize hotspot order over raw CodeHealth order** when git history is available. A type with Code Health 7.5 that ships every sprint matters more than a 6.0 type untouched for years.

Note: `--name-only` does not follow renames, so a renamed file's churn resets at the rename boundary.

### Fallback when git history is unavailable

In non-git directories or repos with insufficient history, `hotspot` may produce no useful ranking. The refactor-loop skill then falls back to **CodeHealth ordering** via `unilyze query --worst N`.

```bash
unilyze hotspot -p . 2>&1 || echo "hotspot unavailable, using CodeHealth order"
unilyze query --worst 5 -p .
```

## Trend tracking

`unilyze trend` reads multiple analysis snapshots from a directory and reports how project-wide quality changed over time.

### Snapshot directory convention

Accumulate one JSON file per meaningful point — release tag, weekly CI run, or post-refactor checkpoint:

```
.unilyze/history/
  2026-05-01.json
  2026-06-01.json
  2026-06-11.json
```

Produce snapshots with the **same project path and filters** each time:

```bash
HISTORY_DIR=".unilyze/history"
mkdir -p "$HISTORY_DIR"

unilyze -p . -f json -o "$HISTORY_DIR/$(date +%Y-%m-%d).json"
```

Filenames are sorted lexicographically; use `YYYY-MM-DD` (or ISO timestamps) for chronological order.

### Run trend

```bash
unilyze trend .unilyze/history
```

Example stderr summary:

```
Trend: 3 snapshot(s)
  CodeHealth delta:  +0.3
  CodeSmell delta:   -12

  Date                Types  Health  Smells  HighCC  AvgCogCC
  ------------------  -----  ------  ------  ------  --------
  2026-05-01 10:00    198     9.4     346       1       1.7
  2026-06-01 10:00    200     9.6     340       0       1.6
  2026-06-11 10:00    200     9.7     334       0       1.6
```

### Summary table columns

| Column | Meaning |
|--------|---------|
| **Date** | `analyzedAt` from the snapshot |
| **Types** | Count of analyzed types |
| **Health** | Average Code Health across types |
| **Smells** | Total warning-level code smells |
| **HighCC** | Types with high complexity smells |
| **AvgCogCC** | Average cognitive complexity across types |

**CodeHealth delta** and **CodeSmell delta** in the header compare the first and last snapshot in the sorted set.

Save structured output:

```bash
unilyze trend .unilyze/history -o trend.json
```

### metricsVersion warning

When snapshots were produced under different metric definition versions, stderr warns:

```
Warning: metrics versions differ across snapshots (1, 2). Trend deltas may be unreliable.
```

Do not compare pre/post [metricsVersion](../metrics.md) bump snapshots for gate or trend decisions. Re-baseline after a version change.

## The continuous improvement loop

Combine hotspot prioritization, per-round diff, and trend accumulation:

```
┌─────────────────────────────────────────────────────────┐
│  CI / release: unilyze -f json → .unilyze/history/      │
└──────────────────────────┬──────────────────────────────┘
                           ▼
┌─────────────────────────────────────────────────────────┐
│  unilyze trend .unilyze/history   (are we improving?)   │
└──────────────────────────┬──────────────────────────────┘
                           ▼
┌─────────────────────────────────────────────────────────┐
│  unilyze hotspot -p .             (what to fix next?)   │
└──────────────────────────┬──────────────────────────────┘
                           ▼
┌─────────────────────────────────────────────────────────┐
│  refactor one type → test → unilyze diff (verdict)      │
└──────────────────────────┬──────────────────────────────┘
                           │
                           └──── repeat ────┘
```

### Per-round diff gate

After each refactor, compare before/after snapshots (see [agent-integration.md](./agent-integration.md)):

```bash
unilyze diff "$UNILYZE_DIR/refactor-before.json" \
             "$UNILYZE_DIR/refactor-after.json" \
  --changed-only --fail-on-regression
```

Exit `2` means aggregate quality regressed — fix before starting the next hotspot.

### Goodhart's law caveat

Optimizing metrics alone can harm readability (excessive method splitting, boxing workarounds that obscure intent). After each round, confirm qualitatively that maintainability improved — not just that numbers moved. The refactor-loop skill explicitly warns against metric-gaming.

## CI integration pointers

- **PR regression gate:** [ci-quality-gate.md](./ci-quality-gate.md) — `diff --fail-on-regression` and `--base-ref`
- **Badge floor:** `unilyze badge --fail-under` for absolute policy lines
- **History snapshots:** add `unilyze -p . -f json -o .unilyze/history/$(date +%Y-%m-%d).json` to a scheduled or post-merge workflow

## Quick local smoke test

Verified against this repository:

```bash
mkdir -p /tmp/unilyze-trend-test
unilyze -p . -f json -o /tmp/unilyze-trend-test/snap.json
unilyze hotspot -p . -n 5
unilyze hotspot -p . -i /tmp/unilyze-trend-test/snap.json -n 3
cp /tmp/unilyze-trend-test/snap.json /tmp/unilyze-trend-test/2026-06-11.json
unilyze trend /tmp/unilyze-trend-test
```

All commands should exit `0`.

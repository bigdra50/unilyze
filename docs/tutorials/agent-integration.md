# Agent Integration Tutorial

unilyze pairs **deterministic measurement** with **LLM interpretation**: agents read numeric evidence, propose refactors, and verify improvements with `diff`. This tutorial covers skill installation, bundled workflows, snapshot conventions, and CLI self-discovery.

The bundled `SKILL.md` files (Japanese) remain the authoritative agent instructions; this document is an English walkthrough for humans.

## Prerequisites

```bash
dotnet tool install --global Unilyze
```

From source:

```bash
dotnet run --project src/Unilyze -f net10.0 --
```

## Install skills

`unilyze skills install` copies embedded skills into each AI tool's skills directory.

| Target flag | Tool | Install location |
|-------------|------|------------------|
| `--claude` | Claude Code | `.claude/skills/` |
| `--codex` | Codex CLI | `.codex/skills/` |
| `--cursor` | Cursor | `.cursor/skills/` |
| `--gemini` | Gemini CLI | `.gemini/skills/` |
| `--windsurf` | Windsurf | `.windsurf/skills/` |

```bash
# Single target
unilyze skills install --claude

# Multiple targets
unilyze skills install --claude --codex --cursor

# Global install (~/.claude/skills/, etc.)
unilyze skills install --claude --global
```

Check status:

```bash
unilyze skills list --claude
```

Example output:

```
Claude Code (Project):
Location: /path/to/project/.claude/skills
  ✗ quality-audit (not installed)
  ✗ refactor-loop (not installed)
```

Two skills ship today: **quality-audit** (broad audit) and **refactor-loop** (iterative improvement). After install, invoke them via your tool's skill mechanism (e.g. `/quality-audit`, `/refactor-loop` in Claude Code).

## Snapshot conventions (`.unilyze/`)

Store analysis JSON under the repository root so agents and humans share the same artifacts:

```bash
UNILYZE_DIR="$(git rev-parse --show-toplevel 2>/dev/null || pwd)/.unilyze"
mkdir -p "$UNILYZE_DIR"
```

| File | Purpose |
|------|---------|
| `quality-audit.json` | Baseline from a quality audit |
| `refactor-before.json` | Before snapshot for a refactor round |
| `refactor-after.json` | After snapshot for a refactor round |

**Same-filter rule:** before and after snapshots must use identical scope flags (`--prefix`, `-a`, `--exclude-dir`). Mismatched filters produce spurious Added/Removed types in `diff`.

Example with a prefix filter (recommended for Unity projects to exclude third-party code):

```bash
UNILYZE_FILTER="--prefix App."

unilyze -p . $UNILYZE_FILTER -f json -o "$UNILYZE_DIR/quality-audit.json"
```

## Quality-audit workflow

The bundled quality-audit skill runs three phases:

```
Phase 1: unilyze snapshot + query evidence packs
    ↓
Phase 2: AI reads worst-type source, proposes fixes
    ↓
Phase 3: AI checks measurement blind spots (top-level statements, etc.)
```

### Phase 1: Quantitative baseline

```bash
unilyze -p . -f json -o "$UNILYZE_DIR/quality-audit.json"
# Optional: include API surface for naming/intent/comment review grounding
unilyze -p . -f json --include-api-surface -o "$UNILYZE_DIR/quality-audit.json"
```

Discover worst types with **evidence packs** — token-efficient per-type summaries with `file:line` anchors, smells, dependencies, and top methods:

```bash
# Worst 5 types (markdown, default)
unilyze query --worst 5 -i "$UNILYZE_DIR/quality-audit.json"

# With API surface (doc comments, signatures, identifiers)
unilyze query --worst 5 -i "$UNILYZE_DIR/quality-audit.json" --include-api-surface

# Single type as JSON
unilyze query --type MyService -i "$UNILYZE_DIR/quality-audit.json" -f json

# Direct analysis (no snapshot file)
unilyze query --worst 5 -p .
```

Example pack header:

```
## Unilyze.CodeHealthCalculator — CH 8.0 @ `./src/Unilyze/CodeHealthCalculator.cs:58`
```

### Phase 2: Targeted AI review

Read only the worst-type source files identified in Phase 1. Focus on:

- Root cause of bad metrics (not just the numbers)
- Concrete refactor proposals (extract method, split class, etc.)
- Runtime risks metrics cannot see

**CycCC vs CogCC:** high CycCC signals testability pressure (many branches); high CogCC signals human readability cost (nesting, boolean logic). Use both when choosing a strategy.

### Phase 3: Blind-spot check

unilyze does not measure everything (e.g. some top-level patterns). The skill's `references/blind-spots.md` lists gaps; the agent supplements with qualitative review.

## Refactor-loop workflow

The refactor-loop skill iterates until Code Health converges toward a target (default 8.0, max 5 rounds):

```
baseline snapshot
    → hotspot prioritization (if git history available)
    → pick worst type
    → refactor one type
    → run tests
    → unilyze diff (quantitative verdict)
    → repeat or stop
```

Pseudocode:

```python
snapshot = get_or_create_baseline(path)
hotspots = unilyze_hotspot(path)    # churn × complexity when git history exists
targets = identify_worst_types(snapshot, hotspots, threshold=target)
# Without hotspots (non-git / thin history): fall back to CodeHealth ordering

for round in range(1, max_rounds + 1):
    type_to_fix = pick_worst(targets)
    refactor(type_to_fix)
    run_tests()
    diff = unilyze_diff(snapshot)
    if all_above_target(diff):
        break
    if has_degradation(diff):
        fix_degradation()
    snapshot = update_snapshot()
```

### Step 1: Baseline and hotspot

```bash
# Reuse quality-audit snapshot when filters match
if [ -f "$UNILYZE_DIR/quality-audit.json" ]; then
  cp "$UNILYZE_DIR/quality-audit.json" "$UNILYZE_DIR/refactor-before.json"
else
  unilyze -p . $UNILYZE_FILTER -f json -o "$UNILYZE_DIR/refactor-before.json"
fi

# Hotspot once per loop (optional; continues on failure)
unilyze hotspot -p . 2>&1 || echo "hotspot unavailable, using CodeHealth order"
```

When hotspots are available, prioritize types that are **frequently changed and unhealthy**. Pure CodeHealth ordering can waste effort on rarely touched code.

Pick the next target:

```bash
unilyze query --worst 1 -i "$UNILYZE_DIR/refactor-before.json"
```

### Step 2: Refactor one type

Use this smell-to-strategy table (from the bundled skill):

| Code smell / metric | Strategy |
|---------------------|----------|
| GodClass (lines > 500) | Split by responsibility |
| LongMethod (lines > 60) | Extract methods |
| HighComplexity (CogCC > 25) | Flatten branches, early return, strategy pattern |
| DeepNesting (depth > 4) | Guard clauses, extract method |
| HighCoupling (CBO > 14) | Introduce interfaces, invert dependencies |
| ExcessiveParameters (> 5) | Parameter object |
| LowCohesion (LCOM > 0.8) | Move related methods + fields to a new class |
| BoxingAllocation | struct overrides, generic constraints, Span |
| ClosureCapture | static lambda, pass locals as parameters |
| ParamsArrayAllocation | pre-allocate array, Span overload |
| CatchAllException | catch specific types, rethrow when needed |
| MissingInnerException | `throw new X("msg", inner)` |
| ThrowingSystemException | use concrete exception types |

Refactor **one type per round**. Do not batch multiple types — `diff` cannot attribute regressions cleanly.

### Step 3: Test

```bash
dotnet test   # or your project's test command
```

### Step 4: Quantitative verdict

```bash
unilyze -p . $UNILYZE_FILTER -f json -o "$UNILYZE_DIR/refactor-after.json"
unilyze diff "$UNILYZE_DIR/refactor-before.json" "$UNILYZE_DIR/refactor-after.json" --changed-only
```

Interpretation:

- `Degraded = 0` and target type Code Health ≥ target → success, move to next type
- `Degraded = 0` but Code Health still below target → continue on same type
- `Degraded > 0` → revert or fix degradation before proceeding

## Self-documenting CLI

Agents discover definitions without external docs:

```bash
unilyze metrics   # metric definitions and smell thresholds
unilyze schema    # JSON field reference (typeMetrics, assemblies, etc.)
unilyze query --help
```

## jq recipes (snapshot JSON)

Prefer `unilyze query` for agent evidence packs. For bulk snapshot queries, `typeMetrics` holds per-type Code Health:

```bash
# Average and minimum Code Health
jq '[.typeMetrics[] | .codeHealth] | (add / length), min' snapshot.json

# Three worst types by Code Health
jq '[.typeMetrics | sort_by(.codeHealth) | .[0:3][] |
     {name: .qualifiedName, ch: .codeHealth}]' snapshot.json
```

Example output:

```json
[
  {"name": "Unilyze.CodeHealthCalculator", "ch": 8},
  {"name": "Unilyze.StatuslineRunner", "ch": 8},
  {"name": "Unilyze.AnalysisPipeline", "ch": 8.1}
]
```

## Claude Code status line (optional)

`unilyze statusline` emits a one-line summary for Claude Code's status bar. See [docs/statusline.md](../statusline.md) for the shell snippet and `--background-refresh` setup.

## Agent workflow summary

```
unilyze (measure) → unilyze query (evidence) → fix → unilyze diff (verify)
```

Install skills once per tool, keep snapshots in `.unilyze/` with consistent filters, and let `diff` be the quantitative judge after every change.

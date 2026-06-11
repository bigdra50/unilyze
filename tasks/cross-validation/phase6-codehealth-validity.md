# Phase 6: CodeHealth validity evidence (cross-validation)

Issue #90 — evidence base for Phase 3 CodeHealth v2 weight redesign. SonarQube output is **not** used as ground truth (Ghost Echoes / ICSME 2024 constraint).

## Corpus

Re-measured with current unilyze (`metricsVersion: 3`, `--level syntax`). Repos cloned under `/tmp/cross-validation-repos/`; pinned SHAs in `data/phase6-corpus-shas.json`.

| Project | Repository | SHA (mv2 snapshot) | Types (mv2) |
|---------|------------|-------------------|-------------|
| BossRoom | Unity-Technologies/com.unity.multiplayer.samples.coop | `1299ba4f` | 226 |
| HelloMarioFramework | HelloFangaming/HelloMarioFramework | `a0cb6803` | 109 |
| UniTask | Cysharp/UniTask | `a9e27c03` | 685 |
| VContainer | hadashiA/VContainer | `d9f6dc6b` | 234 |
| Unilyze (self) | bigdra50/unilyze | `21f5d31b` | 202 |

Legacy v0.1.x snapshots remain in `data/unilyze-*.json` (no `metricsVersion` field).

## Scripts

| Script | Purpose |
|--------|---------|
| `scripts/phase6_remeasure.sh` | Clone corpus + emit `data/unilyze-*-mv2.json` |
| `scripts/phase6_fetch_history.sh` | Unshallow clones for git-log analysis |
| `scripts/phase6_collinearity.py` | CogCC × LOC + 6×6 input matrix |
| `scripts/phase6_bugfix_density.py` | SZZ-lite bug-fix density vs CodeHealth |

## 1. Collinearity (CogCC × line count)

### Per-method (pooled, mv2)

| Metric | n | Pearson r | Spearman ρ |
|--------|---|-----------|------------|
| method CogCC × method lineCount | 6157 | **0.859** | **0.775** |

Method-level CogCC and LOC are strongly collinear across the corpus. This supports Lavazza et al. (JSS 2023): at method granularity, CogCC carries much of the same information as size.

### Per-type avgCogCC × lineCount (pooled, mv2)

| Metric | n | Pearson r | Spearman ρ |
|--------|---|-----------|------------|
| type avgCogCC × type lineCount | 1456 | 0.284 | **0.644** |

Aggregation to type level reduces but does not remove collinearity. Spearman remains moderate–strong (ρ ≈ 0.64); Pearson is weaker (r ≈ 0.28) because complexity grows sub-linearly with type size.

### Stability: legacy vs re-measured (pooled type avgCogCC × lineCount)

| Snapshot | Spearman ρ | Pearson r |
|----------|------------|-----------|
| Legacy v0.1.x | 0.549 | 0.375 |
| mv2 (current) | 0.644 | 0.284 |

Rank correlation is stable and slightly stronger on mv2; Pearson dropped. Conclusion structure (CogCC and LOC share variance) holds across tool versions.

### 6×6 input correlation matrix (type level, pooled mv2)

Spearman ρ / Pearson r for `CalculateHealthScore` raw inputs:

| Input | avgCogCC | maxCogCC | lineCount | methodCount | maxNesting | excessiveParams |
|-------|----------|----------|-----------|-------------|------------|-----------------|
| avgCogCC | 1.00 / 1.00 | 0.98 / 0.82 | **0.64 / 0.28** | 0.58 / 0.08 | 0.94 / 0.73 | 0.19 / 0.02 |
| maxCogCC | 0.98 / 0.82 | 1.00 / 1.00 | **0.69 / 0.35** | 0.66 / 0.15 | 0.96 / 0.69 | 0.20 / 0.02 |
| lineCount | 0.64 / 0.28 | 0.69 / 0.35 | 1.00 / 1.00 | 0.75 / 0.71 | 0.68 / 0.24 | 0.23 / 0.58 |

Implications for v2 weights:

- **avgCogCC ↔ maxCogCC**: ρ = 0.98 — nearly redundant; combined 45% weight double-counts the same axis.
- **CogCC ↔ lineCount**: ρ = 0.64 (avg), 0.69 (max) — effective size weight exceeds the documented 15% lineCount term.
- **lineCount ↔ methodCount**: ρ = 0.75 — additional size-axis overlap.
- **maxNesting ↔ CogCC**: ρ ≈ 0.94–0.96 — nesting depth tracks cognitive complexity closely.

Full per-project tables: `phase6-collinearity-results.md`, machine-readable: `data/phase6-collinearity.json`.

## 2. External validity (bug-fix commit density)

### Heuristic (SZZ-lite)

Bug-fix if commit subject matches `fix:` / `bugfix` / `hotfix` / `fixes #N` / `bug fix` patterns, excluding typo/lint/format/docs/test-only subjects. Touched `.cs` files mapped to types via path-suffix matching (same logic as `HotspotAnalyzer.ResolveChangeCount`).

**Precision** (manual re-label of positive sample, target ≥ 80%):

| Project | Positive sample | Precision |
|---------|-----------------|-----------|
| BossRoom | 50 | 90% |
| HelloMarioFramework | 2 | 100% (too few fix commits in history) |
| UniTask | 50 | 96% |
| VContainer | 50 | 96% |
| Unilyze (self) | 0 | n/a (no classified fix commits) |

HelloMarioFramework has only 2 fix-tagged commits in its 23-commit history; Unilyze self-analysis worktree lacks fix-keyword commits. Four of five projects yield usable signal; pooled n = 994 types with non-zero bug-fix density.

### Spearman ρ: metric vs bugfixDensityPerKLoc (pooled, n = 994)

| Metric | ρ | p |
|--------|---|---|
| **CodeHealth** (composite) | **+0.547** | 1.0×10⁻⁷⁸ |
| lineCount (baseline) | −0.910 | ≈ 0 |
| avgCogCC (baseline) | −0.516 | 7.5×10⁻⁶⁹ |

Per-project CodeHealth ρ: BossRoom 0.556, UniTask 0.569, VContainer 0.500 (HelloMarioFramework n = 3, inconclusive).

### Interpretation

- Absolute ρ values are modest-to-moderate, consistent with Majumder et al. (EMSE 2022) weak product-metric ↔ defect links.
- **Relative comparison**: CodeHealth composite (ρ = +0.547) does **not** outperform the lineCount baseline on magnitude (|ρ| = 0.910). The sign flip vs lineCount reflects CodeHealth's penalization of size/complexity in the score construction; per-KLoC normalization also mechanically couples density to lineCount.
- Positive CodeHealth ↔ density correlation is partly confounded by small, churn-heavy types (high density denominator effect). Treat as structural evidence for v2 redesign, not proof of predictive power.
- No SonarQube ground truth was used anywhere in this phase.

Full tables: `phase6-bugfix-validity-results.md`, `data/phase6-bugfix-validity.json`.

## Threats to validity

1. Bug-fix keyword heuristic misses reworded fixes and mis-tags chore commits (precision ~90–96% on sampled repos).
2. `bugfixDensityPerKLoc` normalizes by type line count, which is also a CodeHealth input — circularity risk.
3. Library repos (UniTask, VContainer) dominate pooled n; game samples (BossRoom, HMF) are smaller.
4. Shallow-then-unshallow clone workflow required for git history; remeasure uses HEAD, not historical snapshots.

## Positioning

This phase records **evidence for Phase 3 CodeHealth v2 weight reallocation** — not a metricsVersion bump and not a claim that current weights are validated. Primary findings:

1. CogCC and LOC are collinear at method (ρ ≈ 0.78) and type (ρ ≈ 0.64) levels.
2. CodeHealth inputs overlap on multiple axes (CogCC pair, CogCC×nesting, line×methods).
3. Composite CodeHealth correlates with external bug-fix density, but single-input lineCount shows stronger rank association; composite advantage is **not** demonstrated.

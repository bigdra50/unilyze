# Phase 6: bug-fix density external validity

SZZ-lite: classify bug-fix commits from git history (no SonarQube ground truth), map touched `.cs` files to types, and correlate per-type CodeHealth with bug-fix density.

## Heuristic

Positive if subject matches conventional fix/bugfix/hotfix/issue-reference patterns; negative overrides for typo/lint/format/docs/test-only fixes.


## Heuristic validation (approx. 50 positive + 50 negative samples per repo)

| Project | Pos sample | Precision | Neg accuracy | Overall |
|---------|------------|-----------|--------------|---------|
| BossRoom | 50 | 90.0% | nan% | 90.0% |
| HelloMarioFramework | 2 | 100.0% | nan% | 100.0% |
| UniTask | 50 | 96.0% | nan% | 96.0% |
| VContainer | 50 | 96.0% | nan% | 96.0% |
| Unilyze (self) | 0 | nan% | nan% | nan% |

## Spearman rho: CodeHealth vs bug-fix density (non-zero density types)

| Project | Density | n | CodeHealth rho (p) | lineCount rho (p) | avgCogCC rho (p) |
|---------|---------|---|--------------------|-------------------|------------------|
| BossRoom | bugfixDensityPerKLoc | 181 | 0.556 (4.32e-16) | -0.961 (2.95e-102) | -0.705 (1.47e-28) |
| HelloMarioFramework | bugfixDensityPerKLoc | 3 | 0.866 (3.33e-01) | -1.000 (0.00e+00) | -0.500 (6.67e-01) |
| UniTask | bugfixDensityPerKLoc | 635 | 0.569 (9.66e-56) | -0.906 (5.84e-239) | -0.453 (2.17e-33) |
| VContainer | bugfixDensityPerKLoc | 175 | 0.500 (1.96e-12) | -0.861 (9.91e-53) | -0.555 (1.54e-15) |
| Unilyze (self) | bugfixDensityPerKLoc | 0 | nan (nan) | nan (nan) | nan (nan) |
| Pooled | bugfixDensityPerKLoc | 994 | 0.547 (1.04e-78) | -0.910 (0.00e+00) | -0.516 (7.48e-69) |

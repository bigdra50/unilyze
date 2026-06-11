# Phase 6: CodeHealth input collinearity

Per-method and per-type Pearson/Spearman correlations for CogCC x line count, plus the 6x6 matrix of raw `CalculateHealthScore` inputs at type level. Tables show Spearman rho / Pearson r.


## Legacy corpus (v0.1.x snapshots)

### CogCC x line count

| Project | Scope | Result |
|---------|-------|--------|
| Pooled | method | n=7368, Pearson r=0.904 (p=0.00e+00), Spearman rho=0.816 (p=0.00e+00) |
| Pooled | type avgCogCC x lineCount | n=1579, Pearson r=0.375 (p=6.82e-54), Spearman rho=0.549 (p=4.33e-125) |
| Pooled | type maxCogCC x lineCount | n=1579, Pearson r=0.471 (p=3.44e-88), Spearman rho=0.585 (p=1.18e-145) |
| BossRoom | method | n=1183, Pearson r=0.823 (p=7.28e-293), Spearman rho=0.810 (p=1.05e-275) |
| BossRoom | type avgCogCC x lineCount | n=228, Pearson r=0.508 (p=2.33e-16), Spearman rho=0.721 (p=7.85e-38) |
| HelloMarioFramework | method | n=418, Pearson r=0.929 (p=9.48e-182), Spearman rho=0.756 (p=1.52e-78) |
| HelloMarioFramework | type avgCogCC x lineCount | n=109, Pearson r=0.431 (p=2.96e-06), Spearman rho=0.635 (p=1.17e-13) |
| UniTask | method | n=4958, Pearson r=0.955 (p=0.00e+00), Spearman rho=0.857 (p=0.00e+00) |
| UniTask | type avgCogCC x lineCount | n=942, Pearson r=0.397 (p=6.78e-37), Spearman rho=0.450 (p=3.95e-48) |
| VContainer | method | n=575, Pearson r=0.576 (p=4.46e-52), Spearman rho=0.446 (p=1.92e-29) |
| VContainer | type avgCogCC x lineCount | n=234, Pearson r=0.255 (p=8.05e-05), Spearman rho=0.653 (p=8.07e-30) |
| Unilyze (self) | method | n=234, Pearson r=0.686 (p=6.07e-34), Spearman rho=0.720 (p=1.22e-38) |
| Unilyze (self) | type avgCogCC x lineCount | n=66, Pearson r=0.139 (p=2.64e-01), Spearman rho=0.785 (p=5.96e-15) |

### 6x6 input correlation matrix (type level, pooled)

| Input | avgCogCC | maxCogCC | lineCount | methodCount | maxNesting | excessiveParams |
|-------|----------|----------|-----------|-------------|------------|-----------------|
| avgCogCC | 1.00 / 1.00 | 0.98 / 0.84 | 0.55 / 0.37 | 0.58 / 0.06 | 0.95 / 0.72 | 0.14 / -0.00 |
| maxCogCC | 0.98 / 0.84 | 1.00 / 1.00 | 0.59 / 0.47 | 0.65 / 0.11 | 0.97 / 0.70 | 0.16 / 0.01 |
| lineCount | 0.55 / 0.37 | 0.59 / 0.47 | 1.00 / 1.00 | 0.64 / 0.14 | 0.57 / 0.24 | 0.17 / 0.06 |
| methodCount | 0.58 / 0.06 | 0.65 / 0.11 | 0.64 / 0.14 | 1.00 / 1.00 | 0.63 / 0.13 | 0.22 / 0.86 |
| maxNesting | 0.95 / 0.72 | 0.97 / 0.70 | 0.57 / 0.24 | 0.63 / 0.13 | 1.00 / 1.00 | 0.14 / 0.04 |
| excessiveParams | 0.14 / -0.00 | 0.16 / 0.01 | 0.17 / 0.06 | 0.22 / 0.86 | 0.14 / 0.04 | 1.00 / 1.00 |

## Re-measured corpus (current tool, SyntaxOnly)

### CogCC x line count

| Project | Scope | Result |
|---------|-------|--------|
| Pooled | method | n=6157, Pearson r=0.859 (p=0.00e+00), Spearman rho=0.775 (p=0.00e+00) |
| Pooled | type avgCogCC x lineCount | n=1456, Pearson r=0.284 (p=2.46e-28), Spearman rho=0.644 (p=3.23e-171) |
| Pooled | type maxCogCC x lineCount | n=1456, Pearson r=0.354 (p=2.35e-44), Spearman rho=0.690 (p=2.75e-206) |
| BossRoom | method | n=1161, Pearson r=0.823 (p=5.97e-287), Spearman rho=0.807 (p=2.28e-267) |
| BossRoom | type avgCogCC x lineCount | n=226, Pearson r=0.537 (p=2.92e-18), Spearman rho=0.726 (p=2.64e-38) |
| HelloMarioFramework | method | n=418, Pearson r=0.929 (p=9.48e-182), Spearman rho=0.756 (p=1.52e-78) |
| HelloMarioFramework | type avgCogCC x lineCount | n=109, Pearson r=0.431 (p=2.96e-06), Spearman rho=0.635 (p=1.17e-13) |
| UniTask | method | n=3281, Pearson r=0.943 (p=0.00e+00), Spearman rho=0.811 (p=0.00e+00) |
| UniTask | type avgCogCC x lineCount | n=685, Pearson r=0.277 (p=1.70e-13), Spearman rho=0.534 (p=1.04e-51) |
| VContainer | method | n=575, Pearson r=0.576 (p=4.51e-52), Spearman rho=0.446 (p=1.85e-29) |
| VContainer | type avgCogCC x lineCount | n=234, Pearson r=0.253 (p=9.20e-05), Spearman rho=0.653 (p=7.92e-30) |
| Unilyze (self) | method | n=722, Pearson r=0.645 (p=4.05e-86), Spearman rho=0.660 (p=1.32e-91) |
| Unilyze (self) | type avgCogCC x lineCount | n=202, Pearson r=0.557 (p=7.40e-18), Spearman rho=0.829 (p=1.98e-52) |

### 6x6 input correlation matrix (type level, pooled)

| Input | avgCogCC | maxCogCC | lineCount | methodCount | maxNesting | excessiveParams |
|-------|----------|----------|-----------|-------------|------------|-----------------|
| avgCogCC | 1.00 / 1.00 | 0.98 / 0.82 | 0.64 / 0.28 | 0.58 / 0.08 | 0.94 / 0.73 | 0.19 / 0.02 |
| maxCogCC | 0.98 / 0.82 | 1.00 / 1.00 | 0.69 / 0.35 | 0.66 / 0.15 | 0.96 / 0.69 | 0.20 / 0.02 |
| lineCount | 0.64 / 0.28 | 0.69 / 0.35 | 1.00 / 1.00 | 0.75 / 0.71 | 0.68 / 0.24 | 0.23 / 0.58 |
| methodCount | 0.58 / 0.08 | 0.66 / 0.15 | 0.75 / 0.71 | 1.00 / 1.00 | 0.65 / 0.17 | 0.24 / 0.85 |
| maxNesting | 0.94 / 0.73 | 0.96 / 0.69 | 0.68 / 0.24 | 0.65 / 0.17 | 1.00 / 1.00 | 0.19 / 0.04 |
| excessiveParams | 0.19 / 0.02 | 0.20 / 0.02 | 0.23 / 0.58 | 0.24 / 0.85 | 0.19 / 0.04 | 1.00 / 1.00 |

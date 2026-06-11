#!/usr/bin/env python3
"""Shared helpers for Phase 6 CodeHealth validity analysis."""

from __future__ import annotations

import json
import math
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

try:
    from scipy import stats as scipy_stats
except ImportError:  # pragma: no cover
    scipy_stats = None


DATA_DIR = Path(__file__).parent.parent / "data"
REPO_ROOT = Path(__file__).resolve().parents[3]
TMP_REPO_ROOT = Path("/tmp/cross-validation-repos")

CORPUS_PROJECTS = [
    {
        "key": "bossroom",
        "label": "BossRoom",
        "repo_url": "https://github.com/Unity-Technologies/com.unity.multiplayer.samples.coop",
        "clone_dir": TMP_REPO_ROOT / "bossroom",
        "project_path": TMP_REPO_ROOT / "bossroom",
        "legacy_json": "unilyze-bossroom.json",
        "mv2_json": "unilyze-bossroom-mv2.json",
    },
    {
        "key": "hmf",
        "label": "HelloMarioFramework",
        "repo_url": "https://github.com/HelloFangaming/HelloMarioFramework",
        "clone_dir": TMP_REPO_ROOT / "HelloMarioFramework",
        "project_path": TMP_REPO_ROOT / "HelloMarioFramework",
        "legacy_json": "unilyze-hmf.json",
        "mv2_json": "unilyze-hmf-mv2.json",
    },
    {
        "key": "unitask",
        "label": "UniTask",
        "repo_url": "https://github.com/Cysharp/UniTask",
        "clone_dir": TMP_REPO_ROOT / "UniTask",
        "project_path": TMP_REPO_ROOT / "UniTask" / "src" / "UniTask",
        "legacy_json": "unilyze-unitask.json",
        "mv2_json": "unilyze-unitask-mv2.json",
    },
    {
        "key": "vcontainer",
        "label": "VContainer",
        "repo_url": "https://github.com/hadashiA/VContainer",
        "clone_dir": TMP_REPO_ROOT / "VContainer",
        "project_path": TMP_REPO_ROOT / "VContainer" / "VContainer",
        "legacy_json": "unilyze-vcontainer.json",
        "mv2_json": "unilyze-vcontainer-mv2.json",
    },
    {
        "key": "self",
        "label": "Unilyze (self)",
        "repo_url": None,
        "clone_dir": None,
        "project_path": REPO_ROOT / "src" / "Unilyze",
        "legacy_json": "unilyze-self.json",
        "mv2_json": "unilyze-self-mv2.json",
    },
]


@dataclass(frozen=True)
class CorrelationResult:
    n: int
    pearson_r: float
    pearson_p: float
    spearman_rho: float
    spearman_p: float


def load_unilyze_json(path: Path) -> dict:
    with path.open(encoding="utf-8") as f:
        return json.load(f)


def iter_type_metrics(data: dict) -> Iterable[dict]:
    for tm in data.get("typeMetrics", []):
        yield tm


def iter_method_rows(data: dict) -> Iterable[dict]:
    for tm in data.get("typeMetrics", []):
        type_name = tm.get("typeName", "")
        for method in tm.get("methods", []):
            yield {
                "typeName": type_name,
                "methodName": method.get("methodName", ""),
                "lineCount": method.get("lineCount", 0),
                "cognitiveComplexity": method.get("cognitiveComplexity", 0),
            }


def type_health_inputs(tm: dict) -> dict[str, float]:
    return {
        "avgCogCC": float(tm.get("averageCognitiveComplexity", 0)),
        "maxCogCC": float(tm.get("maxCognitiveComplexity", 0)),
        "lineCount": float(tm.get("lineCount", 0)),
        "methodCount": float(tm.get("methodCount", 0)),
        "maxNesting": float(tm.get("maxNestingDepth", 0)),
        "excessiveParams": float(tm.get("excessiveParameterMethodCount", 0)),
        "codeHealth": float(tm.get("codeHealth", 0)),
    }


def pearson_r(x: list[float], y: list[float]) -> float:
    n = len(x)
    if n < 2:
        return float("nan")
    mx, my = sum(x) / n, sum(y) / n
    num = sum((a - mx) * (b - my) for a, b in zip(x, y))
    dx = math.sqrt(sum((a - mx) ** 2 for a in x))
    dy = math.sqrt(sum((b - my) ** 2 for b in y))
    denom = dx * dy
    return num / denom if denom > 0 else float("nan")


def spearman_rho(x: list[float], y: list[float]) -> float:
    n = len(x)
    if n < 2:
        return float("nan")

    def ranks(vals: list[float]) -> list[float]:
        indexed = sorted(enumerate(vals), key=lambda t: t[1])
        result = [0.0] * n
        i = 0
        while i < n:
            j = i
            while j < n and indexed[j][1] == indexed[i][1]:
                j += 1
            avg_rank = (i + j - 1) / 2.0 + 1
            for k in range(i, j):
                result[indexed[k][0]] = avg_rank
            i = j
        return result

    rx, ry = ranks(x), ranks(y)
    return pearson_r(rx, ry)


def correlation_with_pvalues(x: list[float], y: list[float]) -> CorrelationResult:
    n = len(x)
    if n < 3:
        return CorrelationResult(n, float("nan"), float("nan"), float("nan"), float("nan"))

    if scipy_stats is not None:
        pr, pp = scipy_stats.pearsonr(x, y)
        sr, sp = scipy_stats.spearmanr(x, y)
        return CorrelationResult(n, float(pr), float(pp), float(sr), float(sp))

    r = pearson_r(x, y)
    rho = spearman_rho(x, y)
    return CorrelationResult(
        n,
        r,
        _approx_pearson_p(r, n),
        rho,
        _approx_spearman_p(rho, n),
    )


def _approx_pearson_p(r: float, n: int) -> float:
    if n < 3 or math.isnan(r) or abs(r) >= 1:
        return float("nan")
    t = r * math.sqrt((n - 2) / max(1e-12, 1 - r * r))
    return _two_tailed_t_p(t, n - 2)


def _approx_spearman_p(rho: float, n: int) -> float:
    if n < 3 or math.isnan(rho) or abs(rho) >= 1:
        return float("nan")
    t = rho * math.sqrt((n - 2) / max(1e-12, 1 - rho * rho))
    return _two_tailed_t_p(t, n - 2)


def _two_tailed_t_p(t: float, df: int) -> float:
    x = df / (df + t * t)
    return _regularized_incomplete_beta(x, df / 2, 0.5)


def _regularized_incomplete_beta(x: float, a: float, b: float) -> float:
    if x <= 0:
        return 0.0
    if x >= 1:
        return 1.0
    ln_beta = math.lgamma(a) + math.lgamma(b) - math.lgamma(a + b)
    front = math.exp(math.log(x) * a + math.log(1 - x) * b - ln_beta) / a
    fp = 1.0
    c = 1.0
    d = 0.0
    for m in range(1, 201):
        if m % 2 == 0:
            numerator = m / 2 * (b - m / 2) * x
            denominator = (a + m - 1)
        else:
            numerator = -(a + m - 1) * (a + b + m - 1) * x / (a + m - 1)
            denominator = a + m
        d = 1 + numerator / denominator * d
        if abs(d) < 1e-30:
            d = 1e-30
        c = 1 + numerator / denominator / c
        if abs(c) < 1e-30:
            c = 1e-30
        d = 1 / d
        delta = c * d
        fp *= delta
        if abs(delta - 1) < 1e-10:
            break
    return front * (fp - 1)


def normalize_path(path: str) -> str:
    return path.replace("\\", "/").rstrip("/")


def resolve_change_count(
    file_path: str | None,
    project_path: str,
    change_by_rel_path: dict[str, int],
) -> int:
    if not file_path:
        return 0

    normalized_project = normalize_path(str(Path(project_path).resolve()))
    normalized_absolute = normalize_path(str(Path(file_path).resolve()))
    prefix = normalized_project + "/"
    if normalized_absolute.lower().startswith(prefix.lower()):
        relative = normalized_absolute[len(prefix) :]
        if relative in change_by_rel_path:
            return change_by_rel_path[relative]

    file_name = Path(file_path).name.lower()
    for rel_path, count in change_by_rel_path.items():
        if Path(rel_path).name.lower() == file_name:
            return count
    return 0

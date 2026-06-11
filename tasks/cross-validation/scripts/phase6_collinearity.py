#!/usr/bin/env python3
"""Phase 6: CogCC x LOC collinearity and CodeHealth input correlation matrix."""

from __future__ import annotations

import json
import sys
from pathlib import Path

from phase6_common import (
    CORPUS_PROJECTS,
    DATA_DIR,
    CorrelationResult,
    correlation_with_pvalues,
    iter_method_rows,
    iter_type_metrics,
    load_unilyze_json,
    type_health_inputs,
)

INPUT_COLUMNS = [
    "avgCogCC",
    "maxCogCC",
    "lineCount",
    "methodCount",
    "maxNesting",
    "excessiveParams",
]


def correlation_to_dict(corr: CorrelationResult) -> dict:
    return {
        "n": corr.n,
        "pearson_r": corr.pearson_r,
        "pearson_p": corr.pearson_p,
        "spearman_rho": corr.spearman_rho,
        "spearman_p": corr.spearman_p,
    }


def analyze_dataset(label: str, json_path: Path) -> dict:
    data = load_unilyze_json(json_path)
    types = list(iter_type_metrics(data))
    methods = list(iter_method_rows(data))

    method_lines = [float(m["lineCount"]) for m in methods]
    method_cog = [float(m["cognitiveComplexity"]) for m in methods]
    method_corr = correlation_with_pvalues(method_lines, method_cog)

    type_rows = [type_health_inputs(tm) for tm in types]
    type_line = [r["lineCount"] for r in type_rows]
    type_avg = [r["avgCogCC"] for r in type_rows]
    type_max = [r["maxCogCC"] for r in type_rows]

    avg_vs_line = correlation_with_pvalues(type_line, type_avg)
    max_vs_line = correlation_with_pvalues(type_line, type_max)

    matrix = {}
    for left in INPUT_COLUMNS:
        matrix[left] = {}
        xs = [r[left] for r in type_rows]
        for right in INPUT_COLUMNS:
            ys = [r[right] for r in type_rows]
            corr = correlation_with_pvalues(xs, ys)
            matrix[left][right] = {
                "pearson_r": corr.pearson_r,
                "spearman_rho": corr.spearman_rho,
            }

    return {
        "label": label,
        "json_path": str(json_path),
        "metrics_version": data.get("metricsVersion"),
        "analysis_level": data.get("analysisLevel"),
        "type_count": len(types),
        "method_count": len(methods),
        "method_cogcc_x_line": correlation_to_dict(method_corr),
        "type_avg_cogcc_x_line": correlation_to_dict(avg_vs_line),
        "type_max_cogcc_x_line": correlation_to_dict(max_vs_line),
        "input_matrix": matrix,
    }


def format_corr(corr) -> str:
    if isinstance(corr, dict):
        return (
            f"n={corr['n']}, Pearson r={corr['pearson_r']:.3f} (p={corr['pearson_p']:.2e}), "
            f"Spearman rho={corr['spearman_rho']:.3f} (p={corr['spearman_p']:.2e})"
        )
    return (
        f"n={corr.n}, Pearson r={corr.pearson_r:.3f} (p={corr.pearson_p:.2e}), "
        f"Spearman rho={corr.spearman_rho:.3f} (p={corr.spearman_p:.2e})"
    )


def format_matrix_table(matrix: dict) -> str:
    lines = [
        "| Input | avgCogCC | maxCogCC | lineCount | methodCount | maxNesting | excessiveParams |",
        "|-------|----------|----------|-----------|-------------|------------|-----------------|",
    ]
    for left in INPUT_COLUMNS:
        cells = [left]
        for right in INPUT_COLUMNS:
            cell = matrix[left][right]
            cells.append(f"{cell['spearman_rho']:.2f} / {cell['pearson_r']:.2f}")
        lines.append("| " + " | ".join(cells) + " |")
    return "\n".join(lines)


def pool_type_rows(results: list[dict], json_paths: list[Path]) -> list[dict]:
    rows: list[dict] = []
    for path in json_paths:
        for tm in iter_type_metrics(load_unilyze_json(path)):
            rows.append(type_health_inputs(tm))
    return rows


def analyze_pooled(label: str, json_paths: list[Path]) -> dict:
    rows = pool_type_rows([], json_paths)
    type_line = [r["lineCount"] for r in rows]
    type_avg = [r["avgCogCC"] for r in rows]
    type_max = [r["maxCogCC"] for r in rows]

    matrix = {}
    for left in INPUT_COLUMNS:
        matrix[left] = {}
        xs = [r[left] for r in rows]
        for right in INPUT_COLUMNS:
            ys = [r[right] for r in rows]
            corr = correlation_with_pvalues(xs, ys)
            matrix[left][right] = {
                "pearson_r": corr.pearson_r,
                "spearman_rho": corr.spearman_rho,
            }

    methods_lines: list[float] = []
    methods_cog: list[float] = []
    for path in json_paths:
        for method in iter_method_rows(load_unilyze_json(path)):
            methods_lines.append(float(method["lineCount"]))
            methods_cog.append(float(method["cognitiveComplexity"]))

    return {
        "label": label,
        "type_count": len(rows),
        "method_count": len(methods_lines),
        "method_cogcc_x_line": correlation_to_dict(
            correlation_with_pvalues(methods_lines, methods_cog)
        ),
        "type_avg_cogcc_x_line": correlation_to_dict(
            correlation_with_pvalues(type_line, type_avg)
        ),
        "type_max_cogcc_x_line": correlation_to_dict(
            correlation_with_pvalues(type_line, type_max)
        ),
        "input_matrix": matrix,
    }


def render_markdown(legacy: list[dict], mv2: list[dict], legacy_pooled: dict, mv2_pooled: dict) -> str:
    lines = [
        "# Phase 6: CodeHealth input collinearity\n",
        "Per-method and per-type Pearson/Spearman correlations for CogCC x line count, "
        "plus the 6x6 matrix of raw `CalculateHealthScore` inputs at type level. "
        "Tables show Spearman rho / Pearson r.\n",
    ]

    for title, per_project, pooled in [
        ("Legacy corpus (v0.1.x snapshots)", legacy, legacy_pooled),
        ("Re-measured corpus (current tool, SyntaxOnly)", mv2, mv2_pooled),
    ]:
        lines.append(f"\n## {title}\n")
        lines.append("### CogCC x line count\n")
        lines.append("| Project | Scope | Result |")
        lines.append("|---------|-------|--------|")
        lines.append(
            f"| Pooled | method | {format_corr(pooled['method_cogcc_x_line'])} |"
        )
        lines.append(
            f"| Pooled | type avgCogCC x lineCount | {format_corr(pooled['type_avg_cogcc_x_line'])} |"
        )
        lines.append(
            f"| Pooled | type maxCogCC x lineCount | {format_corr(pooled['type_max_cogcc_x_line'])} |"
        )
        for result in per_project:
            lines.append(
                f"| {result['label']} | method | {format_corr(result['method_cogcc_x_line'])} |"
            )
            lines.append(
                f"| {result['label']} | type avgCogCC x lineCount | {format_corr(result['type_avg_cogcc_x_line'])} |"
            )

        lines.append("\n### 6x6 input correlation matrix (type level, pooled)\n")
        lines.append(format_matrix_table(pooled["input_matrix"]))

    return "\n".join(lines) + "\n"


def main() -> int:
    legacy_results: list[dict] = []
    mv2_results: list[dict] = []
    legacy_paths: list[Path] = []
    mv2_paths: list[Path] = []

    for project in CORPUS_PROJECTS:
        legacy_path = DATA_DIR / project["legacy_json"]
        mv2_path = DATA_DIR / project["mv2_json"]
        if legacy_path.exists():
            legacy_results.append(analyze_dataset(project["label"], legacy_path))
            legacy_paths.append(legacy_path)
        else:
            print(f"warning: missing legacy JSON {legacy_path}", file=sys.stderr)

        if mv2_path.exists():
            mv2_results.append(analyze_dataset(project["label"], mv2_path))
            mv2_paths.append(mv2_path)
        else:
            print(f"warning: missing mv2 JSON {mv2_path}", file=sys.stderr)

    legacy_pooled = analyze_pooled("legacy pooled", legacy_paths) if legacy_paths else {}
    mv2_pooled = analyze_pooled("mv2 pooled", mv2_paths) if mv2_paths else {}

    report = render_markdown(legacy_results, mv2_results, legacy_pooled, mv2_pooled)
    report_path = Path(__file__).parent.parent / "phase6-collinearity-results.md"
    report_path.write_text(report, encoding="utf-8")

    payload = {
        "legacy": legacy_results,
        "mv2": mv2_results,
        "legacy_pooled": legacy_pooled,
        "mv2_pooled": mv2_pooled,
    }
    json_path = DATA_DIR / "phase6-collinearity.json"
    json_path.write_text(json.dumps(payload, indent=2), encoding="utf-8")

    print(report)
    print(f"\nWrote {report_path}")
    print(f"Wrote {json_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

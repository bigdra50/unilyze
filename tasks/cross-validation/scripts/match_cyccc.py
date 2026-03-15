#!/usr/bin/env python3
"""Match Unilyze and lizard CycCC results and compute statistics."""

import csv
import json
import math
import sys
from pathlib import Path


def load_unilyze(json_path: str) -> dict[str, dict]:
    """Load Unilyze JSON, return {TypeName.MethodName:paramCount -> {cycCC, cogCC, ...}}."""
    with open(json_path) as f:
        data = json.load(f)

    methods = {}
    for t in data.get("typeMetrics", []):
        type_name = t["typeName"]
        for m in t.get("methods", []):
            key = f"{type_name}.{m['methodName']}:{m['parameterCount']}"
            methods[key] = {
                "cyclomaticComplexity": m["cyclomaticComplexity"],
                "cognitiveComplexity": m.get("cognitiveComplexity", 0),
                "lineCount": m.get("lineCount", 0),
                "typeName": type_name,
                "methodName": m["methodName"],
            }
    return methods


def load_lizard(csv_path: str) -> dict[str, dict]:
    """Load lizard CSV, return {TypeName.MethodName:paramCount -> {cycCC, ...}}."""
    methods = {}
    with open(csv_path) as f:
        reader = csv.reader(f)
        for row in reader:
            if len(row) < 11:
                continue
            try:
                # lizard CSV: NLOC, CCN, token, PARAM, length, ...
                cyc_cc = int(row[1])
                param_count = int(row[3])
            except ValueError:
                continue

            # row[7] = "ClassName::MethodName"
            class_method = row[7]
            if "::" not in class_method:
                continue

            parts = class_method.split("::")
            class_name = parts[0]
            method_name = parts[1] if len(parts) > 1 else ""

            key = f"{class_name}.{method_name}:{param_count}"
            methods[key] = {
                "cyclomaticComplexity": cyc_cc,
                "nloc": int(row[1]) if row[1].isdigit() else 0,
                "className": class_name,
                "methodName": method_name,
            }
    return methods


def spearman_rho(x: list[float], y: list[float]) -> float:
    """Calculate Spearman rank correlation coefficient."""
    n = len(x)
    if n < 2:
        return 1.0

    def ranks(vals):
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
    mx, my = sum(rx) / n, sum(ry) / n
    num = sum((a - mx) * (b - my) for a, b in zip(rx, ry))
    dx = math.sqrt(sum((a - mx) ** 2 for a in rx))
    dy = math.sqrt(sum((b - my) ** 2 for b in ry))
    denom = dx * dy
    return num / denom if denom > 0 else 1.0


def pearson_r(x: list[float], y: list[float]) -> float:
    """Calculate Pearson correlation coefficient."""
    n = len(x)
    if n < 2:
        return 1.0
    mx, my = sum(x) / n, sum(y) / n
    num = sum((a - mx) * (b - my) for a, b in zip(x, y))
    dx = math.sqrt(sum((a - mx) ** 2 for a in x))
    dy = math.sqrt(sum((b - my) ** 2 for b in y))
    denom = dx * dy
    return num / denom if denom > 0 else 1.0


def analyze(unilyze_path: str, lizard_path: str, name: str) -> dict:
    """Match and compare CycCC for a single project."""
    uni = load_unilyze(unilyze_path)
    liz = load_lizard(lizard_path)

    matched = []
    unmatched_uni = []
    unmatched_liz = []

    common_keys = set(uni.keys()) & set(liz.keys())
    for key in sorted(common_keys):
        matched.append({
            "key": key,
            "unilyze": uni[key]["cyclomaticComplexity"],
            "lizard": liz[key]["cyclomaticComplexity"],
            "delta": uni[key]["cyclomaticComplexity"] - liz[key]["cyclomaticComplexity"],
        })

    for key in sorted(set(uni.keys()) - set(liz.keys())):
        unmatched_uni.append(key)
    for key in sorted(set(liz.keys()) - set(uni.keys())):
        unmatched_liz.append(key)

    if not matched:
        return {"name": name, "error": "No matched methods", "uni_count": len(uni), "liz_count": len(liz)}

    uni_vals = [float(m["unilyze"]) for m in matched]
    liz_vals = [float(m["lizard"]) for m in matched]
    deltas = [m["delta"] for m in matched]

    exact = sum(1 for d in deltas if d == 0)
    within1 = sum(1 for d in deltas if abs(d) <= 1)
    mae = sum(abs(d) for d in deltas) / len(deltas)
    max_delta = max(abs(d) for d in deltas)

    # Non-trivial methods only for correlation
    nontrivial = [(u, l) for u, l in zip(uni_vals, liz_vals) if u > 1 or l > 1]
    if len(nontrivial) >= 2:
        nt_u, nt_l = zip(*nontrivial)
        rho = spearman_rho(list(nt_u), list(nt_l))
        r = pearson_r(list(nt_u), list(nt_l))
    else:
        rho = 1.0
        r = 1.0

    divergences = sorted(matched, key=lambda m: -abs(m["delta"]))
    top_divergences = [d for d in divergences if d["delta"] != 0][:20]

    return {
        "name": name,
        "total_matched": len(matched),
        "unmatched_unilyze": len(unmatched_uni),
        "unmatched_lizard": len(unmatched_liz),
        "exact_match": exact,
        "exact_match_rate": exact / len(matched),
        "within1": within1,
        "within1_rate": within1 / len(matched),
        "mae": mae,
        "max_delta": max_delta,
        "spearman_rho": rho,
        "pearson_r": r,
        "nontrivial_count": len(nontrivial),
        "top_divergences": top_divergences,
        "unmatched_unilyze_sample": unmatched_uni[:10],
        "unmatched_lizard_sample": unmatched_liz[:10],
    }


def format_report(results: list[dict]) -> str:
    """Format results as markdown report."""
    lines = ["# Phase 1: CycCC - Unilyze vs lizard 比較結果\n"]

    lines.append("## 概要\n")
    lines.append("| Project | Matched | Exact% | Within1% | MAE | MaxDelta | Spearman | Pearson |")
    lines.append("|---------|---------|--------|----------|-----|----------|----------|---------|")
    for r in results:
        if "error" in r:
            lines.append(f"| {r['name']} | ERROR: {r['error']} | - | - | - | - | - | - |")
            continue
        lines.append(
            f"| {r['name']} | {r['total_matched']} | "
            f"{r['exact_match_rate']:.1%} | {r['within1_rate']:.1%} | "
            f"{r['mae']:.2f} | {r['max_delta']} | "
            f"{r['spearman_rho']:.3f} | {r['pearson_r']:.3f} |"
        )

    for r in results:
        if "error" in r:
            continue
        lines.append(f"\n## {r['name']}\n")
        lines.append(f"- Matched methods: {r['total_matched']}")
        lines.append(f"- Unmatched (Unilyze only): {r['unmatched_unilyze']}")
        lines.append(f"- Unmatched (lizard only): {r['unmatched_lizard']}")
        lines.append(f"- Non-trivial methods (CycCC > 1): {r['nontrivial_count']}")

        if r["top_divergences"]:
            lines.append(f"\n### Top divergences\n")
            lines.append("| Method | Unilyze | lizard | Delta |")
            lines.append("|--------|---------|--------|-------|")
            for d in r["top_divergences"]:
                lines.append(f"| `{d['key']}` | {d['unilyze']} | {d['lizard']} | {d['delta']:+d} |")

        if r["unmatched_unilyze_sample"]:
            lines.append(f"\n### Unmatched (Unilyze only, sample)\n")
            for k in r["unmatched_unilyze_sample"]:
                lines.append(f"- `{k}`")

        if r["unmatched_lizard_sample"]:
            lines.append(f"\n### Unmatched (lizard only, sample)\n")
            for k in r["unmatched_lizard_sample"]:
                lines.append(f"- `{k}`")

    lines.append("\n## 既知の定義差異\n")
    lines.append("| 対象 | lizard | Unilyze | 影響 |")
    lines.append("|------|--------|---------|------|")
    lines.append("| `?.` (null conditional) | 非カウント | +1 | Unilyze > lizard |")
    lines.append("| `goto` | 非カウント | +1 | Unilyze > lizard |")
    lines.append("| `#if`/`#elif` | +1 | 非カウント | lizard > Unilyze |")
    lines.append("| `switch expression arm` | 非カウント | +1 | Unilyze > lizard |")
    lines.append("| `bool &`/`bool \\|` | 非カウント | +1 (SemanticModel) | Unilyze > lizard |")

    return "\n".join(lines) + "\n"


def main():
    data_dir = Path(__file__).parent.parent / "data"

    projects = [
        ("HelloMarioFramework", "unilyze-hmf.json", "lizard-hmf.csv"),
        ("BossRoom", "unilyze-bossroom.json", "lizard-bossroom.csv"),
        ("UniTask", "unilyze-unitask.json", "lizard-unitask.csv"),
        ("VContainer", "unilyze-vcontainer.json", "lizard-vcontainer.csv"),
    ]

    results = []
    for name, uni_file, liz_file in projects:
        uni_path = data_dir / uni_file
        liz_path = data_dir / liz_file
        if not uni_path.exists() or not liz_path.exists():
            results.append({"name": name, "error": f"Missing data files"})
            continue
        result = analyze(str(uni_path), str(liz_path), name)
        results.append(result)

    report = format_report(results)

    # Write report
    report_path = Path(__file__).parent.parent / "phase1-cyccc-lizard.md"
    report_path.write_text(report)
    print(report)

    # Write matched CSV for each project
    for name, uni_file, liz_file in projects:
        uni_path = data_dir / uni_file
        liz_path = data_dir / liz_file
        if not uni_path.exists() or not liz_path.exists():
            continue
        uni = load_unilyze(str(uni_path))
        liz = load_lizard(str(liz_path))
        common = set(uni.keys()) & set(liz.keys())
        csv_path = data_dir / f"matched-cyccc-{name.lower()}.csv"
        with open(csv_path, "w", newline="") as f:
            w = csv.writer(f)
            w.writerow(["key", "unilyze_cyccc", "lizard_cyccc", "delta"])
            for key in sorted(common):
                u = uni[key]["cyclomaticComplexity"]
                l = liz[key]["cyclomaticComplexity"]
                w.writerow([key, u, l, u - l])


if __name__ == "__main__":
    main()

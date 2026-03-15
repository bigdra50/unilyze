#!/usr/bin/env python3
"""Phase 2: Cross-validate Unilyze CogCC against SonarAnalyzer S3776 for Unity projects.

Uses a programmatic Roslyn-based SonarRunner tool (built in /tmp/sonar_runner)
to run S3776 analysis without requiring full compilation.
"""

import csv
import json
import math
import subprocess
import sys
from dataclasses import dataclass, field
from pathlib import Path


# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------
SONAR_VERSION = "10.20.0.135146"
SONAR_RUNNER_DIR = Path("/tmp/sonar_runner")


# ---------------------------------------------------------------------------
# Data classes
# ---------------------------------------------------------------------------
@dataclass
class MatchedMethod:
    key: str  # TypeName.MethodName or TypeName.MethodName:paramCount
    type_name: str
    method_name: str
    unilyze_cogcc: int
    sonar_cogcc: int

    @property
    def delta(self) -> int:
        return self.unilyze_cogcc - self.sonar_cogcc


@dataclass
class ProjectResult:
    name: str
    matched: list[MatchedMethod] = field(default_factory=list)
    sonar_only: list[str] = field(default_factory=list)
    error: str = ""
    sonar_raw_count: int = 0


# ---------------------------------------------------------------------------
# SonarRunner invocation
# ---------------------------------------------------------------------------
def run_sonar_runner(source_dir: str, output_json: Path) -> list[dict]:
    """Run the SonarRunner tool on a source directory.

    Returns list of {typeName, methodName, score, file, line}.
    """
    print(f"  [SonarRunner] {source_dir}...", flush=True)
    res = subprocess.run(
        ["dotnet", "run", "--no-build", "--", source_dir, str(output_json)],
        cwd=SONAR_RUNNER_DIR,
        capture_output=True, text=True, timeout=300,
    )
    if res.returncode != 0:
        print(f"  ERROR: SonarRunner failed (exit {res.returncode})", file=sys.stderr)
        print(res.stderr[:1000], file=sys.stderr)
        return []

    for line in res.stderr.strip().split("\n"):
        if line.strip():
            print(f"    {line}")

    if not output_json.exists():
        return []

    with open(output_json) as f:
        return json.load(f)


# ---------------------------------------------------------------------------
# Unilyze JSON loader
# ---------------------------------------------------------------------------
@dataclass
class UnilyzeMethod:
    type_name: str
    method_name: str
    cogcc: int
    param_count: int
    start_line: int
    key: str  # TypeName.MethodName or TypeName.MethodName:paramCount


def load_unilyze_cogcc(json_path: str) -> list[UnilyzeMethod]:
    """Load Unilyze JSON, return list of UnilyzeMethod."""
    with open(json_path) as f:
        data = json.load(f)

    # First pass: detect overloaded method names (same TypeName.MethodName)
    name_counts: dict[str, int] = {}
    for t in data.get("typeMetrics", []):
        type_name = t["typeName"]
        for m in t.get("methods", []):
            simple_key = f"{type_name}.{m['methodName']}"
            name_counts[simple_key] = name_counts.get(simple_key, 0) + 1

    methods: list[UnilyzeMethod] = []
    for t in data.get("typeMetrics", []):
        type_name = t["typeName"]
        for m in t.get("methods", []):
            simple_key = f"{type_name}.{m['methodName']}"
            if name_counts.get(simple_key, 0) > 1:
                key = f"{type_name}.{m['methodName']}:{m['parameterCount']}"
            else:
                key = simple_key
            methods.append(UnilyzeMethod(
                type_name=type_name,
                method_name=m["methodName"],
                cogcc=m.get("cognitiveComplexity", 0),
                param_count=m.get("parameterCount", 0),
                start_line=m.get("startLine", 0),
                key=key,
            ))
    return methods


# ---------------------------------------------------------------------------
# Matching logic
# ---------------------------------------------------------------------------
def match_results(
    unilyze_methods: list[UnilyzeMethod],
    sonar_entries: list[dict],
) -> tuple[list[MatchedMethod], list[str], int]:
    """Match Unilyze CogCC with SonarAnalyzer S3776 results.

    Strategy:
    1. For non-overloaded methods: match by TypeName.MethodName directly.
    2. For overloaded methods: match by (TypeName, MethodName, line proximity).
    3. Methods not reported by SonarAnalyzer are assumed CogCC = 0.

    Returns: (matched, sonar_only_keys, sonar_total)
    """
    # Group Sonar entries by (typeName, methodName)
    sonar_by_type_method: dict[str, list[dict]] = {}
    for entry in sonar_entries:
        tn = entry.get("typeName", "")
        mn = entry.get("methodName", "")
        if tn and mn:
            key = f"{tn}.{mn}"
            sonar_by_type_method.setdefault(key, []).append(entry)

    # Group Unilyze methods by TypeName.MethodName (without paramCount)
    uni_by_type_method: dict[str, list[UnilyzeMethod]] = {}
    for um in unilyze_methods:
        base_key = f"{um.type_name}.{um.method_name}"
        uni_by_type_method.setdefault(base_key, []).append(um)

    matched: list[MatchedMethod] = []
    used_sonar_keys: set[str] = set()

    for base_key, uni_group in uni_by_type_method.items():
        sonar_group = sonar_by_type_method.get(base_key, [])

        if len(uni_group) == 1 and len(sonar_group) <= 1:
            # Simple case: unique method name
            um = uni_group[0]
            sonar_score = sonar_group[0]["score"] if sonar_group else 0
            matched.append(MatchedMethod(
                key=um.key,
                type_name=um.type_name,
                method_name=um.method_name,
                unilyze_cogcc=um.cogcc,
                sonar_cogcc=sonar_score,
            ))
            if sonar_group:
                used_sonar_keys.add(base_key)

        elif len(uni_group) > 1 and sonar_group:
            # Overloaded methods: match by line proximity
            used_sonar = set()
            for um in uni_group:
                best_sonar = None
                best_dist = float("inf")
                for i, se in enumerate(sonar_group):
                    if i in used_sonar:
                        continue
                    dist = abs(um.start_line - se.get("line", 0))
                    if dist < best_dist:
                        best_dist = dist
                        best_sonar = (i, se)

                if best_sonar is not None and best_dist <= 5:
                    idx, se = best_sonar
                    used_sonar.add(idx)
                    matched.append(MatchedMethod(
                        key=um.key,
                        type_name=um.type_name,
                        method_name=um.method_name,
                        unilyze_cogcc=um.cogcc,
                        sonar_cogcc=se["score"],
                    ))
                else:
                    # No close Sonar match -> assume 0
                    matched.append(MatchedMethod(
                        key=um.key,
                        type_name=um.type_name,
                        method_name=um.method_name,
                        unilyze_cogcc=um.cogcc,
                        sonar_cogcc=0,
                    ))
            used_sonar_keys.add(base_key)

        elif len(uni_group) > 1 and not sonar_group:
            # Overloaded in Unilyze, nothing in Sonar -> all 0
            for um in uni_group:
                matched.append(MatchedMethod(
                    key=um.key,
                    type_name=um.type_name,
                    method_name=um.method_name,
                    unilyze_cogcc=um.cogcc,
                    sonar_cogcc=0,
                ))

        else:
            # Single Unilyze, no Sonar match
            um = uni_group[0]
            matched.append(MatchedMethod(
                key=um.key,
                type_name=um.type_name,
                method_name=um.method_name,
                unilyze_cogcc=um.cogcc,
                sonar_cogcc=0,
            ))

    sonar_only = sorted(set(sonar_by_type_method.keys()) - used_sonar_keys)
    return matched, sonar_only, len(sonar_entries)


# ---------------------------------------------------------------------------
# Statistics
# ---------------------------------------------------------------------------
def spearman_rho(x: list[float], y: list[float]) -> float:
    n = len(x)
    if n < 2:
        return 1.0

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
    mx, my = sum(rx) / n, sum(ry) / n
    num = sum((a - mx) * (b - my) for a, b in zip(rx, ry))
    dx = math.sqrt(sum((a - mx) ** 2 for a in rx))
    dy = math.sqrt(sum((b - my) ** 2 for b in ry))
    denom = dx * dy
    return num / denom if denom > 0 else 1.0


def pearson_r(x: list[float], y: list[float]) -> float:
    n = len(x)
    if n < 2:
        return 1.0
    mx, my = sum(x) / n, sum(y) / n
    num = sum((a - mx) * (b - my) for a, b in zip(x, y))
    dx = math.sqrt(sum((a - mx) ** 2 for a in x))
    dy = math.sqrt(sum((b - my) ** 2 for b in y))
    denom = dx * dy
    return num / denom if denom > 0 else 1.0


def compute_stats(result: ProjectResult) -> dict:
    if result.error or not result.matched:
        return {"error": result.error or "No matched methods"}

    deltas = [m.delta for m in result.matched]
    uni_vals = [float(m.unilyze_cogcc) for m in result.matched]
    sonar_vals = [float(m.sonar_cogcc) for m in result.matched]

    total = len(result.matched)
    exact = sum(1 for d in deltas if d == 0)
    within1 = sum(1 for d in deltas if abs(d) <= 1)
    mae = sum(abs(d) for d in deltas) / total
    max_delta = max(abs(d) for d in deltas)

    # Non-trivial: at least one side > 0
    nontrivial = [(u, s) for u, s in zip(uni_vals, sonar_vals) if u > 0 or s > 0]
    if len(nontrivial) >= 2:
        nt_u, nt_s = zip(*nontrivial)
        rho = spearman_rho(list(nt_u), list(nt_s))
        r = pearson_r(list(nt_u), list(nt_s))
    else:
        rho = 1.0
        r = 1.0

    return {
        "total_matched": total,
        "exact_match": exact,
        "exact_match_rate": exact / total,
        "within1": within1,
        "within1_rate": within1 / total,
        "mae": mae,
        "max_delta": max_delta,
        "spearman_rho": rho,
        "pearson_r": r,
        "nontrivial_count": len(nontrivial),
    }


# ---------------------------------------------------------------------------
# Report
# ---------------------------------------------------------------------------
def format_report(results: list[tuple[ProjectResult, dict]]) -> str:
    lines = ["# Phase 2: CogCC - Unilyze vs SonarAnalyzer S3776\n"]

    lines.append(f"SonarAnalyzer.CSharp version: {SONAR_VERSION}\n")

    lines.append("## Summary\n")
    lines.append("| Project | Matched | Non-trivial | Exact% | Within1% | MAE | MaxDelta | Spearman | Pearson |")
    lines.append("|---------|---------|-------------|--------|----------|-----|----------|----------|---------|")
    for pr, stats in results:
        if "error" in stats:
            lines.append(f"| {pr.name} | ERROR: {stats['error']} | - | - | - | - | - | - | - |")
            continue
        lines.append(
            f"| {pr.name} | {stats['total_matched']} | {stats['nontrivial_count']} | "
            f"{stats['exact_match_rate']:.1%} | {stats['within1_rate']:.1%} | "
            f"{stats['mae']:.2f} | {stats['max_delta']} | "
            f"{stats['spearman_rho']:.3f} | {stats['pearson_r']:.3f} |"
        )

    for pr, stats in results:
        if "error" in stats:
            continue

        lines.append(f"\n## {pr.name}\n")
        lines.append(f"- Matched methods: {stats['total_matched']}")
        lines.append(f"- Non-trivial (CogCC > 0): {stats['nontrivial_count']}")
        lines.append(f"- SonarAnalyzer raw diagnostics: {pr.sonar_raw_count}")
        lines.append(f"- Sonar-only methods (unmatched): {len(pr.sonar_only)}")
        lines.append(f"- Exact match rate: {stats['exact_match_rate']:.1%} ({stats['exact_match']} / {stats['total_matched']})")
        lines.append(f"- Within +/-1: {stats['within1_rate']:.1%}")
        lines.append(f"- MAE: {stats['mae']:.2f}")
        lines.append(f"- Spearman rho: {stats['spearman_rho']:.3f}")
        lines.append(f"- Pearson r: {stats['pearson_r']:.3f}")

        # Divergences
        divergences = sorted(
            [m for m in pr.matched if m.delta != 0],
            key=lambda m: -abs(m.delta),
        )
        if divergences:
            lines.append(f"\n### Divergences ({len(divergences)} methods)\n")
            lines.append("| Method | Unilyze | Sonar | Delta |")
            lines.append("|--------|---------|-------|-------|")
            for d in divergences[:30]:
                lines.append(f"| `{d.key}` | {d.unilyze_cogcc} | {d.sonar_cogcc} | {d.delta:+d} |")
            if len(divergences) > 30:
                lines.append(f"| ... ({len(divergences) - 30} more) | | | |")

        # Sonar-only
        if pr.sonar_only:
            lines.append(f"\n### Sonar-only methods (not matched to Unilyze)\n")
            for name in pr.sonar_only[:10]:
                lines.append(f"- `{name}`")
            if len(pr.sonar_only) > 10:
                lines.append(f"- ... ({len(pr.sonar_only) - 10} more)")

    lines.append("\n## Methodology\n")
    lines.append("1. SonarAnalyzer S3776 run programmatically via Roslyn CompilationWithAnalyzers API")
    lines.append("2. No full compilation required - parsed syntax trees with minimal references")
    lines.append("3. S3776 threshold=0 via SonarLint.xml AdditionalText, severity via SpecificDiagnosticOptions")
    lines.append("4. Unity preprocessor symbols (UNITY_EDITOR etc.) passed to parse options")
    lines.append("5. Matching by TypeName.MethodName; overloaded methods matched by line proximity")
    lines.append("6. Methods not reported by SonarAnalyzer assumed CogCC=0")

    lines.append("\n## Known differences\n")
    lines.append("| Source | Unilyze | SonarAnalyzer | Impact |")
    lines.append("|--------|---------|---------------|--------|")
    lines.append("| `goto` | +1 | +1 (but also increments by nesting) | Sonar may be higher |")
    lines.append("| `#if`/`#elif` (preprocessor) | Parsed with UNITY_EDITOR | Parsed with UNITY_EDITOR | Should match |")
    lines.append("| Constructors | Recorded as methods | Detected but may not match key format | Sonar-only |")
    lines.append("| Properties (get/set) | Not always in methods list | Detected separately | Sonar-only |")

    return "\n".join(lines) + "\n"


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
def ensure_sonar_runner():
    """Verify SonarRunner tool is built."""
    dll = SONAR_RUNNER_DIR / "bin" / "Debug" / "net8.0" / "SonarRunner.dll"
    if not dll.exists():
        print("Building SonarRunner tool...", flush=True)
        res = subprocess.run(
            ["dotnet", "build", "-v", "quiet"],
            cwd=SONAR_RUNNER_DIR, capture_output=True, text=True, timeout=120,
        )
        if res.returncode != 0:
            print(f"ERROR: Failed to build SonarRunner:\n{res.stderr}", file=sys.stderr)
            sys.exit(1)
    print(f"SonarRunner ready at {dll}")


def process_project(
    name: str,
    source_dirs: list[str],
    unilyze_json: str,
    data_dir: Path,
) -> tuple[ProjectResult, dict]:
    """Process a single project (may have multiple source directories)."""
    result = ProjectResult(name=name)

    json_path = Path(unilyze_json)
    if not json_path.exists():
        result.error = f"Unilyze JSON not found: {unilyze_json}"
        return result, compute_stats(result)

    # Validate source directories
    valid_dirs = [d for d in source_dirs if Path(d).exists()]
    if not valid_dirs:
        result.error = f"No source directories found"
        return result, compute_stats(result)

    print(f"\n{'='*60}")
    print(f"Processing {name}")
    print(f"{'='*60}")

    # Load Unilyze results
    unilyze_methods = load_unilyze_cogcc(str(json_path))
    print(f"  Unilyze methods: {len(unilyze_methods)}")

    # Run SonarRunner on each directory and merge results
    all_sonar_entries: list[dict] = []
    for i, source_dir in enumerate(valid_dirs):
        suffix = f"-{i}" if len(valid_dirs) > 1 else ""
        sonar_json_path = data_dir / f"sonar-cogcc-{name.lower()}{suffix}.json"
        entries = run_sonar_runner(source_dir, sonar_json_path)
        all_sonar_entries.extend(entries)
    print(f"  SonarAnalyzer diagnostics (total): {len(all_sonar_entries)}")

    # Save merged Sonar results
    merged_path = data_dir / f"sonar-cogcc-{name.lower()}.json"
    with open(merged_path, "w") as f:
        json.dump(all_sonar_entries, f, indent=2)

    # Match
    matched, sonar_only, sonar_total = match_results(unilyze_methods, all_sonar_entries)
    result.matched = matched
    result.sonar_only = sonar_only
    result.sonar_raw_count = len(all_sonar_entries)

    stats = compute_stats(result)
    print(f"  Matched: {stats.get('total_matched', 0)}")
    print(f"  Non-trivial: {stats.get('nontrivial_count', 0)}")
    print(f"  Exact match: {stats.get('exact_match_rate', 0):.1%}")
    print(f"  Spearman rho: {stats.get('spearman_rho', 0):.3f}")

    return result, stats


def main():
    data_dir = Path(__file__).parent.parent / "data"
    data_dir.mkdir(parents=True, exist_ok=True)

    ensure_sonar_runner()

    projects = [
        (
            "HelloMarioFramework",
            ["/Volumes/CrucialX9/dev/github.com/HelloFangaming/HelloMarioFramework/Assets/HelloMarioFramework/Script"],
            str(data_dir / "unilyze-hmf.json"),
        ),
        (
            "VContainer",
            [
                "/tmp/cross-validation-repos/VContainer/VContainer/Assets/VContainer/Runtime",
                "/tmp/cross-validation-repos/VContainer/VContainer/Assets/VContainer/Editor",
                "/tmp/cross-validation-repos/VContainer/VContainer/Assets/Tests",
            ],
            str(data_dir / "unilyze-vcontainer.json"),
        ),
    ]

    all_results: list[tuple[ProjectResult, dict]] = []

    for name, source_dirs, json_path in projects:
        pr, stats = process_project(name, source_dirs, json_path, data_dir)
        all_results.append((pr, stats))

        # Write matched CSV
        if pr.matched:
            csv_path = data_dir / f"matched-cogcc-sonar-{name.lower()}.csv"
            with open(csv_path, "w", newline="") as f:
                w = csv.writer(f)
                w.writerow(["key", "type_name", "method_name", "unilyze_cogcc", "sonar_cogcc", "delta"])
                for m in sorted(pr.matched, key=lambda m: m.key):
                    w.writerow([m.key, m.type_name, m.method_name, m.unilyze_cogcc, m.sonar_cogcc, m.delta])
            print(f"  CSV written: {csv_path}")

    # Generate report
    report = format_report(all_results)
    report_path = Path(__file__).parent.parent / "phase2-cogcc-sonar.md"
    report_path.write_text(report)
    print(f"\nReport written: {report_path}")
    print(report)


if __name__ == "__main__":
    main()

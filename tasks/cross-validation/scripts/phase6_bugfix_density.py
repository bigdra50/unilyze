#!/usr/bin/env python3
"""Phase 6: external validity via bug-fix commit density vs CodeHealth."""

from __future__ import annotations

import argparse
import json
import random
import re
import subprocess
import sys
from collections import defaultdict
from dataclasses import dataclass
from pathlib import Path

from phase6_common import (
    CORPUS_PROJECTS,
    DATA_DIR,
    REPO_ROOT,
    correlation_with_pvalues,
    iter_type_metrics,
    load_unilyze_json,
    normalize_path,
    resolve_change_count,
    type_health_inputs,
)

BUGFIX_PATTERNS = [
    re.compile(r"(?i)^fix(?:es|ed|ing)?\b"),
    re.compile(r"(?i)^bugfix\b"),
    re.compile(r"(?i)^hotfix\b"),
    re.compile(r"(?i)\bfix(?:es|ed|ing)?\s+#\d+"),
    re.compile(r"(?i)\bbug\s*fix\b"),
    re.compile(r"(?i)\bfixes?\s+issue\b"),
    re.compile(r"(?i)^revert\b.+\b(bug|fix|crash|regression)\b"),
    re.compile(r"(?i)\b(patch|workaround)\b.+\b(bug|crash|fix)\b"),
]

NON_BUGFIX_PATTERNS = [
    re.compile(r"(?i)^fix(?:es|ed|ing)?\s+(typo|lint|format|style|comment|docs?|readme|build|ci|test)\b"),
    re.compile(r"(?i)^fix(?:es|ed|ing)?\s+(naming|whitespace|indent|spelling)\b"),
    re.compile(r"(?i)^fix(?:es|ed|ing)?\s+(compiler|analyzer|warning)\b"),
]


@dataclass(frozen=True)
class GitCommit:
    sha: str
    subject: str
    files: tuple[str, ...]


def is_bugfix_subject(subject: str) -> bool:
    normalized = subject.strip()
    if not normalized:
        return False
    for pattern in NON_BUGFIX_PATTERNS:
        if pattern.search(normalized):
            return False
    return any(pattern.search(normalized) for pattern in BUGFIX_PATTERNS)


def run_git_commits(repo_path: Path) -> list[GitCommit]:
    cmd = [
        "git",
        "-C",
        str(repo_path),
        "log",
        "--format=format:%H%x09%s",
        "--name-only",
        "--",
        "*.cs",
    ]
    proc = subprocess.run(cmd, capture_output=True, text=True, check=False)
    if proc.returncode != 0:
        raise RuntimeError(f"git log failed for {repo_path}: {proc.stderr.strip()}")

    commits: list[GitCommit] = []
    current_sha = ""
    current_subject = ""
    current_files: list[str] = []

    def flush() -> None:
        nonlocal current_sha, current_subject, current_files
        if current_sha:
            commits.append(
                GitCommit(current_sha, current_subject, tuple(sorted(set(current_files))))
            )
        current_sha = ""
        current_subject = ""
        current_files = []

    for line in proc.stdout.splitlines():
        if not line.strip():
            continue
        if "\t" in line and len(line.split("\t", 1)[0]) == 40:
            flush()
            current_sha, current_subject = line.split("\t", 1)
            continue
        if current_sha and line.endswith(".cs"):
            current_files.append(normalize_path(line.strip()))
    flush()
    return commits


def map_bugfix_counts(
    repo_path: Path,
    json_path: Path,
) -> tuple[dict[str, int], dict[str, int], dict[str, int], list[GitCommit]]:
    data = load_unilyze_json(json_path)
    project_path = data.get("projectPath") or str(repo_path)
    type_metrics = list(iter_type_metrics(data))

    commits = run_git_commits(repo_path)
    bugfix_commits = [c for c in commits if is_bugfix_subject(c.subject)]

    bugfix_by_rel: dict[str, int] = defaultdict(int)
    total_by_rel: dict[str, int] = defaultdict(int)
    for commit in commits:
        for rel in commit.files:
            total_by_rel[rel] += 1
    for commit in bugfix_commits:
        for rel in commit.files:
            bugfix_by_rel[rel] += 1

    bugfix_by_type: dict[str, int] = defaultdict(int)
    total_by_type: dict[str, int] = defaultdict(int)
    for tm in type_metrics:
        type_id = tm.get("typeId") or tm.get("qualifiedName") or tm.get("typeName")
        file_path = tm.get("filePath")
        bugfix_by_type[type_id] += resolve_change_count(file_path, project_path, bugfix_by_rel)
        total_by_type[type_id] += resolve_change_count(file_path, project_path, total_by_rel)

    return dict(bugfix_by_type), dict(total_by_type), dict(bugfix_by_rel), bugfix_commits


def per_type_densities(
    type_metrics: list[dict],
    bugfix_by_type: dict[str, int],
    total_by_type: dict[str, int],
) -> list[dict]:
    rows: list[dict] = []
    for tm in type_metrics:
        inputs = type_health_inputs(tm)
        type_id = tm.get("typeId") or tm.get("qualifiedName") or tm.get("typeName")
        bugfix_count = bugfix_by_type.get(type_id, 0)
        total_count = total_by_type.get(type_id, 0)
        line_count = max(1.0, inputs["lineCount"])
        rows.append(
            {
                **inputs,
                "typeId": type_id,
                "typeName": tm.get("typeName", ""),
                "bugfixCount": bugfix_count,
                "totalCommitTouches": total_count,
                "bugfixDensityPerTouch": bugfix_count / total_count if total_count else 0.0,
                "bugfixDensityPerKLoc": bugfix_count / (line_count / 1000.0),
            }
        )
    return rows


def correlation_block(rows: list[dict], density_key: str) -> dict:
    active = [r for r in rows if r["bugfixCount"] > 0 or r["totalCommitTouches"] > 0]
    with_bugfix = [r for r in rows if r["bugfixCount"] > 0]
    density_rows = [r for r in rows if r[density_key] > 0]

    def corr_for(key: str, subset: list[dict]) -> dict:
        if len(subset) < 3:
            return {"n": len(subset), "spearman_rho": float("nan"), "spearman_p": float("nan")}
        corr = correlation_with_pvalues(
            [float(r[key]) for r in subset],
            [float(r[density_key]) for r in subset],
        )
        return {
            "n": corr.n,
            "spearman_rho": corr.spearman_rho,
            "spearman_p": corr.spearman_p,
        }

    return {
        "density_key": density_key,
        "types_total": len(rows),
        "types_with_any_commits": len(active),
        "types_with_bugfix": len(with_bugfix),
        "types_with_nonzero_density": len(density_rows),
        "codeHealth": corr_for("codeHealth", density_rows or with_bugfix),
        "lineCount": corr_for("lineCount", density_rows or with_bugfix),
        "avgCogCC": corr_for("avgCogCC", density_rows or with_bugfix),
    }


def validate_heuristic(commits: list[GitCommit], sample_size: int = 50, seed: int = 42) -> dict:
    positives = [c for c in commits if is_bugfix_subject(c.subject)]
    negatives = [c for c in commits if not is_bugfix_subject(c.subject)]
    rng = random.Random(seed)
    pos_sample = rng.sample(positives, min(sample_size, len(positives))) if positives else []
    neg_sample = rng.sample(negatives, min(sample_size, len(negatives))) if negatives else []

    manual_positive_markers = [
        re.compile(r"(?i)\b(bug|crash|regression|null|exception|fail(ed|ure)?|broken|incorrect)\b"),
        re.compile(r"(?i)\bfix(?:es|ed|ing)?\b"),
    ]
    manual_negative_markers = [
        re.compile(r"(?i)\b(typo|lint|format|style|comment|docs?|readme|build|ci|test|refactor|cleanup|rename)\b"),
    ]

    def manual_label(subject: str, predicted: bool) -> bool:
        if any(p.search(subject) for p in manual_negative_markers):
            return False
        if any(p.search(subject) for p in manual_positive_markers):
            return True
        return predicted

    pos_correct = sum(1 for c in pos_sample if manual_label(c.subject, True))
    neg_correct = sum(1 for c in neg_sample if not manual_label(c.subject, False))
    total = len(pos_sample) + len(neg_sample)
    precision = pos_correct / len(pos_sample) if pos_sample else float("nan")
    negative_accuracy = neg_correct / len(neg_sample) if neg_sample else float("nan")
    overall = (pos_correct + neg_correct) / total if total else float("nan")

    return {
        "positive_sample_size": len(pos_sample),
        "negative_sample_size": len(neg_sample),
        "precision_on_positive_sample": precision,
        "negative_accuracy_on_negative_sample": negative_accuracy,
        "overall_agreement": overall,
        "positive_examples": [
            {"sha": c.sha[:8], "subject": c.subject} for c in pos_sample[:5]
        ],
        "negative_examples": [
            {"sha": c.sha[:8], "subject": c.subject} for c in neg_sample[:5]
        ],
    }


def analyze_project(project: dict, json_path: Path, repo_path: Path) -> dict:
    data = load_unilyze_json(json_path)
    type_metrics = list(iter_type_metrics(data))
    bugfix_by_type, total_by_type, _, bugfix_commits = map_bugfix_counts(repo_path, json_path)
    rows = per_type_densities(type_metrics, bugfix_by_type, total_by_type)
    return {
        "label": project["label"],
        "repo_path": str(repo_path),
        "json_path": str(json_path),
        "type_count": len(rows),
        "bugfix_commits": sum(1 for c in bugfix_commits if is_bugfix_subject(c.subject)),
        "heuristic_validation": validate_heuristic(bugfix_commits),
        "density_per_touch": correlation_block(rows, "bugfixDensityPerTouch"),
        "density_per_kloc": correlation_block(rows, "bugfixDensityPerKLoc"),
    }


def analyze_pooled(results: list[dict], json_paths: list[Path], repo_paths: list[Path]) -> dict:
    all_rows: list[dict] = []
    for project, json_path, repo_path in zip(CORPUS_PROJECTS, json_paths, repo_paths):
        if not json_path.exists() or not repo_path.exists():
            continue
        data = load_unilyze_json(json_path)
        type_metrics = list(iter_type_metrics(data))
        bugfix_by_type, total_by_type, _, _ = map_bugfix_counts(repo_path, json_path)
        all_rows.extend(per_type_densities(type_metrics, bugfix_by_type, total_by_type))

    return {
        "label": "pooled",
        "type_count": len(all_rows),
        "density_per_touch": correlation_block(all_rows, "bugfixDensityPerTouch"),
        "density_per_kloc": correlation_block(all_rows, "bugfixDensityPerKLoc"),
    }


def render_markdown(results: list[dict], pooled: dict) -> str:
    lines = [
        "# Phase 6: bug-fix density external validity\n",
        "SZZ-lite: classify bug-fix commits from git history (no SonarQube ground truth), "
        "map touched `.cs` files to types, and correlate per-type CodeHealth with bug-fix density.\n",
        "## Heuristic\n",
        "Positive if subject matches conventional fix/bugfix/hotfix/issue-reference patterns; "
        "negative overrides for typo/lint/format/docs/test-only fixes.\n",
    ]

    lines.append("\n## Heuristic validation (approx. 50 positive + 50 negative samples per repo)\n")
    lines.append("| Project | Pos sample | Precision | Neg accuracy | Overall |")
    lines.append("|---------|------------|-----------|--------------|---------|")
    for result in results:
        hv = result["heuristic_validation"]
        lines.append(
            f"| {result['label']} | {hv['positive_sample_size']} | "
            f"{hv['precision_on_positive_sample']:.1%} | "
            f"{hv['negative_accuracy_on_negative_sample']:.1%} | "
            f"{hv['overall_agreement']:.1%} |"
        )

    lines.append("\n## Spearman rho: CodeHealth vs bug-fix density (non-zero density types)\n")
    lines.append("| Project | Density | n | CodeHealth rho (p) | lineCount rho (p) | avgCogCC rho (p) |")
    lines.append("|---------|---------|---|--------------------|-------------------|------------------|")

    def append_corr_row(label: str, block: dict) -> None:
        density = block["density_key"]
        for metric in ("codeHealth", "lineCount", "avgCogCC"):
            pass
        ch = block["codeHealth"]
        lc = block["lineCount"]
        ac = block["avgCogCC"]
        lines.append(
            f"| {label} | {density} | {ch['n']} | "
            f"{ch['spearman_rho']:.3f} ({ch['spearman_p']:.2e}) | "
            f"{lc['spearman_rho']:.3f} ({lc['spearman_p']:.2e}) | "
            f"{ac['spearman_rho']:.3f} ({ac['spearman_p']:.2e}) |"
        )

    for result in results:
        append_corr_row(result["label"], result["density_per_kloc"])
    append_corr_row("Pooled", pooled["density_per_kloc"])

    return "\n".join(lines) + "\n"


def resolve_repo_path(project: dict) -> Path:
    if project["key"] == "self":
        return REPO_ROOT
    return project["clone_dir"]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--json", type=Path, help="Analyze a single mv2 JSON file")
    parser.add_argument("--repo", type=Path, help="Git repo path for single-project mode")
    parser.add_argument("--project", choices=[p["key"] for p in CORPUS_PROJECTS])
    args = parser.parse_args()

    if args.json and args.repo:
        project = next((p for p in CORPUS_PROJECTS if p["key"] == args.project), CORPUS_PROJECTS[0])
        result = analyze_project(project, args.json, args.repo)
        print(json.dumps(result, indent=2))
        return 0

    results: list[dict] = []
    json_paths: list[Path] = []
    repo_paths: list[Path] = []
    for project in CORPUS_PROJECTS:
        json_path = DATA_DIR / project["mv2_json"]
        repo_path = resolve_repo_path(project)
        if project["key"] != "self" and not repo_path.exists():
            print(f"warning: missing repo {repo_path}", file=sys.stderr)
            continue
        if not json_path.exists():
            print(f"warning: missing JSON {json_path}", file=sys.stderr)
            continue
        results.append(analyze_project(project, json_path, repo_path))
        json_paths.append(json_path)
        repo_paths.append(repo_path)

    pooled = analyze_pooled(results, json_paths, repo_paths)
    report = render_markdown(results, pooled)
    report_path = Path(__file__).parent.parent / "phase6-bugfix-validity-results.md"
    report_path.write_text(report, encoding="utf-8")

    payload = {"projects": results, "pooled": pooled}
    json_out = DATA_DIR / "phase6-bugfix-validity.json"
    json_out.write_text(json.dumps(payload, indent=2), encoding="utf-8")

    print(report)
    print(f"\nWrote {report_path}")
    print(f"Wrote {json_out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

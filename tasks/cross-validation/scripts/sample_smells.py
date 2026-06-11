#!/usr/bin/env python3
"""Build a stratified smell-precision labeling CSV from v2 unilyze snapshots.

Rare Kinds (corpus-wide count < frequentThreshold) are included exhaustively.
Frequent Kinds are stratified-random sampled (default 20 per Kind, seed 42)
with at least minProjects distinct projects when possible.
"""

from __future__ import annotations

import argparse
import csv
import json
import random
import sys
from collections import Counter, defaultdict
from dataclasses import dataclass
from pathlib import Path

from smell_kind_rules import rule_id_for_kind

SCRIPT_DIR = Path(__file__).resolve().parent
TASK_DIR = SCRIPT_DIR.parent
DATA_DIR = TASK_DIR / "data"
MANIFEST_PATH = TASK_DIR / "corpus-projects.json"
DEFAULT_OUTPUT = DATA_DIR / "smell-precision-labels.csv"

CSV_COLUMNS = [
    "project",
    "commit",
    "ruleId",
    "kind",
    "typeName",
    "methodName",
    "line",
    "severity",
    "message",
    "label",
    "judge",
    "rationale",
]


@dataclass(frozen=True)
class SmellOccurrence:
    project: str
    commit: str
    kind: str
    type_name: str
    method_name: str | None
    line: int | None
    severity: str
    message: str
    file_path: str | None

    @property
    def rule_id(self) -> str:
        return rule_id_for_kind(self.kind)

    def row(self) -> list[str]:
        return [
            self.project,
            self.commit,
            self.rule_id,
            self.kind,
            self.type_name,
            self.method_name or "",
            "" if self.line is None else str(self.line),
            self.severity,
            self.message,
            "",
            "",
            "",
        ]


def load_manifest() -> dict:
    with open(MANIFEST_PATH, encoding="utf-8") as f:
        return json.load(f)


def load_measurement_commits() -> dict[str, str]:
    summary_path = DATA_DIR / "smell-corpus-measurement.json"
    if not summary_path.is_file():
        return {}
    with open(summary_path, encoding="utf-8") as f:
        summary = json.load(f)
    return {
        entry["id"]: entry.get("commit") or ""
        for entry in summary.get("projects", [])
    }


def iter_snapshot_files(manifest: dict, include_optional: bool) -> list[tuple[str, Path, str]]:
    commits = load_measurement_commits()
    files: list[tuple[str, Path, str]] = []
    for project in manifest["projects"]:
        if project.get("optional") and not include_optional:
            continue
        output = DATA_DIR / project["outputFile"]
        if not output.is_file():
            continue
        commit = commits.get(project["id"], project.get("commit", ""))
        files.append((project["id"], output, commit or ""))
    return files


def extract_occurrences(project_id: str, snapshot_path: Path, commit: str) -> list[SmellOccurrence]:
    with open(snapshot_path, encoding="utf-8") as f:
        snapshot = json.load(f)

    occurrences: list[SmellOccurrence] = []
    for type_metrics in snapshot.get("typeMetrics") or []:
        type_name = type_metrics.get("typeName", "")
        file_path = type_metrics.get("filePath")
        for smell in type_metrics.get("codeSmells") or []:
            occurrences.append(
                SmellOccurrence(
                    project=project_id,
                    commit=commit,
                    kind=smell["kind"],
                    type_name=type_name,
                    method_name=smell.get("methodName"),
                    line=smell.get("line"),
                    severity=smell.get("severity", ""),
                    message=smell.get("message", ""),
                    file_path=file_path,
                )
            )
    return occurrences


def stratified_sample(
    pool: list[SmellOccurrence],
    sample_size: int,
    rng: random.Random,
    min_projects: int,
) -> list[SmellOccurrence]:
    if len(pool) <= sample_size:
        return list(pool)

    by_project: dict[str, list[SmellOccurrence]] = defaultdict(list)
    for item in pool:
        by_project[item.project].append(item)

    projects = sorted(by_project)
    if len(projects) == 1:
        return rng.sample(pool, sample_size)

    chosen: list[SmellOccurrence] = []
    used_keys: set[tuple[str, str, str | None, int | None]] = set()

    # Seed one occurrence per project first (up to sample_size).
    for project in projects:
        if len(chosen) >= sample_size:
            break
        candidates = by_project[project]
        pick = rng.choice(candidates)
        key = (pick.project, pick.type_name, pick.method_name, pick.line)
        if key not in used_keys:
            chosen.append(pick)
            used_keys.add(key)

    remaining = [o for o in pool if (o.project, o.type_name, o.method_name, o.line) not in used_keys]
    need = sample_size - len(chosen)
    if need > 0 and remaining:
        chosen.extend(rng.sample(remaining, min(need, len(remaining))))

    if len({o.project for o in chosen}) < min(min_projects, len(projects)):
        # Best-effort second pass: swap singles to increase project spread.
        chosen_projects = {o.project for o in chosen}
        for project in projects:
            if project in chosen_projects or len(chosen) >= sample_size:
                continue
            replacement_idx = next(
                (i for i, o in enumerate(chosen) if list(chosen_projects).count(o.project) > 1),
                None,
            )
            if replacement_idx is None:
                break
            candidate = rng.choice(by_project[project])
            chosen[replacement_idx] = candidate
            chosen_projects = {o.project for o in chosen}

    return chosen[:sample_size]


def build_sample(
    occurrences: list[SmellOccurrence],
    frequent_threshold: int,
    sample_size: int,
    seed: int,
    min_projects: int,
) -> tuple[list[SmellOccurrence], dict[str, dict[str, int]]]:
    by_kind: dict[str, list[SmellOccurrence]] = defaultdict(list)
    for occ in occurrences:
        by_kind[occ.kind].append(occ)

    corpus_counts = {kind: len(items) for kind, items in sorted(by_kind.items())}
    rng = random.Random(seed)
    selected: list[SmellOccurrence] = []
    plan: dict[str, dict[str, int]] = {}

    for kind, pool in sorted(by_kind.items()):
        corpus_n = len(pool)
        if corpus_n < frequent_threshold:
            picked = list(pool)
            strategy = "exhaustive"
        else:
            picked = stratified_sample(pool, sample_size, rng, min_projects)
            strategy = f"sampled({sample_size})"

        selected.extend(picked)
        plan[kind] = {
            "corpusCount": corpus_n,
            "labelCount": len(picked),
            "strategy": strategy,
        }

    selected.sort(key=lambda o: (o.kind, o.project, o.type_name, o.method_name or "", o.line or 0))
    return selected, plan


def write_csv(rows: list[SmellOccurrence], output_path: Path) -> None:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with open(output_path, "w", encoding="utf-8", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(CSV_COLUMNS)
        for row in rows:
            writer.writerow(row.row())


def print_plan(plan: dict[str, dict[str, int]]) -> None:
    print(f"{'Kind':<32} {'Corpus':>7} {'Label':>7} Strategy")
    print("-" * 62)
    for kind, info in sorted(plan.items()):
        print(
            f"{kind:<32} {info['corpusCount']:>7} {info['labelCount']:>7} {info['strategy']}"
        )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument(
        "--sample-size",
        type=int,
        default=20,
        help="Target rows per frequent Kind (default: 20).",
    )
    parser.add_argument(
        "--frequent-threshold",
        type=int,
        default=20,
        help="Kinds with corpus count below this are labeled exhaustively.",
    )
    parser.add_argument(
        "--min-projects",
        type=int,
        default=2,
        help="Minimum distinct projects in a frequent-Kind sample when possible.",
    )
    parser.add_argument(
        "--include-optional",
        action="store_true",
        help="Include optional supplemental snapshots (Unity-Decommissioned).",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=DEFAULT_OUTPUT,
    )
    parser.add_argument(
        "--check",
        action="store_true",
        help="Verify output exists and print Kind plan without rewriting.",
    )
    args = parser.parse_args()

    manifest = load_manifest()
    snapshot_files = iter_snapshot_files(manifest, args.include_optional)
    if not snapshot_files:
        print("No v2 snapshot files found. Run measure_smell_corpus.py first.", file=sys.stderr)
        return 1

    if args.check:
        if not args.output.is_file():
            print(f"Missing {args.output}", file=sys.stderr)
            return 1
        with open(args.output, encoding="utf-8") as f:
            reader = csv.DictReader(f)
            label_counts = Counter(row["kind"] for row in reader)
        print(f"OK: {args.output} ({sum(label_counts.values())} rows)")
        for kind, count in sorted(label_counts.items()):
            print(f"  {kind}: {count}")
        return 0

    all_occurrences: list[SmellOccurrence] = []
    for project_id, path, commit in snapshot_files:
        all_occurrences.extend(extract_occurrences(project_id, path, commit))

    selected, plan = build_sample(
        all_occurrences,
        frequent_threshold=args.frequent_threshold,
        sample_size=args.sample_size,
        seed=args.seed,
        min_projects=args.min_projects,
    )
    write_csv(selected, args.output)

    summary = {
        "seed": args.seed,
        "sampleSize": args.sample_size,
        "frequentThreshold": args.frequent_threshold,
        "totalCorpusSmells": len(all_occurrences),
        "totalLabelRows": len(selected),
        "kinds": plan,
        "snapshots": [str(p) for _, p, _ in snapshot_files],
    }
    summary_path = DATA_DIR / "smell-precision-sample-plan.json"
    with open(summary_path, "w", encoding="utf-8") as f:
        json.dump(summary, f, indent=2)
        f.write("\n")

    print(f"Wrote {args.output} ({len(selected)} rows)")
    print_plan(plan)
    print(f"Plan: {summary_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

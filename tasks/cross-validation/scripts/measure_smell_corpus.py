#!/usr/bin/env python3
"""Re-measure cross-validation projects with the current unilyze release.

Clones OSS repos under /tmp/cross-validation-repos (never inside the worktree),
runs unilyze, and writes self-describing v2 snapshots to tasks/cross-validation/data/.
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
from collections import Counter
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
TASK_DIR = SCRIPT_DIR.parent
REPO_ROOT = Path(__file__).resolve().parents[3]
DATA_DIR = TASK_DIR / "data"
MANIFEST_PATH = TASK_DIR / "corpus-projects.json"
CLONE_ROOT = Path("/tmp/cross-validation-repos")
EDITORS_SYMLINK_ROOT = Path("/tmp/unilyze-editors-symlinks")


def load_manifest() -> dict:
    with open(MANIFEST_PATH, encoding="utf-8") as f:
        return json.load(f)


def ensure_clone(project: dict) -> str | None:
    if "repo" not in project:
        return None

    clone_dir = CLONE_ROOT / project["cloneDir"]
    clone_dir.parent.mkdir(parents=True, exist_ok=True)

    if not (clone_dir / ".git").is_dir():
        print(f"  Cloning {project['repo']}...", flush=True)
        subprocess.run(
            ["git", "clone", "--filter=blob:none", project["repo"], str(clone_dir)],
            check=True,
        )

    commit = project["commit"]
    if commit != "HEAD":
        subprocess.run(["git", "-C", str(clone_dir), "fetch", "origin"], check=False)
        subprocess.run(["git", "-C", str(clone_dir), "checkout", "-q", commit], check=True)
    else:
        subprocess.run(["git", "-C", str(clone_dir), "pull", "--ff-only"], check=False)

    result = subprocess.run(
        ["git", "-C", str(clone_dir), "rev-parse", "HEAD"],
        capture_output=True,
        text=True,
        check=True,
    )
    return result.stdout.strip()


def prepare_editors_root(manifest: dict) -> Path | None:
    editors_root = manifest.get("editorsRoot")
    aliases = manifest.get("editorVersionAliases") or {}
    if not editors_root or not Path(editors_root).is_dir():
        return None

    EDITORS_SYMLINK_ROOT.mkdir(parents=True, exist_ok=True)
    for installed in Path(editors_root).iterdir():
        if installed.is_dir():
            link = EDITORS_SYMLINK_ROOT / installed.name
            if not link.exists():
                link.symlink_to(installed, target_is_directory=True)

    for requested, installed in aliases.items():
        target = EDITORS_SYMLINK_ROOT / installed
        link = EDITORS_SYMLINK_ROOT / requested
        if target.exists() and not link.exists():
            link.symlink_to(target, target_is_directory=True)

    return EDITORS_SYMLINK_ROOT


def maybe_copy_script_assemblies(project: dict) -> None:
    source = project.get("scriptAssembliesSource")
    if not source:
        return

    src = Path(source)
    if not src.is_dir():
        print(f"  Warning: ScriptAssemblies source missing: {src}", file=sys.stderr)
        return

    dest = Path(project["projectPath"]) / "Library" / "ScriptAssemblies"
    if dest.is_dir() and any(dest.glob("*.dll")):
        return

    dest.parent.mkdir(parents=True, exist_ok=True)
    print(f"  Copying ScriptAssemblies from {src}...", flush=True)
    shutil.copytree(src, dest, dirs_exist_ok=True)


def resolve_project_path(project: dict) -> Path:
    raw = project["projectPath"]
    path = Path(raw)
    if not path.is_absolute():
        path = (REPO_ROOT / raw).resolve()
    return path


def run_unilyze(project_path: Path, requested_level: str, env: dict[str, str]) -> tuple[dict, str]:
    cmd = [
        "dotnet",
        "run",
        "--project",
        str(REPO_ROOT / "src/Unilyze/Unilyze.csproj"),
        "-c",
        "Release",
        "--framework",
        "net10.0",
        "--no-build",
        "--",
        "-p",
        str(project_path),
        "-f",
        "json",
    ]
    if requested_level:
        cmd.extend(["--level", requested_level])

    proc = subprocess.run(cmd, capture_output=True, text=True, env=env)
    stderr = proc.stderr or ""

    if proc.returncode != 0 and requested_level:
        print(
            f"  Level '{requested_level}' unavailable; retrying without pin...",
            flush=True,
        )
        cmd = [c for c in cmd if c not in {"--level", requested_level}]
        proc = subprocess.run(cmd, capture_output=True, text=True, env=env)
        stderr = proc.stderr or ""

    if proc.returncode != 0:
        print(proc.stderr, file=sys.stderr)
        raise RuntimeError(f"unilyze failed for {project_path} (exit {proc.returncode})")

    return json.loads(proc.stdout), stderr


def smell_kind_counts(snapshot: dict) -> Counter[str]:
    counts: Counter[str] = Counter()
    for type_metrics in snapshot.get("typeMetrics") or []:
        for smell in type_metrics.get("codeSmells") or []:
            counts[smell["kind"]] += 1
    return counts


def measure_project(project: dict, manifest: dict, env: dict[str, str], skip_clone: bool) -> dict:
    commit = None
    if "repo" in project:
        if not skip_clone:
            commit = ensure_clone(project)
        else:
            clone_dir = CLONE_ROOT / project["cloneDir"]
            if (clone_dir / ".git").is_dir():
                result = subprocess.run(
                    ["git", "-C", str(clone_dir), "rev-parse", "HEAD"],
                    capture_output=True,
                    text=True,
                    check=False,
                )
                commit = result.stdout.strip() if result.returncode == 0 else project.get("commit")

        maybe_copy_script_assemblies(project)

    project_path = resolve_project_path(project)
    if not project_path.is_dir():
        raise FileNotFoundError(f"Project path not found: {project_path}")

    requested = project.get("requestedLevel") or "complete"
    print(f"  Measuring {project['name']} ({project_path})...", flush=True)
    snapshot, stderr = run_unilyze(project_path, requested, env)

    output_path = DATA_DIR / project["outputFile"]
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(snapshot, f, indent=2)
        f.write("\n")

    counts = smell_kind_counts(snapshot)
    meta = {
        "id": project["id"],
        "name": project["name"],
        "commit": commit or project.get("commit"),
        "projectPath": str(project_path),
        "outputFile": project["outputFile"],
        "requestedLevel": requested,
        "analysisLevel": snapshot.get("analysisLevel"),
        "toolVersion": snapshot.get("toolVersion"),
        "metricsVersion": snapshot.get("metricsVersion"),
        "totalSmells": sum(counts.values()),
        "kindCounts": dict(sorted(counts.items())),
    }
    if project.get("optional"):
        meta["optional"] = True
    if stderr.strip():
        meta["stderrTail"] = stderr.strip().splitlines()[-3:]

    print(
        f"  -> {output_path.name}: level={meta['analysisLevel']}, "
        f"smells={meta['totalSmells']}, kinds={len(counts)}",
        flush=True,
    )
    return meta


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--project",
        action="append",
        help="Measure only these project ids (default: all non-optional unless --include-optional).",
    )
    parser.add_argument(
        "--include-optional",
        action="store_true",
        help="Also measure optional supplemental projects (Unity-Decommissioned).",
    )
    parser.add_argument(
        "--skip-clone",
        action="store_true",
        help="Use existing /tmp clones; do not fetch/checkout.",
    )
    parser.add_argument(
        "--skip-build",
        action="store_true",
        help="Skip dotnet build (assumes Release net10.0 is already built).",
    )
    args = parser.parse_args()

    manifest = load_manifest()
    editors_root = prepare_editors_root(manifest)

    env = os.environ.copy()
    if editors_root is not None:
        env["UNILYZE_EDITORS_ROOT"] = str(editors_root)

    if not args.skip_build:
        print("Building unilyze (Release, net10.0)...", flush=True)
        subprocess.run(
            [
                "dotnet",
                "build",
                str(REPO_ROOT / "src/Unilyze/Unilyze.csproj"),
                "-c",
                "Release",
                "-f",
                "net10.0",
                "-v",
                "q",
            ],
            check=True,
        )

    selected = set(args.project or [])
    results: list[dict] = []

    for project in manifest["projects"]:
        if project.get("optional") and not args.include_optional and project["id"] not in selected:
            continue
        if selected and project["id"] not in selected:
            continue

        print(f"\n[{project['id']}]", flush=True)
        try:
            results.append(measure_project(project, manifest, env, args.skip_clone))
        except Exception as exc:  # noqa: BLE001 - CLI aggregates per-project failures
            print(f"  ERROR: {exc}", file=sys.stderr)
            if project.get("optional"):
                print("  (optional project skipped)", flush=True)
                continue
            return 1

    summary_path = DATA_DIR / "smell-corpus-measurement.json"
    with open(summary_path, "w", encoding="utf-8") as f:
        json.dump({"projects": results}, f, indent=2)
        f.write("\n")

    print(f"\nMeasurement summary: {summary_path}", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

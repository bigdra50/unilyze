#!/usr/bin/env python3
"""Assemble changelog.d/ fragments into a CHANGELOG.md release section.

Each pull request adds one file per user-facing change instead of editing the
shared `[Unreleased]` section of CHANGELOG.md (which causes near-guaranteed
merge conflicts between concurrent PRs). At release time this script bundles
those fragments -- plus any legacy hand-written `[Unreleased]` entries left
over from before this convention -- into a new `## [X.Y.Z] - <date>` section,
inserted directly after `[Unreleased]`, and deletes the consumed fragments.

Fragment format: changelog.d/<id>.<category>.md
  <id>       PR/issue number or a short slug. Used only for ordering.
  <category> one of: added, changed, deprecated, removed, fixed, security
             (Keep a Changelog: https://keepachangelog.com/en/1.1.0/).
  contents   the Markdown text of one changelog bullet, without a leading
             "- " (this script adds it). May span multiple lines. A
             "**[metrics]**"-style prefix is allowed and passed through
             verbatim -- see docs/metrics.md#metric-compatibility-policy.

Usage:
  python3 scripts/changelog/assemble.py <X.Y.Z> [--date YYYY-MM-DD] [--dry-run]
  python3 scripts/changelog/assemble.py --validate

--dry-run prints the assembled CHANGELOG.md to stdout instead of writing it
and never deletes fragments. --validate only checks changelog.d/ fragment
filenames, categories, and non-empty content; it touches nothing and does not
require a version argument.

This script never runs git; the caller stages/commits the result.
"""
from __future__ import annotations

import argparse
import datetime as dt
import re
import sys
from dataclasses import dataclass
from pathlib import Path

CATEGORY_ORDER = ["added", "changed", "deprecated", "removed", "fixed", "security"]
CATEGORY_TITLES = {
    "added": "Added",
    "changed": "Changed",
    "deprecated": "Deprecated",
    "removed": "Removed",
    "fixed": "Fixed",
    "security": "Security",
}
TITLE_TO_CATEGORY = {title.lower(): cat for cat, title in CATEGORY_TITLES.items()}

FRAGMENT_NAME_RE = re.compile(
    r"^(?P<id>[A-Za-z0-9][A-Za-z0-9_-]*)\.(?P<category>" + "|".join(CATEGORY_ORDER) + r")\.md$"
)
UNRELEASED_HEADING = "## [Unreleased]"
VERSION_RE = re.compile(r"^\d+\.\d+\.\d+$")
DATE_RE = re.compile(r"^\d{4}-\d{2}-\d{2}$")


class AssembleError(Exception):
    """Raised for user-fixable problems (bad fragment, malformed CHANGELOG.md, ...)."""


@dataclass
class Fragment:
    path: Path
    id: str
    category: str
    body: str


def load_fragments(fragments_dir: Path) -> list[Fragment]:
    """Read and validate every changelog.d/*.md fragment.

    Raises AssembleError listing every problem found (not just the first),
    so --validate and normal runs report everything in one pass.
    """
    if not fragments_dir.is_dir():
        return []

    fragments: list[Fragment] = []
    errors: list[str] = []

    for path in sorted(fragments_dir.iterdir()):
        if not path.is_file() or path.suffix != ".md":
            continue
        if path.name == "README.md":
            continue

        m = FRAGMENT_NAME_RE.match(path.name)
        if not m:
            errors.append(
                f"{path.name}: invalid fragment filename; expected "
                f"'<id>.<category>.md' with category in {CATEGORY_ORDER}"
            )
            continue

        body = path.read_text(encoding="utf-8").strip("\n").rstrip()
        if not body.strip():
            errors.append(f"{path.name}: fragment is empty")
            continue

        fragments.append(Fragment(path=path, id=m.group("id"), category=m.group("category"), body=body))

    if errors:
        raise AssembleError("Invalid changelog.d/ fragment(s):\n" + "\n".join(f"  - {e}" for e in errors))

    return fragments


def _fragment_sort_key(fragment: Fragment) -> tuple[int, int, str]:
    """Numeric ids sort ascending first, then slug ids sort by ordinal name."""
    if fragment.id.isdigit():
        return (0, int(fragment.id), "")
    return (1, 0, fragment.id)


def parse_entries(body_lines: list[str]) -> dict[str, list[str]]:
    """Parse the legacy hand-written entries inside the [Unreleased] body.

    Groups bullets by their nearest preceding '### Category' heading. A
    bullet may continue onto following non-bullet, non-heading lines; a
    blank line ends the current bullet.
    """
    entries: dict[str, list[str]] = {cat: [] for cat in CATEGORY_ORDER}
    current_cat: str | None = None
    current_lines: list[str] = []

    def flush() -> None:
        nonlocal current_lines
        if current_lines:
            if current_cat is None:
                raise AssembleError("[Unreleased] has a bullet before any '### Category' heading")
            entries[current_cat].append("\n".join(current_lines).rstrip())
            current_lines = []

    for line in body_lines:
        heading = re.match(r"^### (.+?)\s*$", line)
        if heading:
            flush()
            title = heading.group(1).strip()
            category = TITLE_TO_CATEGORY.get(title.lower())
            if category is None:
                raise AssembleError(
                    f"[Unreleased] has unknown subsection '### {title}'; "
                    f"expected one of {sorted(CATEGORY_TITLES.values())}"
                )
            current_cat = category
            continue

        if line.startswith("- "):
            flush()
            current_lines = [line[2:]]
            continue

        if line.strip() == "":
            flush()
            continue

        # Continuation of the current bullet.
        if current_lines:
            current_lines.append(line)

    flush()
    return entries


@dataclass
class ParsedChangelog:
    preamble_lines: list[str]
    legacy_entries: dict[str, list[str]]
    rest_lines: list[str]


def parse_changelog(text: str) -> ParsedChangelog:
    lines = text.splitlines()

    unreleased_idx = next((i for i, line in enumerate(lines) if line.strip() == UNRELEASED_HEADING), None)
    if unreleased_idx is None:
        raise AssembleError(f"CHANGELOG.md has no '{UNRELEASED_HEADING}' section")

    end_idx = len(lines)
    for j in range(unreleased_idx + 1, len(lines)):
        if lines[j].startswith("## ["):
            end_idx = j
            break

    preamble_lines = lines[:unreleased_idx]
    body_lines = lines[unreleased_idx + 1 : end_idx]
    rest_lines = lines[end_idx:]

    return ParsedChangelog(
        preamble_lines=preamble_lines,
        legacy_entries=parse_entries(body_lines),
        rest_lines=rest_lines,
    )


def merge_entries(legacy: dict[str, list[str]], fragments: list[Fragment]) -> dict[str, list[str]]:
    merged = {cat: list(legacy.get(cat, [])) for cat in CATEGORY_ORDER}
    for fragment in sorted(fragments, key=_fragment_sort_key):
        merged[fragment.category].append(fragment.body)
    return merged


def render_section(version: str, date: str, merged: dict[str, list[str]]) -> list[str]:
    lines = [f"## [{version}] - {date}"]
    for cat in CATEGORY_ORDER:
        bullets = merged[cat]
        if not bullets:
            continue
        lines.append("")
        lines.append(f"### {CATEGORY_TITLES[cat]}")
        lines.append("")
        for bullet in bullets:
            bullet_lines = bullet.split("\n")
            lines.append(f"- {bullet_lines[0]}")
            for cont in bullet_lines[1:]:
                lines.append(f"  {cont}" if cont else "")
    return lines


def build_output(parsed: ParsedChangelog, section_lines: list[str]) -> str:
    parts = list(parsed.preamble_lines)
    parts.append(UNRELEASED_HEADING)
    parts.append("")
    parts.extend(section_lines)
    if parsed.rest_lines:
        parts.append("")
        parts.extend(parsed.rest_lines)
    text = "\n".join(parts)
    if not text.endswith("\n"):
        text += "\n"
    return text


def validate_version(version: str) -> None:
    if not VERSION_RE.match(version):
        raise AssembleError(f"version must look like X.Y.Z (no 'v' prefix), got {version!r}")


def validate_date(date: str) -> None:
    if not DATE_RE.match(date):
        raise AssembleError(f"--date must look like YYYY-MM-DD, got {date!r}")
    try:
        dt.date.fromisoformat(date)
    except ValueError as exc:
        raise AssembleError(f"--date is not a valid calendar date: {date!r}") from exc


def run(args: argparse.Namespace) -> int:
    root = Path(args.root)
    fragments_dir = root / "changelog.d"
    changelog_path = root / "CHANGELOG.md"

    fragments = load_fragments(fragments_dir)

    if args.validate:
        print(f"changelog.d: {len(fragments)} fragment(s) OK")
        return 0

    if not args.version:
        raise AssembleError("a version argument (X.Y.Z) is required unless --validate is given")
    validate_version(args.version)

    date = args.date or dt.date.today().isoformat()
    validate_date(date)

    if not changelog_path.is_file():
        raise AssembleError(f"{changelog_path} not found")
    parsed = parse_changelog(changelog_path.read_text(encoding="utf-8"))

    merged = merge_entries(parsed.legacy_entries, fragments)
    if not any(merged.values()):
        raise AssembleError(
            "Nothing to release: no changelog.d/ fragments and no [Unreleased] entries"
        )

    section_lines = render_section(args.version, date, merged)
    output = build_output(parsed, section_lines)

    if args.dry_run:
        sys.stdout.write(output)
        return 0

    changelog_path.write_text(output, encoding="utf-8")
    for fragment in fragments:
        fragment.path.unlink()

    print(f"Wrote {changelog_path}: [{args.version}] - {date} ({len(fragments)} fragment(s) consumed)")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("version", nargs="?", help="release version, e.g. 0.6.0 (no 'v' prefix)")
    parser.add_argument("--date", help="release date as YYYY-MM-DD (default: today, UTC-naive local date)")
    parser.add_argument("--dry-run", action="store_true", help="print the result instead of writing files")
    parser.add_argument(
        "--validate",
        action="store_true",
        help="only validate changelog.d/ fragments (filenames, categories, non-empty content)",
    )
    parser.add_argument(
        "--root",
        default=".",
        help="repository root containing CHANGELOG.md and changelog.d/ (default: current directory)",
    )
    args = parser.parse_args(argv)

    try:
        return run(args)
    except AssembleError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())

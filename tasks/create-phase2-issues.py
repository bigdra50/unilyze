#!/usr/bin/env python3
"""Create Phase 2 issues from workflow drafts (/tmp/phase2-drafts.json).

Creation order = work order (A wave -> B wave -> C wave). Dependency
tokens "P2-<KEY>" in bodies are replaced with actual issue numbers for
already-created issues (deps always point backward in this ordering).
Writes tasks/phase2-issues.json mapping key -> number.
"""
import json
import re
import subprocess
import tempfile
import os
import sys
import time

REPO = "bigdra50/unilyze"
MILESTONE = 2  # Phase 2 - Unity specialization, calibration & PR workflow
ORDER = [
    "A01", "A02", "A03", "A04", "A05", "A06", "A07", "A08",
    "B01", "B02", "B03", "B04", "B05", "B06", "B07", "B08",
    "C01", "C02", "C03", "C04", "C05", "C06", "C07", "C08", "C09", "C10",
]
VALID_LABELS = {"enhancement", "bug", "documentation", "quick-win", "metrics-compat", "epic"}


def gh_token():
    return subprocess.run(["gh", "auth", "token", "--user", "bigdra50"],
                          capture_output=True, text=True).stdout.strip()


def api(args, body_file=None, retries=5):
    """REST call with identity-checked retry (parallel-session token races)."""
    for attempt in range(retries):
        env = dict(os.environ, GH_TOKEN=gh_token())
        who = subprocess.run(["gh", "api", "user", "-q", ".login"],
                             capture_output=True, text=True, env=env).stdout.strip()
        if who == "bigdra50":
            r = subprocess.run(["gh", "api"] + args, capture_output=True, text=True, env=env)
            if r.returncode == 0:
                return r.stdout
            if "already exists" in r.stderr or "422" in r.stderr:
                print(f"  non-retryable: {r.stderr[:200]}", file=sys.stderr)
                return None
        time.sleep(8)
    return None


def main():
    with open("/tmp/phase2-drafts.json") as f:
        drafts = {d["key"]: d for d in json.load(f)}

    missing = [k for k in ORDER if k not in drafts]
    if missing:
        print(f"missing drafts: {missing}", file=sys.stderr)
        sys.exit(1)

    created = {}
    for key in ORDER:
        d = drafts[key]
        body = d["body"]
        for ck, num in created.items():
            body = re.sub(rf"P2-{ck}\b", f"#{num}", body)
        labels = [l.strip() for l in d["labels"].split(",") if l.strip() in VALID_LABELS]
        payload = {
            "title": d["title"],
            "body": body,
            "labels": labels or ["enhancement"],
            "milestone": MILESTONE,
        }
        with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as f:
            json.dump(payload, f)
            path = f.name
        out = api([f"repos/{REPO}/issues", "--input", path])
        os.unlink(path)
        if out is None:
            print(f"FAILED {key}", file=sys.stderr)
            sys.exit(1)
        num = json.loads(out)["number"]
        created[key] = num
        print(f"{key} -> #{num} {d['title'][:70]}")

    with open("/Volumes/CrucialX9/dev/github.com/bigdra50/unilyze/tasks/phase2-issues.json", "w") as f:
        json.dump(created, f, indent=2)
    print("map written")


if __name__ == "__main__":
    main()

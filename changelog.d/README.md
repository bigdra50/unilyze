# changelog.d/

Changelog fragments. Every pull request that changes `src/` adds one file
here instead of editing `CHANGELOG.md`'s shared `[Unreleased]` section
directly, so concurrent PRs never conflict on that file. At release time
`scripts/changelog/assemble.py` bundles all fragments into a new
`## [X.Y.Z] - <date>` section and deletes the files it consumed.

## Filename

```
<id>.<category>.md
```

- `<id>` — the PR or issue number (preferred), or a short slug if neither
  applies. Used only to order entries within a category; it does not need to
  be unique across categories.
- `<category>` — one of, matching [Keep a Changelog](https://keepachangelog.com/en/1.1.0/):
  - `added`
  - `changed`
  - `deprecated`
  - `removed`
  - `fixed`
  - `security`

Example: `224.changed.md`.

## Content

The file body is the Markdown text of one changelog bullet, **without** a
leading `- ` — `assemble.py` adds that. Write it the way it should read in
`CHANGELOG.md`: reference the PR/issue (`([#224](https://github.com/bigdra50/unilyze/pull/224))`),
and describe the user-facing effect, not the implementation diff. Multi-line
bodies are allowed; continuation lines are re-indented under the bullet.

If the change alters a computed metric value, prefix the text with
`**[metrics]**` per the
[Metric Compatibility Policy](../docs/metrics.md#metric-compatibility-policy)
— that policy also requires at least a minor version bump and (if applicable)
a `metricsVersion` bump, independent of this fragment convention.

## Opting out

Changes with no user-facing effect (docs, CI, refactors with no observable
behavior change, etc.) don't need a fragment. Add the `no-changelog` label to
the pull request instead; the `changelog-guard` CI check honors it.

## Usage

```bash
# Validate every fragment's filename, category, and content (run in CI):
python3 scripts/changelog/assemble.py --validate

# Preview the release section a version would produce, without writing anything:
python3 scripts/changelog/assemble.py 0.6.0 --dry-run

# Bundle fragments (and any legacy hand-written [Unreleased] entries) into
# CHANGELOG.md, deleting consumed fragments. Run once per release, then
# review the diff before committing:
python3 scripts/changelog/assemble.py 0.6.0
```

See `scripts/changelog/assemble.py`'s module docstring for the full format
and merge rules.

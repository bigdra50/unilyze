# CLAUDE.md

Guidance for AI agents (and humans) working in this repo.

## Project

`unilyze` is a C# CLI that analyzes Unity / .NET projects with Roslyn and produces
dependency graphs and code-quality metrics. The interactive viewer ships two ways:

- **Static** — `analyze -f html` emits a single self-contained HTML file (inline
  script/style/vendor; ELK loaded from unpkg at runtime; dagre fallback offline).
- **Live** — `unilyze serve -p <path>` runs a loopback HTTP server that re-analyzes on
  source change and pushes full snapshots to the browser via ETag long-polling, with
  same-origin assets under a strict CSP. See `src/Unilyze/Serve/`.

The viewer source lives in `src/Unilyze/Templates/viewer/{index.html,styles.css,main.js}`
(combined into the static output by `combine.py`) and `src/Unilyze/Templates/serve/`
(the serve-only page shell + long-poll client). `main.js` is shared by both modes:
`buildDerivedState(data)` + `applySnapshot(data)` let the live viewer swap snapshots
without a reload; the static path runs the same init with embedded data.

## Build & test

```bash
dotnet build src/Unilyze/Unilyze.csproj -f net8.0 -c Debug
# Tests are multi-targeted; run each TFM that CI runs (net8.0;net9.0;net10.0):
dotnet test tests/Unilyze.Tests/Unilyze.Tests.csproj -f net8.0
```

- Keep all three TFMs green before finishing; CI runs net8.0/net9.0/net10.0 (+ windows net10.0).
- MinVer derives the version from git tags — without tags the version is `0.0.0` and
  `ToolVersionInfoTests` fails. Run `git fetch --tags` if needed.
- The static viewer build needs `python3` (`combine.py`).

## Visual verification (screenshots) — REQUIRED for any viewer/UI change

When you touch the viewer or any user-facing screen — `Templates/viewer/*`,
`Templates/serve/*`, the serve HTTP handler's HTML/CSP, or anything that changes what
renders in the browser — **render each affected screen in a real browser and look at the
screenshots before declaring the change done.** Unit/HTTP tests do not catch broken
rendering, CSP violations, or layout regressions; only looking does.

Use the Playwright harness (it also runs in CI as the `viewer-screenshots` job, gating on
console/page errors + key elements, and uploads the PNGs as artifacts):

```bash
dotnet build src/Unilyze/Unilyze.csproj -f net10.0 -c Release   # or any TFM
cd scripts/viewer-screenshots
npm install && npx playwright install chromium                   # first time
node capture.mjs                                                 # writes ./out/*.png
# Then visually inspect ./out/*.png (read the image files).
```

In this hosted environment Chromium is pre-installed at `/opt/pw-browsers`
(`PLAYWRIGHT_BROWSERS_PATH`); link the global Playwright into `node_modules` instead of
`npm install` if offline:
`mkdir -p node_modules && ln -sfn /opt/node22/lib/node_modules/playwright node_modules/playwright && ln -sfn /opt/node22/lib/node_modules/playwright-core node_modules/playwright-core`.

Screens to capture and eyeball (extend `capture.mjs` when you add a screen/state):

1. **Serve initial** — live status bar (`● live · gen N · updated …`), collapsed root.
2. **Expanded graph** — type-dependency graph after Expand All + Fit (ELK layout, health
   badges, edge legend).
3. **Type → source** — type detail panel → "View source" → read-only source panel.
4. **Stale state** — analysis failure keeps the prior snapshot + shows the stale banner.

For static-viewer changes, also generate `analyze -f html` output and open it (it has the
offline-report fallback path that serve does not exercise).

A screenshot test is **smoke + artifacts**, not a pixel diff: it must fail on render
errors and missing elements, but does not compare pixels (font/OS differences make that
flaky). When adding a screen, assert its key elements and add a `page.screenshot(...)`.

## Conventions

- Commit messages: gitmoji prefix, Japanese body, one logical change per commit; reference
  issues with `Closes #N`.
- Do not edit `CHANGELOG.md`'s `[Unreleased]` section directly. Add a
  `changelog.d/<PR-number>.<category>.md` fragment instead (see
  [changelog.d/README.md](changelog.d/README.md)); `scripts/changelog/assemble.py` bundles
  fragments into a release section at tag time.
- Match surrounding code style; the CLI dispatch lives in `src/Unilyze/Program.cs` and arg
  validation in `src/Unilyze/Cli/CliArgValidation.cs`.
- Do not leak absolute filesystem paths to the browser (serve scrubs them to opaque
  `fileId`s + display names; keep that invariant for new client-facing data/messages).

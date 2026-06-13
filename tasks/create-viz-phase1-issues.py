#!/usr/bin/env python3
"""Create Phase 1 issues for the live-visualization roadmap (tasks/visualization-roadmap-v2.md).

Source: Codex GPT-5.5 (xhigh) revision of the Claude multi-agent roadmap, after
cross-review. Every claim is code-verified with file:line evidence.

Issues are created in work order. Dependencies reference earlier issues by actual
number (substituted at creation time via {P1_NN} placeholders). Outputs
tasks/viz-phase1-issues.json mapping order code -> issue number.

Run order is dependency-first: serve foundation -> security -> change detection ->
delivery/viewer -> source API -> E2E, then the independent quick wins.
"""
import json
import subprocess
import tempfile
import os
import re
import sys
from pathlib import Path

REPO = "bigdra50/unilyze"
MILESTONE_P1 = "Phase 1 - Live visualization MVP (serve)"

ISSUES = [
    # ---------------------------------------------------------------- P1-01
    dict(
        code="P1-01",
        title="[Feature] Add an independent `unilyze serve` command that does not share lifecycle with analyze/MCP",
        labels="enhancement",
        body="""Order: P1-01 | Effort: M | Impact: high | Verdict: code-verified (Codex GPT-5.5)

## Summary

Introduce `unilyze serve -p <path>` as a long-running command, separate from the one-shot `analyze` pipeline and from the stdio MCP server. This is the foundation of the live-visualization MVP: a resident process that re-analyzes on source change and serves the existing viewer.

## Evidence

- Current dispatch is one-shot: analyze, write HTML/JSON, `file://` open, exit (`src/Unilyze/Program.cs:140-180`, `src/Unilyze/Output/HtmlFormatter.cs:13-24`).
- Top-level command allowlist + dispatch live in two places (`src/Unilyze/Cli/CliArgValidation.cs:5-11`, `src/Unilyze/Program.cs:18-45`).
- MCP is a stdin-EOF synchronous loop with a different lifetime contract; do not merge (`src/Unilyze/Mcp/McpStdioServer.cs:8-40`).

## Plan

1. Register `serve` in both the command allowlist and the dispatch switch.
2. Add a `ServeRunner` that owns the HTTP server, watcher, and analysis loop (built in later issues).
3. Graceful shutdown: Ctrl-C / ProcessExit stops the listener, disposes the watcher, exits 0.
4. `serve` is excluded from the `-f json` / `--no-open` / stdout batch contract (it never terminates on its own).

## Acceptance criteria

- [ ] `unilyze serve -p <path>` starts and stays resident; Ctrl-C exits 0.
- [ ] Unknown `serve` options are usage errors (exit 1), consistent with the CLI contract.
- [ ] MCP and analyze behavior unchanged.

## Dependencies

Foundation for the rest of Phase 1.
""",
    ),
    # ---------------------------------------------------------------- P1-02
    dict(
        code="P1-02",
        title="[Feature] serve over BCL HttpListener bound to 127.0.0.1 with port retry (no port-0 reliance)",
        labels="enhancement",
        body="""Order: P1-02 | Effort: M | Impact: high | Verdict: code-verified (Codex GPT-5.5)

## Summary

Serve the viewer and APIs from `System.Net.HttpListener`, loopback-only. Pick `--port` or a random high port and retry on conflict; do not depend on port-0 OS assignment (HttpListener has no API to read back an OS-assigned port).

## Plan

1. Bind `http://127.0.0.1:<port>/`. No `--host`; LAN exposure is explicitly out of scope.
2. On `HttpListenerException` (port in use), retry with the next candidate port.
3. Keep `--no-open`; always print the resolved URL to stderr (works headless/SSH).
4. Route `GET /` to the viewer HTML; APIs added in later issues.

## Acceptance criteria

- [ ] Server binds loopback only; reachable at the printed URL.
- [ ] Port conflict retries instead of crashing.
- [ ] `--no-open` prints URL to stderr and does not launch a browser.

## Dependencies

Requires {P1_01}.
""",
    ),
    # ---------------------------------------------------------------- P1-03
    dict(
        code="P1-03",
        title="[Security] Fix the serve security boundary: HTML-embedded token + Authorization Bearer, Host/Origin checks, no CORS/cookies",
        labels="enhancement,documentation",
        body="""Order: P1-03 | Effort: M | Impact: high | Verdict: code-verified (Codex GPT-5.5)

## Summary

serve opens new attack surface (source delivery, untrusted-repo data over an HTTP origin). Establish the boundary before any data/source endpoint ships. Do not put the token in the URL (`?token=` leaks to history/logs/Referer).

## Plan

1. Generate a per-start session token; embed it once into the no-store `GET /` HTML (not the URL).
2. Require `Authorization: Bearer <token>` on every API call.
3. Validate `Host` for exact `127.0.0.1:<port>` match; if `Origin` is present, allow only the same origin. No CORS, no cookies.
4. Reject mismatched Host/Origin with 403 (DNS-rebinding defense).

## Acceptance criteria

- [ ] API calls without the Bearer token return 401.
- [ ] Requests with a non-loopback Host header are rejected.
- [ ] Token never appears in the URL/query string.

## Dependencies

Requires {P1_02}.
""",
    ),
    # ---------------------------------------------------------------- P1-04
    dict(
        code="P1-04",
        title="[Security] serve-only HTML: same-origin script/vendor/style resources with a strict CSP",
        labels="enhancement",
        body="""Order: P1-04 | Effort: M | Impact: high | Verdict: code-verified (Codex GPT-5.5)

## Summary

For serve, deliver script/vendor/style as individual same-origin resources and apply a CSP. The current single-file template uses inline style/script/vendor and an external ELK script, so `script-src 'self'` cannot be applied as-is.

## Evidence

- Inline + external ELK in the template (`src/Unilyze/Templates/viewer/index.html:7-9,71-76`, `src/Unilyze/Output/HtmlTemplate.cs:28-35`).

## Plan

1. serve serves `/static/main.js`, `/static/styles.css`, `/static/vendor/*` from embedded resources.
2. Apply `default-src 'none'; script-src 'self'; connect-src 'self'; worker-src 'self'; style-src 'self' 'unsafe-inline'` (style stays unsafe-inline until inline styles are removed; tracked separately).
3. The static single-file `analyze -f html` output keeps its current inline form (two delivery paths; drift-check in P1-13/tests).

## Acceptance criteria

- [ ] serve viewer loads with the CSP and no CSP violations in the console.
- [ ] Static `analyze -f html` output unchanged.

## Dependencies

Requires {P1_02}; composes with {P1_11} (ELK embedding removes the last external script).
""",
    ),
    # ---------------------------------------------------------------- P1-05
    dict(
        code="P1-05",
        title="[Feature] Change detection: FileSystemWatcher for immediacy + periodic fingerprint reconcile for missed events; track all analysis inputs",
        labels="enhancement",
        body="""Order: P1-05 | Effort: L | Impact: high | Verdict: code-verified (Codex GPT-5.5)

## Summary

Detect source changes reliably. FileSystemWatcher gives immediacy but drops events (buffer overflow, atomic-rename, OS differences); a periodic fingerprint reconcile catches the rest. Watch the full analysis input set, not just `.cs`, or the live view silently serves stale results.

## Evidence

- Analysis inputs include csproj/asmdef/generated sources/resolved DLLs (`src/Unilyze/Pipeline/AnalysisPipelineDiscovery.cs:30-68,133-177`, `src/Unilyze/Discovery/AsmdefInfo.cs:18-36`, `src/Unilyze/Discovery/GeneratedSourcesResolver.cs:20-38`, `src/Unilyze/Discovery/UnityDllResolver.cs:53-63`).

## Plan

1. FileSystemWatcher over the project; treat every notification as a dirty hint.
2. Track `.cs`, `.sln/.csproj`, `.asmdef/.meta`, generated sources, resolved reference DLLs, Unity `ProjectVersion`/`ScriptAssemblies`.
3. On watcher `Error` or unknown event, fall back to a full fingerprint rescan.
4. Periodic reconcile (low frequency) compares the input manifest to catch missed changes.
5. Exclude `.unilyze/cache`, `Library/Temp/obj/bin/.git`.

## Acceptance criteria

- [ ] A `.csproj`/reference change triggers re-analysis (not just `.cs`).
- [ ] Simulated dropped event is recovered by the reconcile pass.

## Dependencies

Requires {P1_01}.
""",
    ),
    # ---------------------------------------------------------------- P1-06
    dict(
        code="P1-06",
        title="[Feature] Coalesce change events (~300ms), run a single full analysis, atomically swap the latest snapshot",
        labels="enhancement",
        body="""Order: P1-06 | Effort: M | Impact: high | Verdict: code-verified (Codex GPT-5.5)

## Summary

Debounce change events (~300ms) and run at most one analysis at a time; if newer changes arrive during analysis, re-run for the latest generation afterward. Phase 1 uses full analysis (`incremental:false`) and swaps an immutable latest JSON only on success.

## Evidence

- `--incremental` is Syntax-level only; other levels fall back to full analysis (`src/Unilyze/Pipeline/AnalysisPipeline.cs:42-47`, `src/Unilyze/Pipeline/AnalysisBuildOptions.cs:39-40`). So semantic-level live updates are full re-analysis in Phase 1.

## Plan

1. Channel/timer coalescing with a single-flight semaphore (max one analysis running).
2. Monotonic generation number per accepted change batch.
3. On success, atomically replace the latest immutable snapshot; on failure, keep the previous snapshot and mark it stale (P1-13 surfaces this).

## Acceptance criteria

- [ ] Rapid consecutive saves produce one re-analysis, not N.
- [ ] A failed analysis does not corrupt or blank the served snapshot.

## Dependencies

Requires {P1_05}.
""",
    ),
    # ---------------------------------------------------------------- P1-07
    dict(
        code="P1-07",
        title="[Feature] Long-polling delivery: GET /api/state?after=<generation> + GET /api/snapshot with ETag/If-None-Match",
        labels="enhancement",
        body="""Order: P1-07 | Effort: M | Impact: high | Verdict: code-verified (Codex GPT-5.5)

## Summary

Deliver updates via ETag long-polling over `fetch` (not SSE/WebSocket): `EventSource` cannot send the Authorization header and `HttpListener` SSE framing is error-prone. Long-polling carries the Bearer token, supports `AbortController` cancellation and timeouts on the same path.

## Plan

1. `GET /api/state?after=<generation>` blocks until a newer generation exists or a timeout, then returns the current generation/status.
2. `GET /api/snapshot` returns the full snapshot with an `ETag`; client sends `If-None-Match` (304 when unchanged).
3. Client uses `AbortController` to cancel in-flight polls; server sets `application/json`, `nosniff`, `no-store`.
4. Phase 1 ships full snapshots (no diff patch) — keep it simple; patching is Phase 2 and only if measurement shows transfer is dominant.

## Acceptance criteria

- [ ] Browser receives a new snapshot within one poll cycle of a source change.
- [ ] Unchanged snapshot returns 304; poll cancels cleanly on navigation.

## Dependencies

Requires {P1_03} (auth), {P1_06} (generations).
""",
    ),
    # ---------------------------------------------------------------- P1-08
    dict(
        code="P1-08",
        title="[Refactor] Split viewer into buildDerivedState(data)/applySnapshot(data) reusing existing rebuild(); preserve view state",
        labels="enhancement",
        body="""Order: P1-08 | Effort: L | Impact: high | Verdict: code-verified (Codex GPT-5.5)

## Summary

Make the viewer apply a fresh full snapshot without page reload or Cytoscape re-creation. `rebuild()` already adds/removes elements in a batch; there is no `cy.destroy()`. Factor the one-time init into pure functions so a new snapshot can be applied while preserving the user's view.

## Evidence

- `rebuild()` already does batch edge-remove + node add/remove; no `cy.destroy()` exists (`src/Unilyze/Templates/viewer/main.js:1076-1173`).
- `DATA`, `tl`, `tm`, diff indexes are built once at init (`src/Unilyze/Templates/viewer/main.js:1,44-77`).

## Plan

1. Extract `buildDerivedState(data)` (pure: tl/tm/nsInfo/diff indexes) and `applySnapshot(data)` (rebuild derived state, call existing `rebuild()`).
2. Run the initial load through `applySnapshot(DATA)` too (one path).
3. Preserve pan/zoom, expanded namespaces, selection, and search filters across updates; use `fit:false` on incremental layout so the viewport does not jump (`src/Unilyze/Templates/viewer/main.js:1191-1195,1268-1269,1690`).

## Acceptance criteria

- [ ] Applying a new snapshot updates nodes/edges without full re-creation or reload.
- [ ] pan/zoom/selection/expansion/search are retained across an update.

## Dependencies

Requires {P1_07} (snapshot source). Pairs with the JS unit-test split (follow-up).
""",
    ),
    # ---------------------------------------------------------------- P1-09
    dict(
        code="P1-09",
        title="[Feature] In-browser read-only source API: opaque fileId, allowlist, textContent rendering (no absolute paths to client)",
        labels="enhancement",
        body="""Order: P1-09 | Effort: M | Impact: high | Verdict: code-verified (Codex GPT-5.5)

## Summary

Let users jump from a type to its source inside the browser (read-only). No editor launch / URI scheme. The client never receives absolute paths: serve maps analyzed files to opaque `fileId`s and serves only allowlisted file bodies as `text/plain`, rendered with `textContent` only.

## Evidence

- JSON currently holds absolute `ProjectPath`/`FilePath` (`src/Unilyze/Pipeline/AnalysisResult.cs:12-18`, `src/Unilyze/Metrics/CodeHealthCalculator.cs:49-50`).
- partial types keep only the first declaration's FilePath/StartLine, so MVP navigation targets the type's representative declaration (`src/Unilyze/Pipeline/TypeInfo.cs:54-59,345-359`).

## Plan

1. Build a `fileId -> canonical absolute path` allowlist from the analyzed file set at analysis time; expose only `fileId` + relative display name in JSON.
2. `GET /api/source?fileId=<id>` resolves via the allowlist dictionary (exact-match, not StartsWith) and returns `text/plain`; the API never accepts raw paths.
3. Viewer shows the source in a panel via `textContent`, scrolling to `StartLine`.

## Acceptance criteria

- [ ] Clicking a type opens its source in-browser at the right line.
- [ ] Source API rejects any path not in the allowlist; no absolute path reaches the client.

## Dependencies

Requires {P1_03} (boundary), {P1_07} (snapshot with fileIds).
""",
    ),
    # ---------------------------------------------------------------- P1-10
    dict(
        code="P1-10",
        title="[Test] E2E for serve: CLI, HTTP, watching, analysis failure, auth, source boundary across net8.0/9.0/10.0",
        labels="enhancement",
        body="""Order: P1-10 | Effort: M | Impact: high | Verdict: code-verified (Codex GPT-5.5)

## Summary

Add end-to-end tests for the serve lifecycle on all three target frameworks, building on the existing CLI/MCP process-level E2E suites.

## Evidence

- Three TFMs: `net8.0;net9.0;net10.0` (`src/Unilyze/Unilyze.csproj:4`).

## Plan

1. Process-level E2E: start serve on a temp project, assert URL on stderr, hit `/api/state` and `/api/snapshot` with/without the Bearer token (200/401), modify a `.cs` file and assert a new generation arrives.
2. Failure path: introduce a parse error, assert the previous snapshot is kept and marked stale, then recovery.
3. Source boundary: assert `/api/source` rejects a non-allowlisted path and never leaks absolute paths.
4. Run on net8.0/net9.0/net10.0.

## Acceptance criteria

- [ ] All serve E2E paths pass on the three TFMs.
- [ ] Auth, watcher-driven update, failure-stale, and source-boundary cases covered.

## Dependencies

After {P1_07}, {P1_09} (endpoints exist).
""",
    ),
    # ---------------------------------------------------------------- P1-11
    dict(
        code="P1-11",
        title="[Perf] Embed ELK core + worker as resources; drop the unpkg script and blob worker",
        labels="enhancement,quick-win",
        body="""Order: P1-11 | Effort: S | Impact: medium | Verdict: code-verified (Codex GPT-5.5)

## Summary

Make the viewer fully offline/self-contained and CSP-compatible by embedding ELK like the existing vendored libs. Today ELK core is an external script and the worker is fetched via `importScripts`.

## Evidence

- External ELK + blob/importScripts worker (`src/Unilyze/Templates/viewer/index.html:73`, `src/Unilyze/Templates/viewer/main.js:1198-1202`).
- Existing embedded-resource vendoring pattern to follow (`src/Unilyze/Unilyze.csproj:33-38`).

## Plan

1. Add `elk.bundled.js` and the ELK worker as `EmbeddedResource`s (record SHA256 + MIT/EPL notice in THIRD-PARTY-NOTICES.txt).
2. Replace the unpkg `<script>` and `importScripts` with same-origin/self resources.
3. Verify dagre fallback still works.

## Acceptance criteria

- [ ] Viewer renders ELK layout with no network access.
- [ ] No external script tags remain; THIRD-PARTY-NOTICES updated.

## Dependencies

Enables `script-src 'self'`/`worker-src 'self'` in {P1_04}.
""",
    ),
    # ---------------------------------------------------------------- P1-12
    dict(
        code="P1-12",
        title="[Feature] Add measurement points: analysis phase time, generated JSON size, browser apply time",
        labels="enhancement,quick-win",
        body="""Order: P1-12 | Effort: S | Impact: high | Verdict: code-verified (Codex GPT-5.5)

## Summary

Instrument the live loop so Phase 2 decisions (diff patching, semantic incremental) are driven by data, not guesses. The analysis side already has phase timing; add JSON size and browser apply time.

## Evidence

- discover/parse/compile/semantic/aggregate timing already exists (`src/Unilyze/Pipeline/AnalysisPipeline.cs:63-104`).

## Plan

1. Record per-generation: analysis phase breakdown, snapshot JSON byte size, browser-side derived-state + layout apply time.
2. Expose via a `/api/metrics` (loopback, authed) or stderr log line; keep it lightweight.
3. This is the explicit gate for Phase 2: do not start patching/semantic-incremental until this shows the dominant cost.

## Acceptance criteria

- [ ] Each live update logs analysis time, JSON size, and apply time.
- [ ] Numbers are reproducible on a fixed sample project.

## Dependencies

Requires {P1_06} (generations); informs all Phase 2 perf work.
""",
    ),
    # ---------------------------------------------------------------- P1-13
    dict(
        code="P1-13",
        title="[Feature] Status bar: generation number, analyzing state, last success time, error (stale-result indicator)",
        labels="enhancement,quick-win",
        body="""Order: P1-13 | Effort: S | Impact: high | Verdict: code-verified (Codex GPT-5.5)

## Summary

Surface live state in the viewer so users can trust what they see. On analysis failure, keep the previous snapshot but clearly mark it stale.

## Plan

1. Status bar showing: current generation, "analyzing..." indicator, last successful update time, last error.
2. On failure, show the stale banner over the retained snapshot (ties to {P1_06} swap-on-success behavior).
3. Reflect long-poll connection state (connected / reconnecting).

## Acceptance criteria

- [ ] Status bar shows generation, analyzing, last-success, and error states.
- [ ] A failed analysis is visibly marked stale, not silently shown as current.

## Dependencies

Requires {P1_07} (state endpoint). MVP completion criterion of the roadmap.
""",
    ),
]

EPIC_TITLE = "[Epic] Live visualization roadmap - Phase 1: serve MVP (long-polling + in-browser source)"
EPIC_BODY = """Source: `tasks/visualization-roadmap-v2.md` (Codex GPT-5.5 xhigh revision, post cross-review).

MVP goal: `unilyze serve -p <path>` starts a resident server; after a source change the full
analysis result is reflected without a page reload; analyzing/success/failure/stale states are
explicit; and users can jump from a type to in-browser source.

Fixed design decisions (one option each, not menus):
- Delivery: ETag long-polling + fetch (no SSE/WebSocket).
- Live update: rebuild full snapshot into derived state + existing `rebuild()` (no reload, no cy.destroy()).
- Phase 1 analysis: full re-analysis (incremental is Syntax-only); semantic incremental deferred to Phase 2 behind measurement.
- Source reach: in-browser read-only view (no editor launch).
- Security: loopback-only, HTML-embedded token + Authorization Bearer, Host/Origin checks, fileId allowlist, textContent rendering, CSP.

## Work items
{CHILD_LIST}

## Out of scope (Phase 1)
SSE/WebSocket; JSON diff patching; semantic incremental / ReplaceSyntaxTree optimization; editor launch
(vscode:// etc.); LAN/remote sharing/TLS; serve+MCP single-process; per-line semantic diff classification;
full-project call-graph/CFG extraction; dropping the python3 viewer-build dependency; Kestrel/ASP.NET Core.
"""


def run(cmd, **kw):
    return subprocess.run(cmd, capture_output=True, text=True, **kw)


def main():
    dry = "--dry-run" in sys.argv
    out_path = Path(__file__).resolve().parent / "viz-phase1-issues.json"

    # ensure milestone exists
    if not dry:
        ms = run(["gh", "api", f"repos/{REPO}/milestones",
                  "-f", f"title={MILESTONE_P1}",
                  "-f", "state=open"])
        # 422 = already exists; ignore
        if ms.returncode != 0 and "already_exists" not in ms.stdout + ms.stderr:
            print(f"milestone note: {ms.stderr.strip()}", file=sys.stderr)

    created = {}
    for spec in ISSUES:
        body = spec["body"]
        for code, num in created.items():
            body = body.replace("{" + code.replace("-", "_") + "}", f"#{num}")
        body = re.sub(r"\{P1_(\d\d)\}", lambda m: f"P1-{m.group(1)} (later in this epic)", body)

        if dry:
            print(f"[dry] {spec['code']} [{spec['labels']}] {spec['title']}")
            created[spec["code"]] = 0
            continue

        with tempfile.NamedTemporaryFile("w", suffix=".md", delete=False) as f:
            f.write(body)
            path = f.name
        r = run(["gh", "issue", "create", "--repo", REPO,
                 "--title", spec["title"],
                 "--body-file", path,
                 "--label", spec["labels"],
                 "--milestone", MILESTONE_P1])
        os.unlink(path)
        if r.returncode != 0:
            print(f"FAILED {spec['code']}: {r.stderr}", file=sys.stderr)
            sys.exit(1)
        url = r.stdout.strip()
        num = int(url.rsplit("/", 1)[-1])
        created[spec["code"]] = num
        print(f"{spec['code']} -> #{num} {url}")

    # epic with child links
    child_list = "\n".join(
        f"- {'#' + str(created[s['code']]) if not dry else s['code']} {s['title']}"
        for s in ISSUES
    )
    epic_body = EPIC_BODY.replace("{CHILD_LIST}", child_list)
    if dry:
        print(f"[dry] EPIC {EPIC_TITLE}")
    else:
        with tempfile.NamedTemporaryFile("w", suffix=".md", delete=False) as f:
            f.write(epic_body)
            path = f.name
        r = run(["gh", "issue", "create", "--repo", REPO,
                 "--title", EPIC_TITLE,
                 "--body-file", path,
                 "--label", "epic",
                 "--milestone", MILESTONE_P1])
        os.unlink(path)
        if r.returncode == 0:
            url = r.stdout.strip()
            created["EPIC"] = int(url.rsplit("/", 1)[-1])
            print(f"EPIC -> #{created['EPIC']} {url}")
        else:
            print(f"FAILED EPIC: {r.stderr}", file=sys.stderr)

    if not dry:
        out_path.write_text(json.dumps(created, indent=2))
        print(f"map written: {out_path}")


if __name__ == "__main__":
    main()

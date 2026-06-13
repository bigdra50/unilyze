# Status Line Integration

`unilyze statusline` outputs a compact one-line code health summary for use with [Claude Code's status line](https://docs.anthropic.com/en/docs/claude-code/statusline) and similar tools.

Example output:

```
CH:9.8/5.9 W:9.4 T:7.2 111smells 🔴1 📦66
```

## Legend

| Item | Description |
|------|-------------|
| `CH:avg/min` | Average and minimum Code Health (1.0–10.0) |
| `W:n` | LoC-weighted average Code Health |
| `T:n` | Worst-decile average Code Health |
| `Nsmells` | Warning-level code smells |
| `🔴N` | Critical-level code smells (hidden if 0) |
| `📦N` | Boxing allocations (hidden if 0) |
| `♻N` | Cyclic dependencies (hidden if 0) |
| `[level]` | Analysis level marker, shown only below `Complete` (`[syntax]` / `[core]` / `[full]`) |

Code Health color follows the LoC-weighted category: green ≥9.0, yellow ≥4.0, red <4.0.
Warnings are yellow; criticals and cycles are red; boxing is cyan.

### Optional MI reference metric

Pass `--show-mi` to append `MI:n`, the average Maintainability Index over method-bearing types:

```
CH:9.8/5.9 W:9.4 T:7.2 MI:52 111smells 🔴1 📦66
```

MI is a reference metric and is not part of the default statusline contract.
Its color is green ≥80, yellow ≥60, red <60.

## Claude Code setup

Add to `~/.claude/statusline.sh` (Unity projects only):

```bash
if [[ -d "$PROJECT_DIR/Assets" ]] && [[ -d "$PROJECT_DIR/ProjectSettings" ]]; then
    unilyze statusline -p "$PROJECT_DIR" --background-refresh
fi
```

`--background-refresh` never blocks on analysis: it returns cached output immediately and refreshes stale or missing caches in a detached background process. On first run with an empty cache, you may see one blank line until the background refresh completes.

## Caching

Results are cached per project (default TTL 60 seconds). Override with `--refresh <seconds>`. Run `unilyze statusline --help` for the platform cache directory.

The `statusline` subcommand reads `.unilyze.json` and global config automatically — no extra CLI flags needed for exclude directories or baseline suppression (`--baseline`).

## Options reference

| Flag | Purpose |
|------|---------|
| `-p, --path` | Project root (default: `.`) |
| `--refresh` | Cache lifetime in seconds (default: 60) |
| `--level` | Pin analysis level: `syntax`, `core`, `full`, `complete` |
| `--baseline` | Suppress known smells from a baseline file in counts |
| `--codehealth-v1` | Display legacy CodeHealth v1 during the one-release migration window |
| `--show-mi` | Append the reference Maintainability Index metric |
| `--background-refresh` | Non-blocking refresh (recommended for status bars) |
| `--verbose` / `--quiet` | Stderr diagnostics |

See also the [agent integration tutorial](./tutorials/agent-integration.md) for snapshot conventions used alongside statusline workflows.

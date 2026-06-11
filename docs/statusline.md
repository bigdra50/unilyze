# Status Line Integration

`unilyze statusline` outputs a compact one-line code health summary for use with [Claude Code's status line](https://docs.anthropic.com/en/docs/claude-code/statusline) and similar tools.

Example output:

```
CH:9.8/5.9 MI:52 111smells 🔴1 📦66
```

## Legend

| Item | Description |
|------|-------------|
| `CH:avg/min` | Average and minimum Code Health (1.0–10.0) |
| `MI:n` | Average Maintainability Index over method-bearing types (green ≥80, yellow ≥60, red <60) |
| `Nsmells` | Warning-level code smells |
| `🔴N` | Critical-level code smells (hidden if 0) |
| `📦N` | Boxing allocations (hidden if 0) |
| `♻N` | Cyclic dependencies (hidden if 0) |
| `[level]` | Analysis level marker, shown only below `Complete` (`[syntax]` / `[core]` / `[full]`) |

Color coding matches the CLI help: Code Health green ≥8.0 / yellow ≥5.0; MI green ≥80 / yellow ≥60; warnings yellow; criticals red; boxing cyan; cycles red; level marker yellow.

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
| `--background-refresh` | Non-blocking refresh (recommended for status bars) |
| `--verbose` / `--quiet` | Stderr diagnostics |

See also the [agent integration tutorial](./tutorials/agent-integration.md) for snapshot conventions used alongside statusline workflows.

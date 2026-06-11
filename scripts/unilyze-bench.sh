#!/usr/bin/env bash
# Run unilyze on one or more project paths and record wall time, exit code, and peak RSS.
set -euo pipefail

usage() {
  cat <<'EOF'
Usage:
  bash scripts/unilyze-bench.sh [options] <project-path>...

Options:
  --level <level>     Analysis level: SyntaxOnly | CoreEngine | Complete (default: Complete)
  --format <format>   Output format: json | html (default: json)
  --output-dir <dir>  Directory for per-run JSON output (default: /tmp/unilyze-bench)
  -h, --help          Show this help

Environment:
  UNILYZE_CMD         Command to run (default: dotnet run --project src/Unilyze -c Release --framework net10.0 --)
EOF
}

level="Complete"
format="json"
output_dir="/tmp/unilyze-bench"
unilyze_cmd="${UNILYZE_CMD:-dotnet run --project src/Unilyze -c Release --framework net10.0 --}"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --level)
      level="${2:-}"
      shift 2
      ;;
    --format)
      format="${2:-}"
      shift 2
      ;;
    --output-dir)
      output_dir="${2:-}"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    --*)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 1
      ;;
    *)
      break
      ;;
  esac
done

if [[ $# -eq 0 ]]; then
  usage >&2
  exit 1
fi

mkdir -p "$output_dir"

time_flags=()
case "$(uname -s)" in
  Darwin)
    time_bin="/usr/bin/time"
    time_flags=(-l)
    rss_label="maximum resident set size"
    ;;
  Linux)
    time_bin="/usr/bin/time"
    time_flags=(-v)
    rss_label="Maximum resident set size (kbytes)"
    ;;
  *)
    echo "Peak RSS measurement requires macOS (/usr/bin/time -l) or Linux (/usr/bin/time -v)." >&2
    exit 1
    ;;
esac

printf '| %-28s | %-10s | %8s | %12s | %s |\n' "Project" "Level" "rc" "Peak RSS" "Wall (s)"
printf '|%s|\n' "------------------------------|------------|----------|--------------|----------"

for project in "$@"; do
  if [[ ! -e "$project" ]]; then
    echo "Project path not found: $project" >&2
    exit 1
  fi

  name="$(basename "$project")"
  out_file="$output_dir/${name}-${level}.json"
  time_log="$(mktemp "${TMPDIR:-/tmp}/unilyze-bench-time.XXXXXX")"

  set +e
  # shellcheck disable=SC2086
  $time_bin "${time_flags[@]}" -o "$time_log" \
    $unilyze_cmd \
    -p "$project" \
    --level "$level" \
    -f "$format" \
    -o "$out_file" \
    >/dev/null 2>&1
  rc=$?
  set -e

  wall_s="$(awk '/real/ {print $1}' "$time_log" | sed 's/m/ * 60 + /; s/s$//' | bc 2>/dev/null || echo "?")"
  peak_rss="?"
  if [[ "$(uname -s)" == "Darwin" ]]; then
    # macOS /usr/bin/time -l: "<bytes>  maximum resident set size" (no colon)
    peak_rss="$(awk '/maximum resident set size/ {printf "%.1f MB", $1/1024/1024}' "$time_log")"
  else
    peak_rss="$(awk -F': ' '/Maximum resident set size/ {print $2}' "$time_log" | awk '{printf "%.1f MB", $1/1024}')"
  fi

  printf '| %-28s | %-10s | %8s | %12s | %s |\n' "$name" "$level" "$rc" "$peak_rss" "$wall_s"
  rm -f "$time_log"
done

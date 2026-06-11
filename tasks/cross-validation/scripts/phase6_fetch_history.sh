#!/usr/bin/env bash
# Ensure corpus repos have full git history for bug-fix density analysis.
set -euo pipefail

REPO_ROOT="/tmp/cross-validation-repos"

ensure_full_history() {
  local dir="$1"
  if [[ ! -d "$dir/.git" ]]; then
    echo "missing repo: $dir" >&2
    return 1
  fi
  if git -C "$dir" rev-parse --is-shallow-repository | grep -q true; then
    echo "Unshallowing $dir"
    git -C "$dir" fetch --unshallow
  fi
  echo "$(basename "$dir"): $(git -C "$dir" rev-list --count HEAD) commits"
}

ensure_full_history "$REPO_ROOT/bossroom"
ensure_full_history "$REPO_ROOT/HelloMarioFramework"
ensure_full_history "$REPO_ROOT/UniTask"
ensure_full_history "$REPO_ROOT/VContainer"

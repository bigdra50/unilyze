#!/usr/bin/env bash
# Re-measure cross-validation corpus with current unilyze (SyntaxOnly, metricsVersion current).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
DATA_DIR="$ROOT/tasks/cross-validation/data"
REPO_ROOT="/tmp/cross-validation-repos"
UNILYZE=(dotnet run --project "$ROOT/src/Unilyze" -c Release --framework net10.0 --no-build --)

clone_repo() {
  local url="$1"
  local dir="$2"
  if [[ -d "$dir/.git" ]]; then
    git -C "$dir" fetch --depth 1 origin
    git -C "$dir" reset --hard "@{u}" 2>/dev/null || true
  else
    git clone --depth 1 "$url" "$dir"
  fi
  git -C "$dir" rev-parse HEAD
}

mkdir -p "$REPO_ROOT"

SHA_BOSSROOM="$(clone_repo \
  https://github.com/Unity-Technologies/com.unity.multiplayer.samples.coop \
  "$REPO_ROOT/bossroom")"
SHA_HMF="$(clone_repo \
  https://github.com/HelloFangaming/HelloMarioFramework \
  "$REPO_ROOT/HelloMarioFramework")"
SHA_UNITASK="$(clone_repo \
  https://github.com/Cysharp/UniTask \
  "$REPO_ROOT/UniTask")"
SHA_VCONTAINER="$(clone_repo \
  https://github.com/hadashiA/VContainer \
  "$REPO_ROOT/VContainer")"
SHA_SELF="$(git -C "$ROOT" rev-parse HEAD)"

measure() {
  local key="$1"
  local project_path="$2"
  local output="$DATA_DIR/unilyze-${key}-mv2.json"
  echo "Measuring $key -> $output"
  "${UNILYZE[@]}" -p "$project_path" -f json --level syntax -o "$output"
  jq -r '.metricsVersion, .analysisLevel, (.typeMetrics | length)' "$output"
}

measure bossroom "$REPO_ROOT/bossroom"
measure hmf "$REPO_ROOT/HelloMarioFramework"
measure unitask "$REPO_ROOT/UniTask/src/UniTask"
measure vcontainer "$REPO_ROOT/VContainer/VContainer"
measure self "$ROOT/src/Unilyze"

SHA_FILE="$DATA_DIR/phase6-corpus-shas.json"
jq -n \
  --arg bossroom "$SHA_BOSSROOM" \
  --arg hmf "$SHA_HMF" \
  --arg unitask "$SHA_UNITASK" \
  --arg vcontainer "$SHA_VCONTAINER" \
  --arg self "$SHA_SELF" \
  '{
    bossroom: {repo: "Unity-Technologies/com.unity.multiplayer.samples.coop", sha: $bossroom},
    hmf: {repo: "HelloFangaming/HelloMarioFramework", sha: $hmf},
    unitask: {repo: "Cysharp/UniTask", sha: $unitask},
    vcontainer: {repo: "hadashiA/VContainer", sha: $vcontainer},
    self: {repo: "bigdra50/unilyze", sha: $self}
  }' > "$SHA_FILE"

echo "Wrote corpus SHAs to $SHA_FILE"

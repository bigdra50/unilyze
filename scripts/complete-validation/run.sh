#!/usr/bin/env bash

set -euo pipefail

fixture_root="tests/fixtures/golden"
license_file="/tmp/unity-license.ulf"
editors_root="/editors"

if [[ -z "${UNITY_LICENSE:-}" ]]; then
  echo "UNITY_LICENSE is required for Complete-level validation." >&2
  exit 1
fi

if command -v unity-editor >/dev/null 2>&1; then
  unity_editor="$(command -v unity-editor)"
elif [[ -x /opt/unity/Editor/Unity ]]; then
  unity_editor="/opt/unity/Editor/Unity"
else
  echo "Unity editor executable was not found in the GameCI image." >&2
  exit 1
fi

cleanup() {
  rm -f "$license_file"
}
trap cleanup EXIT

printf '%s' "$UNITY_LICENSE" > "$license_file"
chmod 600 "$license_file"

"$unity_editor" \
  -batchmode \
  -nographics \
  -quit \
  -manualLicenseFile "$license_file" \
  -logFile -

"$unity_editor" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "$fixture_root" \
  -logFile -

if ! find "$fixture_root/Library/ScriptAssemblies" -maxdepth 1 -name '*.dll' -print -quit | grep -q .; then
  echo "Unity batch compilation did not produce Library/ScriptAssemblies DLLs." >&2
  exit 1
fi

unity_version="$(
  sed -n 's/^m_EditorVersion:[[:space:]]*//p' \
    "$fixture_root/ProjectSettings/ProjectVersion.txt" \
    | head -n 1
)"
if [[ -z "$unity_version" ]]; then
  echo "m_EditorVersion was not found in ProjectVersion.txt." >&2
  exit 1
fi

mkdir -p "$editors_root"
ln -sfn /opt/unity "$editors_root/$unity_version"
export UNILYZE_EDITORS_ROOT="$editors_root"
export UNILYZE_COMPLETE_VALIDATION=1

rm -f "$fixture_root/Golden.csproj"

dotnet test tests/Unilyze.Tests/Unilyze.Tests.csproj \
  -f net10.0 \
  --filter GoldenCorpusComplete \
  -v minimal

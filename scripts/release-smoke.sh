#!/usr/bin/env bash
set -euo pipefail

tool_name="Unilyze"
tool_command="unilyze"
version=""
package_source=""
install_root=""
keep_temp="false"

usage() {
  cat <<'EOF'
Usage:
  bash scripts/release-smoke.sh --package-source <dir> --version <version> [options]

Options:
  --package-source <dir>  Directory containing the .nupkg to install from
  --version <version>     Package version to install
  --tool-name <name>      NuGet package id (default: Unilyze)
  --tool-command <name>   Installed command name (default: unilyze)
  --install-root <dir>    Tool install directory (default: temporary directory)
  --keep-temp             Keep the temporary install directory after execution
  -h, --help              Show this help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --package-source)
      package_source="${2:-}"
      shift 2
      ;;
    --version)
      version="${2:-}"
      shift 2
      ;;
    --tool-name)
      tool_name="${2:-}"
      shift 2
      ;;
    --tool-command)
      tool_command="${2:-}"
      shift 2
      ;;
    --install-root)
      install_root="${2:-}"
      shift 2
      ;;
    --keep-temp)
      keep_temp="true"
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

if [[ -z "$package_source" || -z "$version" ]]; then
  usage >&2
  exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet command not found on PATH" >&2
  exit 1
fi

if [[ ! -d "$package_source" ]]; then
  echo "Package source directory not found: $package_source" >&2
  exit 1
fi

if [[ -z "$install_root" ]]; then
  install_root="$(mktemp -d "${TMPDIR:-/tmp}/unilyze-release-smoke.XXXXXX")"
  cleanup_install_root="true"
else
  mkdir -p "$install_root"
  cleanup_install_root="false"
fi

case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*)
    tool_path="$install_root/${tool_command}.exe"
    ;;
  *)
    tool_path="$install_root/${tool_command}"
    ;;
esac

on_exit() {
  local exit_code=$?
  if [[ $exit_code -ne 0 ]]; then
    {
      echo
      echo "Release smoke failed."
      echo "dotnet: $(command -v dotnet)"
      echo "DOTNET_ROOT: ${DOTNET_ROOT-<unset>}"
      echo "DOTNET_MULTILEVEL_LOOKUP: ${DOTNET_MULTILEVEL_LOOKUP-<unset>}"
      echo "Package source: $package_source"
      echo "Install root: $install_root"
      dotnet --version || true
      dotnet --list-runtimes || true
      if [[ -e "$tool_path" ]]; then
        ls -l "$tool_path" || true
      fi
    } >&2
  fi

  if [[ "$cleanup_install_root" == "true" && "$keep_temp" != "true" ]]; then
    rm -rf "$install_root"
  fi
}

trap on_exit EXIT

echo "Release smoke environment"
echo "  dotnet: $(command -v dotnet)"
echo "  DOTNET_ROOT: ${DOTNET_ROOT-<unset>}"
echo "  package source: $package_source"
echo "  install root: $install_root"
dotnet --version

dotnet tool install \
  --tool-path "$install_root" \
  "$tool_name" \
  --add-source "$package_source" \
  --version "$version"

"$tool_path" --version

echo "Release smoke passed."

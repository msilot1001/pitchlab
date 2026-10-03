#!/usr/bin/env bash
set -euo pipefail

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
version_file="$root/ProjectSettings/ProjectVersion.txt"
if [[ ! -f "$version_file" ]]; then
  echo "Unity project is absent: install Unity 6.3 LTS and create the URP project first." >&2
  exit 2
fi
version=$(sed -n 's/^m_EditorVersion: //p' "$version_file" | head -n 1)
if [[ "$version" != 6000.3.* ]]; then
  echo "Expected Unity 6.3 LTS; project requests '${version:-unknown}'." >&2
  exit 2
fi
if ! command -v unity >/dev/null 2>&1; then
  echo "Unity CLI is missing from PATH." >&2
  exit 2
fi
if ! unity editors path "$version" --format json >/dev/null 2>&1; then
  echo "Required Unity Editor $version is not installed." >&2
  exit 2
fi
mkdir -p "$root/TestResults"
unity test "$root" --editor-version "$version" --mode PlayMode --output "$root/TestResults/playmode.xml" --timeout 600

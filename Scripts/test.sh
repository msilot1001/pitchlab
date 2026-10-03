#!/usr/bin/env bash
# Usage: Scripts/test.sh EditMode|PlayMode
# Runs tests in the open Editor (via Unity Pipeline) when this project is open; otherwise in batch mode.
set -euo pipefail

mode=${1:-}
if [[ "$mode" != EditMode && "$mode" != PlayMode ]]; then
  echo "Usage: $0 EditMode|PlayMode" >&2
  exit 2
fi

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
version_file="$root/ProjectSettings/ProjectVersion.txt"
if [[ ! -f "$version_file" ]]; then
  echo "Unity project is absent: ProjectSettings/ProjectVersion.txt not found." >&2
  exit 2
fi
version=$(sed -n 's/^m_EditorVersion: //p' "$version_file" | head -n 1)
if [[ "$version" != 6000.3.* ]]; then
  echo "Expected Unity 6.3 LTS; project requests '${version:-unknown}'." >&2
  exit 2
fi
for tool in unity jq; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "$tool is missing from PATH." >&2
    exit 2
  fi
done

# unity status exits non-zero when no Editor is open.
editor_open=$({ unity status --format json 2>/dev/null || true; } |
  jq --arg p "$root" '[.data.instances[]? | select(.project == $p)] | length')

if [[ "$editor_open" == 0 ]]; then
  if ! unity editors path "$version" --format json >/dev/null 2>&1; then
    echo "Required Unity Editor $version is not installed." >&2
    exit 2
  fi
  mkdir -p "$root/TestResults"
  output="$root/TestResults/$(echo "$mode" | tr '[:upper:]' '[:lower:]').xml"
  rm -f "$output"
  exec unity test "$root" --editor-version "$version" --mode "$mode" --output "$output" --timeout 600
fi

echo "Editor has this project open; running $mode tests there."
cmd() { unity command "$@" --project-path "$root" --result-only --timeout 600; }
# Imports and domain reloads briefly drop the Pipeline connection; wait until the Editor is idle.
wait_ready() {
  for _ in $(seq 1 120); do
    if cmd editor_status 2>/dev/null | jq -e '.status == "ready" and (.compiling | not)
        and (.domainReloadInProgress | not)' >/dev/null 2>&1; then return 0; fi
    /bin/sleep 1
  done
  echo "Editor did not become ready." >&2
  return 1
}

wait_ready
# recompile exits non-zero and lists errors when scripts fail to compile. It can also lose the
# connection during the reload it triggers, so retry once after the Editor settles.
if ! unity recompile --project-path "$root" 2>/dev/null; then
  wait_ready
  unity recompile --project-path "$root"
fi
wait_ready
if [[ "$mode" == EditMode ]]; then
  result=$(cmd run_tests --mode editor)
  ok=$(jq '.success == true' <<<"$result")
  results=$(jq -c '.Results // []' <<<"$result")
else
  # Entering Play Mode reloads the domain, so PlayMode tests must run async and be polled.
  rm -f "$root/Temp/pipeline_test_status.json"
  cmd run_tests --mode playmode --async_tests | jq -e '.success' >/dev/null
  status='{}'
  for _ in $(seq 1 300); do
    status=$(cmd test_status 2>/dev/null || echo '{}')
    if jq -e '.status == "completed"' <<<"$status" >/dev/null 2>&1; then break; fi
    /bin/sleep 2
  done
  ok=$(jq '.status == "completed"' <<<"$status")
  results=$(jq -c '.results // []' <<<"$status")
fi

jq -r --arg mode "$mode" --argjson ok "$ok" '
  (map(select(.Status == "Failed")) | .[] | "FAILED \(.FullName): \(.Message)"),
  "\($mode): \(length) total, \(map(select(.Status == "Passed")) | length) passed, \(map(select(.Status == "Failed")) | length) failed\(if $ok then "" else " (run did not complete)" end)"
' <<<"$results"
[[ "$ok" == true ]] && jq -e 'all(.Status != "Failed")' <<<"$results" >/dev/null

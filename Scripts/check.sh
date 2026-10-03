#!/usr/bin/env bash
set -euo pipefail

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
"$root/Scripts/format.sh"
"$root/Scripts/test.sh" EditMode
"$root/Scripts/test.sh" PlayMode

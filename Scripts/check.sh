#!/usr/bin/env bash
set -euo pipefail

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
"$root/Scripts/format.sh"
"$root/Scripts/test-editmode.sh"
"$root/Scripts/test-playmode.sh"

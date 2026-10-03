#!/usr/bin/env bash
set -euo pipefail

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$root"
git diff --check
git diff --cached --check
echo "Whitespace checks passed. No C# formatter is configured yet."

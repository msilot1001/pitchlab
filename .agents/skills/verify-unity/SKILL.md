---
name: verify-unity
description: Use when verifying Unity compilation, tests, scene behavior, or Editor state for Pitchlab.
---

Confirm the exact project and Editor version. Run `Scripts/check.sh` or targeted test scripts. Inspect Console and relevant scene behavior through Unity MCP when connected; reviewers must not mutate scenes, assets, or settings. Check Git status for unrelated changes. Distinguish passing tests from checks that could not run and report the blocker.
